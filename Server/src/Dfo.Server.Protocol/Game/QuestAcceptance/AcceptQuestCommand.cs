using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Protocol.Game.QuestAcceptance;

/// <summary>Why an acceptance was refused, in the codes the client's answer carries.</summary>
/// <remarks>
/// The numbers are the reference server's own. It answers a refusal with two bytes -- a zero and
/// one of these -- and says nothing else about it, so the code is the whole of what the player is
/// told. Two of them share a value there as well: an unusable request and a full list both answer
/// 0x17, and nothing distinguishes them on the wire.
///
/// These are the wire's words for what the quest log answered in its own. The mapping is here
/// because a code the client draws is the protocol's business; which refusals exist at all is the
/// log's.
/// </remarks>
public enum QuestAcceptRefusal : byte
{
    /// <summary>The archive declares no such quest.</summary>
    Unknown = 0x17,

    /// <summary>The character is already carrying it, or has already finished it.</summary>
    /// <remarks>
    /// One code for both, which is the reference server's own doing: its acceptance service
    /// answers 18 for a quest in the active list and 18 again for one already cleared and not
    /// repeatable. Nothing on the wire tells the two apart.
    /// </remarks>
    AlreadyActive = 18,

    /// <summary>Its items would not fit.</summary>
    NoRoomForItems = 0x11,

    /// <summary>Every active slot is taken.</summary>
    ActiveListFull = 0x17,
}

/// <summary>One item the acceptance handed over, and the slot it landed in.</summary>
public sealed record AcceptedQuestItem(ushort SlotIndex, uint ItemId, uint Count);

/// <summary>What became of one attempt to accept a quest.</summary>
/// <param name="InitialTrigger">
/// The progress the client is told to start from. It is the archive's value rather than whatever
/// was stored, because the two can differ and the reference server sends the archive's here and
/// corrects it afterwards with a trigger notification of its own.
/// </param>
public sealed record QuestAcceptOutcome(
    bool Accepted,
    ushort QuestId,
    uint InitialTrigger,
    IReadOnlyList<AcceptedQuestItem> Items,
    QuestAcceptRefusal? Refusal = null,
    QuestProgressSnapshot? QuestProgress = null,
    IReadOnlyList<InventoryRuntimeValueState>? AccountMaterials = null)
{
    public static QuestAcceptOutcome Refuse(ushort questId, QuestAcceptRefusal refusal) =>
        new(false, questId, 0, [], refusal);
}

/// <summary>One character taking on one quest.</summary>
public sealed record AcceptQuestCommand(
    AccountId AccountId,
    CharacterId CharacterId,
    ushort QuestId) : ICommand<QuestAcceptOutcome>;

