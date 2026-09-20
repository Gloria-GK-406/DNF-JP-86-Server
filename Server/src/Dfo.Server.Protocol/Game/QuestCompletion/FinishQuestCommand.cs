using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Protocol.Game.QuestCompletion;

/// <summary>Why a completion was refused, in the code the client's answer carries.</summary>
/// <remarks>
/// One number, which is all the reference server answers with here: every refusal path in its
/// completion service ends at <c>QuestFinishResult.Fail(22)</c>. An unknown quest, an unmet
/// objective, a full bag and a quest nobody is carrying are indistinguishable on the wire.
/// </remarks>
public enum QuestFinishRefusal : byte
{
    /// <summary>The completion cannot be granted. The reference server's only code for this frame.</summary>
    Unusable = 22,
}

/// <summary>What became of one attempt to hand a quest in.</summary>
/// <param name="Experience">What the quest paid, which is not the character's new total.</param>
/// <param name="NewExperience">The total afterwards, which is what the experience frame carries.</param>
public sealed record QuestFinishOutcome(
    bool Finished,
    ushort QuestId,
    byte ObjectiveKind,
    uint Experience,
    IReadOnlyList<FinishedQuestItem> Items,
    byte NewLevel,
    uint NewExperience,
    QuestFinishRefusal? Refusal = null,
    QuestProgressSnapshot? QuestProgress = null,
    IReadOnlyList<AccountMaterialChange>? AccountMaterials = null)
{
    public static QuestFinishOutcome Refuse(
        ushort questId, QuestProgressSnapshot? questProgress = null) =>
        new(false, questId, 0, 0, [], 0, 0, QuestFinishRefusal.Unusable, questProgress);
}

/// <summary>One character handing one quest in.</summary>
/// <param name="AcquiredAtUnixTime">Stamped into every item the completion hands over.</param>
/// <remarks>
/// Where the character stands is deliberately NOT carried here. It is read from the store inside
/// the transaction, because the caller's copy of it is the entry-time snapshot and that snapshot
/// never moves: pricing a reward from it means every completion in a session is priced at the
/// level the session opened at, and adding to it means every completion writes the same total.
/// Both happened -- on 2026-08-27 a quest finished twice paid <c>total=1101</c> both times.
/// </remarks>
public sealed record FinishQuestCommand(
    AccountId AccountId,
    CharacterId CharacterId,
    ushort QuestId,
    ushort RewardSelection,
    ushort CompletionCount,
    uint AcquiredAtUnixTime) : ICommand<QuestFinishOutcome>;

public sealed class QuestFinishRejectedException : Exception
{
    public QuestFinishRejectedException(ushort questId, string message)
        : base(message) => this.QuestId = questId;

    internal QuestFinishRejectedException(ushort questId, string message, Exception innerException)
        : base(message, innerException) => this.QuestId = questId;

    public ushort QuestId { get; }
}

