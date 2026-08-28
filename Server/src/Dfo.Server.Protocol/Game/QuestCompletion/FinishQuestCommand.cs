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
    QuestFinishRefusal? Refusal = null)
{
    public static QuestFinishOutcome Refuse(ushort questId) =>
        new(false, questId, 0, 0, [], 0, 0, QuestFinishRefusal.Unusable);
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
    uint AcquiredAtUnixTime) : ICommand<QuestFinishOutcome>;

/// <summary>
/// Takes the quest out of the log, prices what it pays, and hands it over.
/// </summary>
/// <remarks>
/// Four things settle together or not at all: the quest leaving the carried list, the quest
/// entering the cleared list, the items entering the bag, and the character's new experience. A
/// completion that granted the items and then failed to record the clear would be a quest the
/// player could hand in twice.
///
/// The order is the log first, then the items. The log's gate is the cheap one and the one most
/// likely to refuse -- a quest not carried, an objective unmet -- and refusing before touching the
/// bag keeps a refused completion from having to unwind a placement. When the bag then refuses,
/// the whole command is abandoned rather than compensated: the transaction is what takes the clear
/// back, which is exactly what it is for.
/// </remarks>
public sealed class FinishQuestCommandHandler(
    IQuestDefinitionSource quests,
    IQuestParameterSource questParameters,
    ICharacterQuestLogDomainService logs,
    ICharacterContainerDomainService containers,
    ICharacterTable characters,
    IDungeonExperienceProgressionPolicySource progression)
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

        var definition = await quests.GetAsync(command.QuestId, cancellationToken)
            .ConfigureAwait(false);
        if (definition is null)
            return QuestFinishOutcome.Refuse(command.QuestId);

        var progress = await characters
            .FindProgressAsync(command.CharacterId, cancellationToken).ConfigureAwait(false);
        if (progress is null)
            return QuestFinishOutcome.Refuse(command.QuestId);
        var characterLevel = checked((byte)Math.Clamp(progress.Level, 1, byte.MaxValue));
        var characterExperience = checked((uint)Math.Max(0, progress.Experience));

        var log = logs.LogOf(command.AccountId, command.CharacterId);
        var verdict = await log.FinishAsync(command.QuestId, cancellationToken)
            .ConfigureAwait(false);
        if (verdict is not QuestFinishVerdict.Finished)
            return QuestFinishOutcome.Refuse(command.QuestId);

        var placed = new List<FinishedQuestItem>();
        var rewards = ItemRewardsOf(definition);
        if (rewards.Count > 0)
        {
            var container = containers.FindContainer(
                command.CharacterId, InventoryContainerType.Main);
            if (container is null)
                return QuestFinishOutcome.Refuse(command.QuestId);

            foreach (var (itemId, count) in rewards)
            {
                var outcome = await container.AddAsync(itemId, count, cancellationToken)
                    .ConfigureAwait(false);
                switch (outcome)
                {
                    case ItemPlacementOutcome.AddedToPile topped:
                        placed.Add(new FinishedQuestItem(
                            checked((ushort)topped.SlotIndex), (uint)itemId, (uint)topped.Added));
                        break;

                    case ItemPlacementOutcome.PutInEmptySlot opened:
                        placed.Add(new FinishedQuestItem(
                            checked((ushort)opened.SlotIndex), (uint)itemId, (uint)opened.Count));
                        break;

                    case ItemPlacementOutcome.NoRoom:
                    case ItemPlacementOutcome.UnknownItem:
                        return QuestFinishOutcome.Refuse(command.QuestId);

                    default:
                        throw new ArgumentOutOfRangeException(
                            nameof(command), outcome, "No reading was decided for this outcome.");
                }
            }
        }

        var experience = questParameters.Load().ExperienceFor(
            characterLevel,
            RewardLevelOf(definition),
            DifficultyOf(definition),
            definition.Grade == QuestGrade.Epic,
            definition.PaysAtCharacterLevel);

        var (level, total) = this.Advance(characterLevel, characterExperience, experience);
        if (total != characterExperience
            && await characters.SetProgressAsync(
                command.CharacterId, level, total, cancellationToken).ConfigureAwait(false) != 1)
        {
            return QuestFinishOutcome.Refuse(command.QuestId);
        }

        return new QuestFinishOutcome(
            true,
            command.QuestId,
            ObjectiveKindOf(definition.ObjectiveTypeCode),
            experience,
            placed,
            level,
            total);
    }

    /// <summary>
    /// Where the character stands after the reward, level included.
    /// </summary>
    /// <remarks>
    /// The archive's <c>character/ExpTable.tbl</c> lists what a character must hold to be each
    /// level, so the new level is however many of those the new total has passed. Walked rather
    /// than computed in one step because a single reward can cross more than one threshold at low
    /// levels, where the first few are a few hundred apart and a quest can pay more than that.
    ///
    /// Experience saturates at the top of the table rather than wrapping. A character at the last
    /// threshold keeps earning and stays there, which is what the client draws.
    /// </remarks>
    private (byte Level, uint Total) Advance(byte level, uint current, uint gained)
    {
        if (gained == 0)
            return (level, current);

        var total = (ulong)current + gained;
        var thresholds = progression.Load().LevelThresholds;
        total = Math.Min(total, thresholds.Count > 0 ? thresholds[^1] : uint.MaxValue);

        var next = level;
        while (next < thresholds.Count + 1 && total >= thresholds[next - 1])
            next++;
        return (next, checked((uint)total));
    }

    /// <remarks>
    /// The bottom of the quest's own level band, which is what the reference server prices at. A
    /// quest that declares no band is priced at level one rather than at zero, where the table has
    /// no entry and the quest would pay nothing.
    /// </remarks>
    private static int RewardLevelOf(QuestDefinition definition) =>
        definition.RequiredLevel is { Minimum: > 0 } band ? band.Minimum : 1;

    /// <remarks>
    /// One letter, upper-cased by the table itself. A quest that declares none takes the default
    /// weight, which is what an unlisted letter takes too.
    /// </remarks>
    private static char DifficultyOf(QuestDefinition definition) =>
        string.IsNullOrWhiteSpace(definition.DifficultyCode)
            ? ' '
            : definition.DifficultyCode.Trim()[0];

    private static IReadOnlyList<(int ItemId, int Count)> ItemRewardsOf(QuestDefinition definition)
    {
        // `[reward int data]` is a flat list of item/count pairs under a reward of kind Item. A
        // trailing odd value is not a pair and is dropped rather than granted with a made-up count.
        var rewards = new List<(int, int)>();
        foreach (var reward in definition.Rewards)
        {
            if (reward.Kind is not QuestRewardKind.Item)
                continue;
            for (var index = 0; index + 1 < reward.Values.Count; index += 2)
            {
                var itemId = reward.Values[index];
                var count = reward.Values[index + 1];
                if (itemId > 0 && count > 0)
                    rewards.Add((itemId, count));
            }
        }

        return rewards;
    }

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
