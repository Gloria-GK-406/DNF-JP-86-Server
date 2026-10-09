using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Game;

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
    IReadOnlyList<InventoryRuntimeValueState>? AccountMaterials = null,
    byte? GrowType = null)
{
    public uint NotificationExperience { get; init; } = NewExperience;
    public IReadOnlyList<CommittedInventoryUpdateState> ConsumedItemUpdates { get; init; } = [];

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
    uint AcquiredAtUnixTime, SelectedSessionIdentity? Session = null) : ICommand<QuestFinishOutcome>, IRequestSettlementCommand;

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
/// The domain service decides the full effect plan before applying any write. Seeking progress
/// is projected from current holdings, so a refusal can correct the client without writing it.
///
/// Every ordinary refusal is decided before the log writes. A refusal discovered afterwards throws
/// <see cref="QuestFinishRejectedException"/> through the command bus, so the unit-of-work boundary
/// disposes without committing and rolls every enlisted store back. The package handler catches that
/// dedicated failure outside the boundary and maps it to the reference server's wire code 22.
/// </remarks>
public sealed class FinishQuestCommandHandler(Dfo.Server.Abstractions.Effects.IQuestCompletionDomainService completion,
    IBusinessInventorySlotDomainService inventorySlots)
    : ICommandHandler<FinishQuestCommand, QuestFinishOutcome>
{
    public async ValueTask<QuestFinishOutcome> HandleAsync(CommandExecutionContext context, FinishQuestCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (context.ExecutionKind != CommandExecutionKind.Transactional) throw new InvalidOperationException("Quest completion requires a transaction.");
        try
        {
            var result = await completion.FinishAsync(command.AccountId, command.CharacterId, command.QuestId,
                command.RewardSelection, command.CompletionCount, DateTimeOffset.FromUnixTimeSeconds(command.AcquiredAtUnixTime), cancellationToken, command.Session).ConfigureAwait(false);
            if (!result.Finished) return QuestFinishOutcome.Refuse(command.QuestId, result.Projection);
            var slots = new InventorySlotMappingRuleService();
            var items = new List<FinishedQuestItem>();
            var account = new List<InventoryRuntimeValueState>();
            var consumed = new HashSet<BusinessSlot>();
            foreach (var applied in result.Applied!.Applied)
                switch (applied.Change)
                {
                    case ContainerConsumptionChange change:
                        consumed.Add(change.Slot);
                        break;
                    case Dfo.Server.Abstractions.Effects.BasicCharacterItemChange basic:
                        items.AddRange(basic.Additions.Select(x => new FinishedQuestItem(checked((ushort)slots.ToGameSlot(x.Slot).Index),
                            (uint)((Dfo.Server.Abstractions.Effects.GrantBasicCharacterItemEffect)applied.Effect).ItemId, checked((uint)x.Count))));
                        foreach (var addition in basic.Additions.Where(x => x.Slot.Space == BusinessInventorySpace.SharedMaterial))
                        {
                            var bound = inventorySlots.Find(command.CharacterId, addition.Slot)!;
                            var held = await bound.Container.GetAsync(bound.Slot.Index, cancellationToken).ConfigureAwait(false)
                                ?? throw new InvalidDataException("Shared reward disappeared before commit.");
                            account.Add(new(slots.ToGameSlot(addition.Slot).Index, held.ItemId, held.Count));
                        }
                        break;
                    case Dfo.Server.Abstractions.Effects.QuestRewardGoldChange gold when gold.After > gold.Before:
                        items.Add(new(0, 0, checked((uint)(gold.After - gold.Before))));
                        break;
                }
            var updates = new List<CommittedInventoryUpdateState>();
            foreach (var slot in consumed)
            {
                var bound = inventorySlots.Find(command.CharacterId, slot) ?? throw new InvalidDataException("Consumed quest slot vanished.");
                var item = await bound.Container.GetAsync(bound.Slot.Index, cancellationToken).ConfigureAwait(false);
                var wire = slots.ToGameSlot(slot);
                updates.Add(new(wire.Space, [new(item?.ItemUid, wire.Index,
                    item?.Snapshot.ToCore() ?? StackableItemCore.Create(0, -1, 0))], DateTimeOffset.FromUnixTimeSeconds(command.AcquiredAtUnixTime)));
            }
            return new(true, command.QuestId, ObjectiveKindOf(result.Objective), result.Award!.Gained, items,
                result.Award.Level, result.Award.Total, QuestProgress: result.Projection, AccountMaterials: account, GrowType: result.GrowType)
            { NotificationExperience = result.Award.NotificationTotal, ConsumedItemUpdates = updates };
        }
        catch (Exception exception) when ((exception is not (QuestFinishRejectedException or OperationCanceledException)) && !Dfo.Server.Abstractions.Transactions.TransactionOutcomeUncertainException.Contains(exception))
        {
            throw new QuestFinishRejectedException(command.QuestId, "Quest completion settlement was rejected.", exception);
        }
    }
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