/// <summary>
/// Puts a quest in the character's log, with whatever it hands over on acceptance.
/// </summary>
/// <remarks>
/// Nothing here decides whether the quest may be taken on. Which quest it is comes from the
/// archive, whether this character may carry it comes from the log, and what is left is the order
/// the two are asked in and the answer the client is sent.
///
/// The items go in after the log has said yes and before anything settles. The answer has to name
/// the slot each one landed in, and a quest whose items were refused is not a quest that was
/// accepted -- so the quest is given back up rather than stored with its premise missing. Both
/// writes settle together because the command is transactional, and the log reads through, so it
/// sees the quest it has just staged and can take it back.
/// </remarks>
public sealed class AcceptQuestCommandHandler(
    IQuestDefinitionDomainService quests,
    ICharacterQuestLogDomainService logs,
    ICharacterContainerDomainService containers,
    IQuestProgressDomainService questProgress,
    IItemGrantDomainService itemGrants)
    : ICommandHandler<AcceptQuestCommand, QuestAcceptOutcome>
{
    public async ValueTask<QuestAcceptOutcome> HandleAsync(
        CommandExecutionContext context,
        AcceptQuestCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);

        // Asked of every request, not only of the ones that reach a write. A refusal returns
        // before the log stages anything, so a registration mistake would otherwise show up
        // only when somebody happened to accept a quest they could actually take on.
        if (context.ExecutionKind is not CommandExecutionKind.Transactional)
        {
            throw new InvalidOperationException(
                "Accepting a quest writes rows that outlive the process, so it has to be "
                + $"registered transactional; this execution is {context.ExecutionKind}.");
        }

        var definition = await quests.FindAsync(command.QuestId, cancellationToken)
            .ConfigureAwait(false);
        if (definition is null)
            return QuestAcceptOutcome.Refuse(command.QuestId, QuestAcceptRefusal.Unknown);

        var log = logs.LogOf(command.AccountId, command.CharacterId);
        var taken = await log.AcceptAsync(definition, cancellationToken).ConfigureAwait(false);
        if (!taken.Accepted)
            return QuestAcceptOutcome.Refuse(command.QuestId, WireCodeFor(taken.Refusal));

        var declared = definition.EventItems ?? [];
        var placed = new List<AcceptedQuestItem>(declared.Count);
        var accountChanges = new List<InventoryRuntimeValueState>();
        if (declared.Count > 0)
        {
            var container = containers.FindContainer(
                command.CharacterId, InventoryContainerType.Main);
            if (container is null)
            {
                return await this.HandBackAsync(log, command.QuestId, cancellationToken)
                    .ConfigureAwait(false);
            }

            foreach (var item in declared)
            {
                var granted = await itemGrants.GrantAsync(
                        command.AccountId, command.CharacterId, containers, item.ItemId, item.Count,
                        cancellationToken)
                    .ConfigureAwait(false);
                switch (granted)
                {
                    case BusinessItemAdditionResult { Placement: ItemPlacementOutcome.AddedToPile topped } character:
                        placed.Add(new AcceptedQuestItem(
                            checked((ushort)ItemGrantWireProjection.Index(character, topped.SlotIndex)),
                            (uint)item.ItemId,
                            (uint)topped.Added));
                        break;

                    case BusinessItemAdditionResult { Placement: ItemPlacementOutcome.PutInEmptySlot opened } character:
                        placed.Add(new AcceptedQuestItem(
                            checked((ushort)ItemGrantWireProjection.Index(character, opened.SlotIndex)),
                            (uint)item.ItemId,
                            (uint)opened.Count));
                        break;

                    case BusinessItemAdditionResult { Placement: ItemPlacementOutcome.NoRoom }:
                    case BusinessItemAdditionResult { Placement: ItemPlacementOutcome.UnknownItem }:
                        return await this.HandBackAsync(log, command.QuestId, cancellationToken)
                            .ConfigureAwait(false);

                    default:
                        // Listed rather than defaulted: an outcome nobody read would quietly
                        // become an item the quest promised and never handed over.
                        throw new ArgumentOutOfRangeException(
                            nameof(command), granted, "No reading was decided for this outcome.");
                }
                if (await ItemGrantWireProjection.SharedAsync(granted, cancellationToken).ConfigureAwait(false) is { } shared) accountChanges.Add(shared);
            }
        }

        var progress = placed.Count > 0
            ? (await questProgress.ReportAsync(
                command.AccountId, command.CharacterId,
                [new QuestHoldingsChanged()], cancellationToken).ConfigureAwait(false))
                .NotificationSnapshot
            : null;
        if (progress is null && taken.TriggerValue != definition.InitialTrigger)
        {
            progress = new QuestProgressSnapshot(await log.CarriedAsync(cancellationToken)
                .ConfigureAwait(false));
        }
        return new QuestAcceptOutcome(
            true, command.QuestId, definition.InitialTrigger, placed,
            QuestProgress: progress,
            AccountMaterials: accountChanges);
    }

    /// <remarks>
    /// The items this acceptance already handed over are not taken back with it, which is the
    /// behaviour this server has always had: a quest declaring several items, of which a later one
    /// would not fit, leaves the earlier ones with the player and tells them the quest was refused.
    /// No quest in the archive reaches that shape from a real client, and correcting it is a
    /// decision about what a partial grant means rather than a move of existing rules.
    /// </remarks>
    private async ValueTask<QuestAcceptOutcome> HandBackAsync(
        IQuestLog log, ushort questId, CancellationToken cancellationToken)
    {
        await log.GiveUpAsync(questId, cancellationToken).ConfigureAwait(false);
        return QuestAcceptOutcome.Refuse(questId, QuestAcceptRefusal.NoRoomForItems);
    }

    private static QuestAcceptRefusal WireCodeFor(QuestRefusal? refusal) => refusal switch
    {
        QuestRefusal.AlreadyCarried => QuestAcceptRefusal.AlreadyActive,
        QuestRefusal.AlreadyCleared => QuestAcceptRefusal.AlreadyActive,
        QuestRefusal.NoFreeSlot => QuestAcceptRefusal.ActiveListFull,
        _ => throw new ArgumentOutOfRangeException(
            nameof(refusal), refusal, "No code was decided for this refusal."),
    };
}
