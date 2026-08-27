using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Transactions;
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
/// </remarks>
public enum QuestAcceptRefusal : byte
{
    /// <summary>The archive declares no such quest.</summary>
    Unknown = 0x17,

    /// <summary>The character is already carrying it.</summary>
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
    QuestAcceptRefusal? Refusal = null)
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
/// Puts a quest in the character's active list, with whatever it hands over on acceptance.
/// </summary>
/// <remarks>
/// The slot is the lowest free one of thirty, which is the reference server's rule and is visible
/// to the player: the client draws the active list in slot order, so a quest given the wrong slot
/// is a quest in the wrong place on screen.
///
/// The items go in before the row does. The answer has to name the slot each one landed in, and a
/// quest whose items were refused is not a quest that was accepted -- so a refusal there refuses
/// the whole acceptance rather than storing a quest whose premise is missing. Both writes settle
/// together because the command is transactional.
///
/// <b>Two of the reference server's refusals cannot be evaluated here, and this accepts where it
/// would refuse</b>: a quest already cleared and not repeatable, and a quest whose prerequisites
/// are unmet. Both need the set of quests this character has completed, and this server has no
/// such state -- there is no cleared-quest table, because nothing completes a quest yet. The gap
/// is mostly unreachable from a real client, which offers only what its own copy of that state
/// allows and refuses the rest before sending; it is not unreachable from a crafted frame. It
/// closes when quest completion is implemented, not before. See
/// protocol/game/client-to-server/0x001F.md.
/// </remarks>
public sealed class AcceptQuestCommandHandler(
    IQuestDefinitionSource quests,
    ICharacterStore characters,
    ICharacterContainerDomainService containers,
    IDomainTransactionAccessor transactions)
    : ICommandHandler<AcceptQuestCommand, QuestAcceptOutcome>
{
    /// <summary>How many quests a character can carry at once.</summary>
    private const int ActiveSlotCount = 30;

    public async ValueTask<QuestAcceptOutcome> HandleAsync(
        CommandExecutionContext context,
        AcceptQuestCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Required rather than opened here. The command is registered transactional, so the
        // absence of a transaction is a wiring mistake, and staging writes into a boundary nobody
        // would settle is exactly how a write becomes a no-op with nothing said about it.
        var transaction = transactions.Current
            ?? throw new InvalidOperationException(
                "Accepting a quest needs the use case's transaction; none is current.");
        var rows = characters.SessionIn(transaction);

        var definition = await quests.GetAsync(command.QuestId, cancellationToken)
            .ConfigureAwait(false);
        if (definition is null)
            return QuestAcceptOutcome.Refuse(command.QuestId, QuestAcceptRefusal.Unknown);

        var place = new OwnedCharacterId(command.AccountId, command.CharacterId);
        var active = await rows.Quests.ReadAllAsync(place, cancellationToken).ConfigureAwait(false);
        if (active.Any(quest => quest.QuestId == command.QuestId))
            return QuestAcceptOutcome.Refuse(command.QuestId, QuestAcceptRefusal.AlreadyActive);

        var slot = FirstFreeSlot(active);
        if (slot is null)
            return QuestAcceptOutcome.Refuse(command.QuestId, QuestAcceptRefusal.ActiveListFull);

        var declared = definition.EventItems ?? [];
        var placed = new List<AcceptedQuestItem>(declared.Count);
        if (declared.Count > 0)
        {
            var container = containers.FindContainer(
                command.CharacterId, InventoryContainerType.Main);
            if (container is null)
            {
                return QuestAcceptOutcome.Refuse(
                    command.QuestId, QuestAcceptRefusal.NoRoomForItems);
            }

            foreach (var item in declared)
            {
                var outcome = await container
                    .AddAsync(item.ItemId, item.Count, cancellationToken).ConfigureAwait(false);
                switch (outcome)
                {
                    case ItemPlacementOutcome.AddedToPile topped:
                        placed.Add(new AcceptedQuestItem(
                            checked((ushort)topped.SlotIndex),
                            (uint)item.ItemId,
                            (uint)topped.Added));
                        break;

                    case ItemPlacementOutcome.PutInEmptySlot opened:
                        placed.Add(new AcceptedQuestItem(
                            checked((ushort)opened.SlotIndex),
                            (uint)item.ItemId,
                            (uint)opened.Count));
                        break;

                    case ItemPlacementOutcome.NoRoom:
                    case ItemPlacementOutcome.UnknownItem:
                        return QuestAcceptOutcome.Refuse(
                            command.QuestId, QuestAcceptRefusal.NoRoomForItems);

                    default:
                        // Listed rather than defaulted: an outcome nobody read would quietly
                        // become an item the quest promised and never handed over.
                        throw new ArgumentOutOfRangeException(
                            nameof(command), outcome, "No reading was decided for this outcome.");
                }
            }
        }

        rows.Quests.Write(
            place,
            new StoredActiveQuest(
                slot.Value, command.QuestId, checked((int)definition.InitialTrigger)));

        return new QuestAcceptOutcome(true, command.QuestId, definition.InitialTrigger, placed);
    }

    private static short? FirstFreeSlot(IReadOnlyList<StoredActiveQuest> active)
    {
        var used = active.Select(quest => quest.Slot).ToHashSet();
        for (short slot = 0; slot < ActiveSlotCount; slot++)
        {
            if (!used.Contains(slot))
                return slot;
        }

        return null;
    }
}