/// <summary>
/// Takes the quest out of the log, prices what it pays, and hands it over.
/// </summary>
/// <remarks>
/// The quest leaving the carried list, entering the cleared list, rewards, experience, and any
/// dependent quest progress settle together or not at all. A
/// completion that granted the items and then failed to record the clear would be a quest the
/// player could hand in twice.
///
/// The order is the log first, then the items. The log's gate is the cheap one and the one most
/// likely to refuse -- a quest not carried, an objective unmet -- and refusing before touching the
/// bag keeps a refused completion from having to unwind a placement.
///
/// Every ordinary refusal is decided before the log writes. A refusal discovered afterwards throws
/// <see cref="QuestFinishRejectedException"/> through the command bus, so the unit-of-work boundary
/// disposes without committing and rolls every enlisted store back. The package handler catches that
/// dedicated failure outside the boundary and maps it to the reference server's wire code 22.
/// </remarks>
public sealed class FinishQuestCommandHandler(
    IQuestDefinitionDomainService quests,
    ICharacterQuestLogDomainService logs,
    ICharacterContainerDomainService containers,
    ICharacterExperienceDomainService grant,
    ICharacterDomainService characters,
    IQuestProgressDomainService questProgress,
    IItemGrantDomainService itemGrants)
    : ICommandHandler<FinishQuestCommand, QuestFinishOutcome>
{
    public async ValueTask<QuestFinishOutcome> HandleAsync(
        CommandExecutionContext context,
        FinishQuestCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);

        if (context.ExecutionKind is not CommandExecutionKind.Transactional)
        {
            throw new InvalidOperationException(
                "Handing a quest in writes the quest lists, the bag and the character together, "
                + $"so it has to be registered transactional; this execution is {context.ExecutionKind}.");
        }

        var definition = await quests.FindAsync(command.QuestId, cancellationToken)
            .ConfigureAwait(false);
        if (definition is null)
            return QuestFinishOutcome.Refuse(command.QuestId);

        var character = characters.Get(command.CharacterId);
        var identity = await character.IdentityAsync(cancellationToken).ConfigureAwait(false);
        var progress = await character.ProgressAsync(cancellationToken).ConfigureAwait(false);
        if (identity is null || progress is null)
            return QuestFinishOutcome.Refuse(command.QuestId);

        var resolution = quests.ResolveRewards(
            definition,
            new QuestRewardCandidate(identity.Job, identity.GrowType, progress.Level),
            command.RewardSelection,
            command.CompletionCount);
        if (!resolution.IsValid)
            return QuestFinishOutcome.Refuse(command.QuestId);
        var reward = resolution.Plan!;

        try
        {
            var log = logs.LogOf(command.AccountId, command.CharacterId);
            // A carried-item objective is a projection of the current bag, not proof that the
            // item still exists. Recheck before the log can clear the quest and grant rewards.
            var holdingsProgress = definition.SeekingItems is { Count: > 0 }
                ? (await questProgress.ReportAsync(
                    command.AccountId, command.CharacterId,
                    [new QuestHoldingsChanged()], cancellationToken).ConfigureAwait(false))
                    .NotificationSnapshot
                : null;
            var verdict = await log.FinishAsync(command.QuestId, cancellationToken)
                .ConfigureAwait(false);
            if (verdict is not QuestFinishVerdict.Finished)
                return QuestFinishOutcome.Refuse(command.QuestId, holdingsProgress);

            var placed = new List<FinishedQuestItem>();
            var accountChanges = new List<AccountMaterialChange>();
            if (reward.Items.Count > 0)
            {
                var container = containers.FindContainer(
                    command.CharacterId, InventoryContainerType.Main);
                if (container is null)
                    throw Rejected(command, "the character has no main item container");

                foreach (var (itemId, count) in reward.Items)
                {
                    var granted = await itemGrants.GrantAsync(
                            command.AccountId, container, itemId, count,
                            cancellationToken)
                        .ConfigureAwait(false);
                    switch (granted)
                    {
                        case ItemGrantOutcome.CharacterItem { Placement: ItemPlacementOutcome.AddedToPile topped }:
                            placed.Add(new FinishedQuestItem(
                                checked((ushort)topped.SlotIndex), (uint)itemId, (uint)topped.Added));
                            break;

                        case ItemGrantOutcome.CharacterItem { Placement: ItemPlacementOutcome.PutInEmptySlot opened }:
                            placed.Add(new FinishedQuestItem(
                                checked((ushort)opened.SlotIndex), (uint)itemId, (uint)opened.Count));
                            break;

                        case ItemGrantOutcome.AccountMaterial account:
                            accountChanges.Add(account.Change);
                            placed.Add(new FinishedQuestItem(
                                checked((ushort)account.Change.VirtualSlotIndex),
                                (uint)itemId,
                                (uint)account.Change.AddedCount));
                            break;

                        case ItemGrantOutcome.CharacterItem { Placement: ItemPlacementOutcome.NoRoom }:
                        case ItemGrantOutcome.CharacterItem { Placement: ItemPlacementOutcome.UnknownItem }:
                            throw Rejected(command, $"item {itemId} could not be placed");

                        default:
                            throw new ArgumentOutOfRangeException(
                                nameof(command), granted, "No reading was decided for this outcome.");
                    }
                }
            }

            if (reward.Gold > 0)
            {
                var before = await character.GoldAsync(cancellationToken).ConfigureAwait(false);
                var after = await character.AddGoldAsync(reward.Gold, cancellationToken)
                    .ConfigureAwait(false);
                if (after < before
                    || await character.IdentityAsync(cancellationToken).ConfigureAwait(false) is null)
                {
                    throw Rejected(command, "the character disappeared while gold was being granted");
                }

                var gained = after - before;
                if (gained > 0)
                    placed.Add(new FinishedQuestItem(0, 0, checked((uint)gained)));
            }

            // Through the same grant a kill uses. What a quest pays is its own rule -- the archive's
            // quest table, nothing to do with monsters -- but raising the total, crossing thresholds
            // and persisting are not, and a second copy of those is what made this command write the
            // same total for two completions of one quest.
            var award = await grant.AwardAsync(
                command.CharacterId,
                _ => new ExperienceValuation(reward.Experience, []),
                cancellationToken).ConfigureAwait(false);

            // No character to award to, or a row that would not take the write. Both were refusals
            // before this command shared its granting, and both must stay refusals: reporting a
            // completion whose award did not happen would tell the client its level and total from a
            // placeholder, and on the first of the two that placeholder is level 1 with no experience.
            if (award is null)
                throw Rejected(command, "experience could not be granted");

            var facts = new List<QuestProgressFact> { new QuestCleared(command.QuestId) };
            if (placed.Count > 0)
                facts.Add(new QuestHoldingsChanged());
            var questReport = await questProgress.ReportAsync(
                command.AccountId, command.CharacterId, facts, cancellationToken)
                .ConfigureAwait(false);

            return new QuestFinishOutcome(
                true,
                command.QuestId,
                ObjectiveKindOf(definition.ObjectiveTypeCode),
                award.Gained,
                placed,
                award.Level,
                award.Total,
                QuestProgress: questReport.NotificationSnapshot,
                AccountMaterials: accountChanges);
        }
        catch (Exception exception)
            when (exception is not (QuestFinishRejectedException or OperationCanceledException))
        {
            throw Rejected(command, "settlement failed", exception);
        }
    }

    private static QuestFinishRejectedException Rejected(
        FinishQuestCommand command,
        string reason) =>
        new(command.QuestId, $"Quest {command.QuestId} completion was rejected after settlement began: {reason}.");

    private static QuestFinishRejectedException Rejected(
        FinishQuestCommand command,
        string reason,
        Exception innerException) =>
        new(
            command.QuestId,
            $"Quest {command.QuestId} completion was rejected after settlement began: {reason}.",
            innerException);

    /// <summary>
    /// What the answer calls the objective, in the reference server's numbering.
    /// </summary>
    /// <remarks>
    /// Its <c>QuestFinishType</c>, transcribed. The numbers are not consecutive and the gaps are
    /// its own -- 3, 5, 7, 8 and 9 are unassigned -- so an objective this table does not name
    /// answers 0 rather than being counted into a gap. Confirmed for <c>meet npc</c> = 4 against
    /// the 2026-08-27 capture of quest 1016.
    /// </remarks>
    private static byte ObjectiveKindOf(string? objectiveTypeCode) =>
        objectiveTypeCode?.Trim().ToLowerInvariant() switch
        {
            "seeking" => 0x00,
            "condition under clear" => 0x01,
            "hunt monster" => 0x02,
            "meet npc" => 0x04,
            "hunt enemy" => 0x06,
            "custom quest" => 0x0A,
            "use item" => 0x0B,
            _ => 0x00,
        };
}
