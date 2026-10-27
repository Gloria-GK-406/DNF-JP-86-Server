using Dfo.Server.Abstractions.Creatures;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Inventory;

/// <summary>Why a move did not happen, in the server's own terms.</summary>
/// <remarks>
/// Named separately from the client's codes because several of these share one code and four have
/// none at all. Deciding the reason and choosing the sentence are two steps, and keeping them apart
/// is what lets the log say which refusal occurred when the player was shown nothing.
/// </remarks>
public enum ItemMoveStatus
{
    Moved = 0,

    /// <summary>The request named one slot twice; nothing moved and nothing is wrong.</summary>
    Unchanged,

    /// <summary>An outgoing mirror was removed without transferring an item.</summary>
    MirrorRemoved,

    /// <summary>No published session for the connection that asked.</summary>
    NoSession,

    /// <summary>The request named a list this server does not serve.</summary>
    ListNotServed,

    /// <summary>The character is not bound to one of the containers the request named.</summary>
    /// <remarks>
    /// It used to say "a main container", which was the whole of it while the bag was the only
    /// list a move could start from. A request naming the avatar list needs that list bound too,
    /// and either end being unbound lands here.
    /// </remarks>
    NoBoundContainer,

    /// <summary>Neither named slot holds anything.</summary>
    BothSlotsEmpty,

    /// <summary>The item may not occupy the slot it was aimed at.</summary>
    ItemCannotOccupySlot,

    /// <summary>Merging the two piles would exceed the item's stack limit.</summary>
    MergeExceedsStackLimit,

    /// <summary>No open empty cargo slot remains for an automatic deposit.</summary>
    ContainerFull,

    /// <summary>A swap was asked for with only part of the pile.</summary>
    PartialSwapNotAllowed,

    /// <summary>The item being displaced may not occupy the slot being vacated.</summary>
    DisplacedCannotOccupySlot,

    /// <summary>The archive declares nothing wearable under the identifier the slot holds.</summary>
    NotWearable,

    /// <summary>The request restates a move the client has already been told about.</summary>
    /// <remarks>
    /// A drag that has been acknowledged is sometimes sent a second time. Read literally it is
    /// the reverse of what the player asked for, so carrying it out undoes their own move in front
    /// of them. It is left alone instead, and nothing is drawn for it: the client is already
    /// showing the state it is describing.
    /// <para>
    /// <b>Both directions reach this.</b> A restated put-on would arrive with the cell it came
    /// from empty and the piece worn; a restated take-off with the piece back in the bag or on the
    /// avatar page and the worn slot empty. What they have in common is the only thing this judges:
    /// the request names the item on an end that does not hold it, and the store holds it at the
    /// other. An earlier revision described the first of the two as though it were the whole
    /// condition, because only that one was guarded.
    /// </para>
    /// <para>
    /// <b>Both shapes are derived, not seen.</b> They follow from what the client was observed to
    /// send for a fresh move; <b>no restatement of either has been observed at all</b>, and this
    /// judgement rests on a restatement carrying the original frame's bytes.
    /// </para>
    /// </remarks>
    MoveAlreadyMade,
}

/// <summary>Where the item ended up, or why it stayed.</summary>
public sealed record ItemMoveResult(ItemMoveStatus Status, int MoveValue, int ItemId)
{
    public BusinessSlot? ActualDestinationSlot { get; init; }
    public IReadOnlyList<CreatureRuntimeChange> CreatureChanges { get; init; } = [];
    /// <summary>A refusal decided before anything was read, so no template is named.</summary>
    public static ItemMoveResult Refused(ItemMoveStatus status) => new(status, 0, 0);

    /// <summary>
    /// A refusal decided with the piece already in hand, naming the template it was holding.
    /// </summary>
    /// <remarks>
    /// The move value stays zero, because nothing moved. The identifier is here for the log: the
    /// refusal branch of the protocol result mapping writes it out beside the two
    /// ends the request named, and without it that line says which reason was taken and not over
    /// which template. The two equip refusals in the inventory movement domain service hand it
    /// the piece they had already read.
    /// </remarks>
    public static ItemMoveResult Refused(ItemMoveStatus status, int itemId) =>
        new(status, 0, itemId);
}

/// <summary>One character moving one item between two slots.</summary>
/// <remarks>
/// The two instance values are whatever identity the client named on each end, and zero where it
/// named none. The client names the item on the end it is coming from, so a put-on fills the first
/// and a take-off fills the second.
/// <para>
/// They are here for one decision: telling a fresh drag apart from a drag the client is restating,
/// which read literally are the same two cells the other way round. See
/// <see cref="ItemMoveStatus.MoveAlreadyMade"/>. The destination one used to be decoded and then
/// dropped here, which is why only restated put-ons were caught.
/// </para>
/// </remarks>
public sealed record InventoryMoveRequest(
    CharacterId CharacterId,
    BusinessSlot SourceSlot,
    int MoveCount,
    BusinessSlot DestinationSlot,
    int SourceInstanceValue = 0,
    int DestinationInstanceValue = 0)
{
    public SelectedSessionIdentity? Session { get; init; }
}

/// <summary>Move one item between business inventory positions in the caller's transaction.</summary>
public interface IInventoryMoveDomainService
{
    ValueTask<ItemMoveResult> MoveAsync(InventoryMoveRequest request, CancellationToken cancellationToken);
}
