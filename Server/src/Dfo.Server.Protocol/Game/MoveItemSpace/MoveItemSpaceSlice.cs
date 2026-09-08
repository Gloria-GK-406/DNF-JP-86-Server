using System.Buffers.Binary;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.MoveItemSpace;

/// <summary>
/// CMD 0x0013 <c>MOVE_ITEMSPACE</c>: an item changing slots.
/// </summary>
/// <remarks>
/// The one request behind every inventory drag, and behind the quick-slot bar as well -- the bar is
/// not its own container but slots 3 to 8 of the main list, so binding an item to a number key is
/// an ordinary move within one list.
///
/// Observed live on 2026-08-18. The client sent it unprompted, one frame after the tutorial reward
/// acknowledgement said the potion had landed in slot 65, asking for slot 3. Nothing answered, and
/// that frame is the last in the capture: the client holds the inventory closed until the
/// acknowledgement arrives, which is why every later drag looked like it did nothing at all.
/// </remarks>
public sealed record MoveItemSpaceRequest(
    GameMessageMetadata Metadata,
    byte SourceListType,
    short SourceSlotIndex,
    int SourceInstanceValue,
    int MoveCount,
    byte DestinationListType,
    short DestinationSlotIndex,
    int DestinationInstanceValue) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameMoveItemSpace;
}

/// <summary>
/// The CMD acknowledgement for the same type: either where the item ended up, or why it did not
/// move.
/// </summary>
public sealed record MoveItemSpaceResponse(
    byte? ErrorCode,
    byte SourceListType,
    short SourceSlotIndex,
    int MoveValue,
    byte DestinationListType,
    short DestinationSlotIndex) : IGameCmdMessage
{
    /// <summary>
    /// The client's own word for each refusal, read out of its string table rather than guessed.
    /// </summary>
    /// <remarks>
    /// The table is archived at
    /// <c>protocol/patterns/data/probes/cmd-reason-messages-20260818.md</c>. Of the fifteen codes
    /// this type draws, only three say anything true about moving an item; the rest belong to
    /// equipping, trading, or a restricted area, and borrowing one is handing the player a
    /// sentence about something that did not happen.
    /// <para>
    /// Code <c>4</c> is deliberately absent from this list. It says 「金库空间不足， 无法放入物品」
    /// and is reserved for a target container that genuinely has no free slot -- a condition this
    /// server cannot yet reach, since a move names both of its slots. It used to carry the missing
    /// session, the missing binding, two empty slots and a lost race, each of which the player read
    /// as a space problem.
    /// </para>
    /// <para>
    /// Code <c>19</c> prints the same sentence as <c>23</c> and adds a log line about permissions,
    /// so <c>23</c> is the cleaner of the two. Code <c>18</c> writes to the log and draws nothing.
    /// Code <c>1</c> draws a placeholder in Chinese and an empty string in Japanese.
    /// </para>
    /// </remarks>
    public const byte NotMovable = 23;

    /// <summary>「物品栏已有同类物品」 -- the merge would burst the stack limit.</summary>
    public const byte StackLimitReached = 17;

    /// <summary>
    /// A code the client shows nothing for.
    /// </summary>
    /// <remarks>
    /// It appears in neither this type's branch chain nor the shared fallback table every handler
    /// consults, so the frame arrives and no text is drawn. Silence looks like a drag that did
    /// nothing, which is what a lost race is; the alternative is a sentence blaming the player for
    /// the server's own gap.
    /// </remarks>
    public const byte Silent = 2;

    public ServerMessageType MessageType => ServerMessageType.GameMoveItemSpace;

    public static MoveItemSpaceResponse Success(
        MoveItemSpaceRequest request, int moveValue, short destinationSlotIndex) =>
        new(null, request.SourceListType, request.SourceSlotIndex, moveValue,
            request.DestinationListType, destinationSlotIndex);

    public static MoveItemSpaceResponse Failure(MoveItemSpaceRequest request, byte errorCode) =>
        new(errorCode, request.SourceListType, request.SourceSlotIndex, 0,
            request.DestinationListType, request.DestinationSlotIndex);
}

internal static class MoveItemSpacePayloadCodec
{
    /// <summary>
    /// The shortest body the reference server will parse. Live frames are 24 bytes; the two fields
    /// past offset 14 are read only when the body is long enough to carry them, so a shorter one is
    /// still a valid request rather than a truncated frame.
    /// </summary>
    public const int MinimumBodyLength = 14;

    public static byte[] Encode(MoveItemSpaceResponse message)
    {
        if (message.ErrorCode is { } errorCode)
            return [0x00, errorCode, message.SourceListType, message.DestinationListType];

        var body = new byte[11];
        body[0] = 0x01;
        body[1] = message.SourceListType;
        BinaryPrimitives.WriteInt16LittleEndian(body.AsSpan(2, 2), message.SourceSlotIndex);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(4, 4), message.MoveValue);
        body[8] = message.DestinationListType;
        BinaryPrimitives.WriteInt16LittleEndian(body.AsSpan(9, 2), message.DestinationSlotIndex);
        return body;
    }
}

internal sealed class MoveItemSpaceDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage? Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        if (body.Length < MoveItemSpacePayloadCodec.MinimumBodyLength)
        {
            throw new InvalidDataException(
                $"MOVE_ITEMSPACE requires at least {MoveItemSpacePayloadCodec.MinimumBodyLength} "
                    + $"body bytes; received '{body.Length}'.");
        }

        return new MoveItemSpaceRequest(
            metadata,
            body[0],
            BinaryPrimitives.ReadInt16LittleEndian(body[1..]),
            BinaryPrimitives.ReadInt32LittleEndian(body[3..]),
            BinaryPrimitives.ReadInt32LittleEndian(body[7..]),
            body[11],
            BinaryPrimitives.ReadInt16LittleEndian(body[12..]),
            body.Length >= 18 ? BinaryPrimitives.ReadInt32LittleEndian(body[14..]) : 0);
    }
}

internal sealed class MoveItemSpaceResponseEncoder : GameCmdMessageEncoder<MoveItemSpaceResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(MoveItemSpaceResponse message) =>
        OnePayload(GameProtocol.MoveItemSpaceType, MoveItemSpacePayloadCodec.Encode(message));
}

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

    /// <summary>A swap was asked for with only part of the pile.</summary>
    PartialSwapNotAllowed,

    /// <summary>The item being displaced may not occupy the slot being vacated.</summary>
    DisplacedCannotOccupySlot,

    /// <summary>The archive declares nothing wearable under the identifier the slot holds.</summary>
    NotWearable,

    /// <summary>The request restates a move the client has already been told about.</summary>
    /// <remarks>
    /// A drag that has been acknowledged is sometimes sent a second time, with the slot it came
    /// from now empty and the piece already worn. Read literally that is "the worn piece belongs
    /// back in this empty cell", which is the reverse of what the player asked for -- so a move
    /// naming the worn piece's own identifier on its empty end is left alone rather than carried
    /// out. Nothing is drawn for it: the client is already showing the state it is describing.
    /// </remarks>
    MoveAlreadyMade,
}

/// <summary>Where the item ended up, or why it stayed.</summary>
public sealed record ItemMoveResult(ItemMoveStatus Status, int MoveValue, int ItemId)
{
    /// <summary>A refusal decided before anything was read, so no template is named.</summary>
    public static ItemMoveResult Refused(ItemMoveStatus status) => new(status, 0, 0);

    /// <summary>
    /// A refusal decided with the piece already in hand, naming the template it was holding.
    /// </summary>
    /// <remarks>
    /// The move value stays zero, because nothing moved. The identifier is here for the log: the
    /// refusal branch of <c>MoveItemSpaceHandler.ResolveAsync</c> writes it out beside the two
    /// ends the request named, and without it that line says which reason was taken and not over
    /// which template. The two equip refusals in <see cref="MoveItemSpaceCommandHandler"/> hand it
    /// the piece they had already read.
    /// </remarks>
    public static ItemMoveResult Refused(ItemMoveStatus status, int itemId) =>
        new(status, 0, itemId);
}

/// <summary>One character moving one item between two slots.</summary>
/// <remarks>
/// <c>SourceInstanceValue</c> is whatever identity the client named on the source end, and zero
/// when it named none -- which is what a take-off carries, the identity riding on the other end
/// instead. It is here for one decision: telling a fresh drag apart from a drag the client is
/// restating, which read literally are the same two cells the other way round. See
/// <see cref="ItemMoveStatus.MoveAlreadyMade"/>.
/// </remarks>
public sealed record MoveItemSpaceCommand(
    CharacterId CharacterId,
    byte SourceListType,
    short SourceSlotIndex,
    int MoveCount,
    byte DestinationListType,
    short DestinationSlotIndex,
    int SourceInstanceValue = 0) : ICommand<ItemMoveResult>;

/// <summary>
/// Hands the move to the character's bag and reports what it answered.
/// </summary>
/// <remarks>
/// Two lists hold things a character carries -- the bag and the avatar list -- and moves are served
/// within either, between them, and out of either onto the character and back off again.
/// The other containers this request can name -- cargo, pet, the knight shield catalogue -- each
/// carry their own rules about what may enter them, and answering for them without those rules
/// would let an item through a door that should have been shut. The avatar list was on that list
/// of unanswered doors until its rule was written down (`IItemSlotRuleService.CanOccupySlot`);
/// being unanswered is what made a drag inside it come back `ListNotServed`, before any item was
/// read, which is why that refusal could not even say which item it was about.
///
/// Which slot may hold what, and whether a merge would burst a stack, are the bag's own questions.
/// A move between two lists is neither container's: it needs the archive to say which slot a piece
/// is worn in, and the bag's own rule to say whether what comes off may land where it was aimed.
/// That is why both arrive here rather than inside either container.
///
/// The two ends settle together. The command is transactional, so a piece is never worn without
/// leaving the bag and never leaves the bag without being worn.
/// </remarks>
public sealed class MoveItemSpaceCommandHandler(
    ICharacterContainerDomainService containers,
    IItemMovementDomainService moves)
    : ICommandHandler<MoveItemSpaceCommand, ItemMoveResult>
{
    private const byte MainListType = (byte)InventoryContainerType.Main;

    private const byte AvatarListType = (byte)InventoryContainerType.Avatar;

    private const byte EquipmentListType = (byte)InventoryContainerType.Equipment;

    /// <summary>A piece of equipment does not stack, so exactly one of it ever moves.</summary>
    private const int OnePiece = 1;

    /// <summary>The lists a character carries things in, as opposed to wears them from.</summary>
    /// <remarks>
    /// Both are ordinary containers with a slot rule of their own, so a move within either, or
    /// between them, is the same operation with different ends. Naming them once is what keeps the
    /// four served combinations from being four branches that could drift apart.
    /// </remarks>
    private static InventoryContainerType? CarriedListFor(byte listType) => listType switch
    {
        MainListType => InventoryContainerType.Main,
        AvatarListType => InventoryContainerType.Avatar,
        _ => null,
    };

    /// <summary>The same move, said from the end that turned out to be holding the item.</summary>
    /// <remarks>
    /// A request does not say which of its two ends the item is on; the client puts it on the end
    /// the drag started from, and that is either end depending on which way the player dragged.
    /// Reversing a served pair always gives another served pair -- carrying across becomes carrying
    /// across the other way, putting on becomes taking off, and taking off becomes putting on --
    /// so the path that answers the reversed request is one of the three already here, and none of
    /// the rules about what may sit where has to be restated to reach it.
    /// <para>
    /// The identity does not travel. It belonged to the end the client named as the source, and a
    /// reversed request names none of its own; the one decision that reads it
    /// (<see cref="ItemMoveStatus.MoveAlreadyMade"/>) has already been made by the time this is
    /// called.
    /// </para>
    /// <para>
    /// Reversal happens at most once per request. Each path reverses only on finding its own end
    /// empty, and hands over only after finding the other end occupied -- so the path it hands to
    /// reads a slot that is known to hold something and goes forward from there.
    /// </para>
    /// </remarks>
    private static MoveItemSpaceCommand Reversed(MoveItemSpaceCommand command) => command with
    {
        SourceListType = command.DestinationListType,
        SourceSlotIndex = command.DestinationSlotIndex,
        DestinationListType = command.SourceListType,
        DestinationSlotIndex = command.SourceSlotIndex,
        SourceInstanceValue = 0,
    };

    public async ValueTask<ItemMoveResult> HandleAsync(
        CommandExecutionContext context,
        MoveItemSpaceCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var source = CarriedListFor(command.SourceListType);
        var destination = CarriedListFor(command.DestinationListType);

        if (source is { } putOnFrom && command.DestinationListType == EquipmentListType)
        {
            var from = containers.FindContainer(command.CharacterId, putOnFrom);
            return from is null
                ? ItemMoveResult.Refused(ItemMoveStatus.NoBoundContainer)
                : await this.PutOnAsync(command, from, cancellationToken).ConfigureAwait(false);
        }

        if (command.SourceListType == EquipmentListType && destination is { } takeOffTo)
        {
            var into = containers.FindContainer(command.CharacterId, takeOffTo);
            return into is null
                ? ItemMoveResult.Refused(ItemMoveStatus.NoBoundContainer)
                : await this.TakeOffAsync(command, into, cancellationToken).ConfigureAwait(false);
        }

        if (source is null || destination is null)
            return ItemMoveResult.Refused(ItemMoveStatus.ListNotServed);

        var bag = containers.FindContainer(command.CharacterId, source.Value);
        if (bag is null)
            return ItemMoveResult.Refused(ItemMoveStatus.NoBoundContainer);

        if (source != destination)
        {
            var into = containers.FindContainer(command.CharacterId, destination.Value);
            return into is null
                ? ItemMoveResult.Refused(ItemMoveStatus.NoBoundContainer)
                : await this.CarryAcrossAsync(
                    command, bag, into, destination.Value, cancellationToken).ConfigureAwait(false);
        }

        // The move is carried out inside the bag as the clears and writes it is made of. Everything
        // they touch settles together, because the command is transactional and the boundary around
        // it is what makes four writes one change.
        var outcome = await bag.MoveToAsync(
            command.SourceSlotIndex,
            command.DestinationSlotIndex,
            command.MoveCount,
            cancellationToken).ConfigureAwait(false);

        return outcome switch
        {
            ItemMoveOutcome.Moved moved =>
                new ItemMoveResult(ItemMoveStatus.Moved, moved.MoveCount, moved.ItemId),
            ItemMoveOutcome.Unchanged => new ItemMoveResult(ItemMoveStatus.Unchanged, 0, 0),
            ItemMoveOutcome.Refused refused => ItemMoveResult.Refused(StatusFor(refused.Reason)),
            _ => throw new ArgumentOutOfRangeException(
                nameof(command), outcome, "No reading was decided for this outcome."),
        };
    }

    /// <summary>
    /// Wearing a piece: out of the bag and into the slot the archive says it belongs in.
    /// </summary>
    /// <remarks>
    /// Observed live on 2026-08-26 and answered wrong: the player dragged the shoulder they had
    /// just picked up onto the shoulder slot, and this server refused it because neither list was
    /// the main one. The reference server has the identical request and answers it with the
    /// eleven-byte success this slice already builds --
    /// <c>00 09 00 40 38 00 00 01 00 00 00 03 0F 00</c> answered
    /// <c>01 00 09 00 01 00 00 00 03 0F 00</c> -- and nothing follows it.
    /// <para>
    /// The slot is checked rather than taken from the request. A stock client works it out for
    /// itself and will not offer a drop it believes is illegal, so a request naming the wrong slot
    /// cannot be produced by playing; which is precisely why the server may not take the request's
    /// word for it.
    /// </para>
    /// <para>
    /// Whatever was already worn goes back into the slot the new piece came out of, and nothing
    /// here judges whether it may sit there. <b>The note left in this place on 2026-09-07 asked
    /// whoever gave the costume slots names to come back and decide that; this is the decision.</b>
    /// It stays as it is, and the reason is no longer the empty one it was then ("nothing can be
    /// worn in a costume slot, so nothing can be displaced out of one").
    /// <br/>
    /// The reason now: <b>a swap has the same sort of thing at both ends, and the slot the
    /// displaced piece goes back to is a slot that sort of thing was just sitting in.</b> A piece
    /// may only go on the slot its own archive entry names, so what was already worn there names
    /// that slot too. For the costume slots that means both ends are costumes -- only the eleven
    /// <c>avatar]</c> names reach <c>0..10</c>, which was checked against the whole equipment index.
    /// And the slot it returns to is where the arriving costume was standing a moment ago, in that
    /// same list, so that list admits a costume there: the avatar list takes costumes anywhere in
    /// its range, and a costume in the bag is in the equipment band, because the quick slots turn
    /// avatars away and no move can put one there.
    /// <br/>
    /// <b>This covers the states a move can reach, and says nothing about the others.</b> A store
    /// can hold a costume in a bag slot no move would have put it in -- rows written around the
    /// server, say -- and a swap out of such a slot would put the displaced costume back into it.
    /// That is the store disagreeing with the rules, not this swap creating the disagreement.
    /// <br/>
    /// <b>Do not generalise it to "the worn slot decides what is in it".</b> That is false:
    /// <c>ByName</c> maps <c>[earring]</c>, <c>[support weapon]</c> and <c>[creature]</c> all to
    /// slot 24, so what comes off 24 may be a creature where what went on was an earring -- and the
    /// bag's own band rule does not admit a creature into a quick slot. That swap succeeds today
    /// because nothing here judges it, and it is written up as a defect of its own
    /// (<c>Docs/CreatureDomainDesign.md</c>, and its own issue). A judgement was briefly added here
    /// once and taken out again: it reached that case too, and refusing it is a change to a path
    /// this work was not about.
    /// </para>
    /// </remarks>
    private async ValueTask<ItemMoveResult> PutOnAsync(
        MoveItemSpaceCommand command, IItemContainer bag, CancellationToken cancellationToken)
    {
        var worn = containers.FindContainer(command.CharacterId, InventoryContainerType.Equipment);
        if (worn is null)
            return ItemMoveResult.Refused(ItemMoveStatus.NoBoundContainer);

        var piece = await bag.ReadSlotAsync(command.SourceSlotIndex, cancellationToken)
            .ConfigureAwait(false);
        if (piece is null)
        {
            var alreadyWorn = await worn
                .ReadSlotAsync(command.DestinationSlotIndex, cancellationToken)
                .ConfigureAwait(false);
            if (alreadyWorn is null)
                return ItemMoveResult.Refused(ItemMoveStatus.BothSlotsEmpty);

            if (command.SourceInstanceValue > 0
                && alreadyWorn.ItemId == command.SourceInstanceValue)
            {
                return ItemMoveResult.Refused(
                    ItemMoveStatus.MoveAlreadyMade, alreadyWorn.ItemId);
            }

            return await this.TakeOffAsync(Reversed(command), bag, cancellationToken)
                .ConfigureAwait(false);
        }

        var slot = await moves.WornSlotForAsync(piece.ItemId, cancellationToken)
            .ConfigureAwait(false);
        if (slot is null)
            return ItemMoveResult.Refused(ItemMoveStatus.NotWearable, piece.ItemId);
        if (slot.Value != command.DestinationSlotIndex)
            return ItemMoveResult.Refused(ItemMoveStatus.ItemCannotOccupySlot, piece.ItemId);

        var displaced = await worn.ReadSlotAsync(command.DestinationSlotIndex, cancellationToken)
            .ConfigureAwait(false);
        await worn.PutAsync(command.DestinationSlotIndex, piece.Core, cancellationToken)
            .ConfigureAwait(false);
        if (displaced is null)
        {
            await bag.ClearAsync(command.SourceSlotIndex, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await bag.PutAsync(command.SourceSlotIndex, displaced.Core, cancellationToken)
                .ConfigureAwait(false);
        }

        return new ItemMoveResult(ItemMoveStatus.Moved, OnePiece, piece.ItemId);
    }

    /// <summary>Carrying a thing from one of the character's lists into the other.</summary>
    /// <remarks>
    /// Neither container's own business, for the same reason wearing is not: each end judges what
    /// it lets in, and the two judgements have to be made together before either is written. The
    /// destination's rule is asked of the movement service by name, so the avatar list refusing a
    /// thing that is not a costume and the bag refusing a costume aimed outside its band are one
    /// rule asked twice rather than two rules that could disagree.
    /// <para>
    /// A slot that already holds something is a swap, and a swap has to satisfy both ends: what
    /// comes back has to be admissible where the moving thing came from. Refusing rather than
    /// displacing keeps the client's picture and the store's rows saying the same thing.
    /// </para>
    /// <para>
    /// The count is not honoured. Both lists hold individual pieces -- the avatar list by its rule,
    /// and a costume in the bag by its stack limit of one -- so a partial move of one of them is
    /// not a thing that exists. A request naming a smaller count moves the piece and reports the
    /// one it moved, which is what the equip paths beside this one already do.
    /// </para>
    /// </remarks>
    private async ValueTask<ItemMoveResult> CarryAcrossAsync(
        MoveItemSpaceCommand command,
        IItemContainer from,
        IItemContainer into,
        InventoryContainerType destination,
        CancellationToken cancellationToken)
    {
        var moving = await from.ReadSlotAsync(command.SourceSlotIndex, cancellationToken)
            .ConfigureAwait(false);
        if (moving is null)
        {
            var other = await into.ReadSlotAsync(command.DestinationSlotIndex, cancellationToken)
                .ConfigureAwait(false);
            return other is null
                ? ItemMoveResult.Refused(ItemMoveStatus.BothSlotsEmpty)
                : await this.CarryAcrossAsync(
                    Reversed(command), into, from, from.Type, cancellationToken)
                    .ConfigureAwait(false);
        }

        switch (await moves.MayOccupySlotAsync(
            destination, moving.ItemId, command.DestinationSlotIndex, cancellationToken)
            .ConfigureAwait(false))
        {
            case SlotAdmission.ItemUnknown:
                return ItemMoveResult.Refused(ItemMoveStatus.NotWearable, moving.ItemId);

            case SlotAdmission.SlotNotAllowed:
                return ItemMoveResult.Refused(ItemMoveStatus.ItemCannotOccupySlot, moving.ItemId);

            default:
                break;
        }

        var displaced = await into.ReadSlotAsync(command.DestinationSlotIndex, cancellationToken)
            .ConfigureAwait(false);
        if (displaced is not null)
        {
            var back = await moves.MayOccupySlotAsync(
                from.Type, displaced.ItemId, command.SourceSlotIndex, cancellationToken)
                .ConfigureAwait(false);
            if (back != SlotAdmission.Allowed)
                return ItemMoveResult.Refused(
                    ItemMoveStatus.DisplacedCannotOccupySlot, moving.ItemId);
        }

        await into.PutAsync(command.DestinationSlotIndex, moving.Core, cancellationToken)
            .ConfigureAwait(false);
        if (displaced is null)
        {
            await from.ClearAsync(command.SourceSlotIndex, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await from.PutAsync(command.SourceSlotIndex, displaced.Core, cancellationToken)
                .ConfigureAwait(false);
        }

        return new ItemMoveResult(ItemMoveStatus.Moved, OnePiece, moving.ItemId);
    }

    /// <summary>Taking a piece off: out of its slot and into the bag slot the player aimed at.</summary>
    /// <remarks>
    /// The mirror of putting one on, and the only one of the two with no captured session behind
    /// it: the reference character wore what it picked up and never took anything off. The request
    /// is the same type with its two lists the other way round, and refusing it would leave a
    /// player able to put gear on and never able to take it back off.
    /// <para>
    /// The bag slot is judged by the same rule an arriving item is judged by, so a piece cannot be
    /// taken off into the consumable band. A bag slot that already holds something is a swap, and
    /// what it holds has to be wearable in the slot being vacated for that to be a swap rather than
    /// an item quietly displaced into nowhere.
    /// </para>
    /// </remarks>
    private async ValueTask<ItemMoveResult> TakeOffAsync(
        MoveItemSpaceCommand command, IItemContainer bag, CancellationToken cancellationToken)
    {
        var worn = containers.FindContainer(command.CharacterId, InventoryContainerType.Equipment);
        if (worn is null)
            return ItemMoveResult.Refused(ItemMoveStatus.NoBoundContainer);

        var piece = await worn.ReadSlotAsync(command.SourceSlotIndex, cancellationToken)
            .ConfigureAwait(false);
        if (piece is null)
        {
            var carried = await bag.ReadSlotAsync(command.DestinationSlotIndex, cancellationToken)
                .ConfigureAwait(false);
            return carried is null
                ? ItemMoveResult.Refused(ItemMoveStatus.BothSlotsEmpty)
                : await this.PutOnAsync(Reversed(command), bag, cancellationToken)
                    .ConfigureAwait(false);
        }

        switch (await moves.MayOccupySlotAsync(
            bag.Type, piece.ItemId, command.DestinationSlotIndex, cancellationToken)
            .ConfigureAwait(false))
        {
            case SlotAdmission.ItemUnknown:
                return ItemMoveResult.Refused(ItemMoveStatus.NotWearable, piece.ItemId);

            case SlotAdmission.SlotNotAllowed:
                return ItemMoveResult.Refused(ItemMoveStatus.ItemCannotOccupySlot, piece.ItemId);

            default:
                break;
        }

        var displaced = await bag.ReadSlotAsync(command.DestinationSlotIndex, cancellationToken)
            .ConfigureAwait(false);
        if (displaced is not null)
        {
            var displacedSlot = await moves
                .WornSlotForAsync(displaced.ItemId, cancellationToken)
                .ConfigureAwait(false);
            if (displacedSlot != command.SourceSlotIndex)
                return ItemMoveResult.Refused(
                    ItemMoveStatus.DisplacedCannotOccupySlot, piece.ItemId);
        }

        await bag.PutAsync(command.DestinationSlotIndex, piece.Core, cancellationToken)
            .ConfigureAwait(false);
        if (displaced is null)
        {
            await worn.ClearAsync(command.SourceSlotIndex, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await worn.PutAsync(command.SourceSlotIndex, displaced.Core, cancellationToken)
                .ConfigureAwait(false);
        }

        return new ItemMoveResult(ItemMoveStatus.Moved, OnePiece, piece.ItemId);
    }

    /// <summary>
    /// The bag's refusal, in this protocol's own terms.
    /// </summary>
    /// <remarks>
    /// Listed rather than defaulted, for the same reason `ErrorCodeFor` is: a refusal nobody mapped
    /// would fall through to a code that belongs to something the player did not do.
    /// </remarks>
    internal static ItemMoveStatus StatusFor(ItemMoveRefusal refusal) => refusal switch
    {
        ItemMoveRefusal.BothSlotsEmpty => ItemMoveStatus.BothSlotsEmpty,
        ItemMoveRefusal.ItemCannotOccupySlot => ItemMoveStatus.ItemCannotOccupySlot,
        ItemMoveRefusal.MergeExceedsStackLimit => ItemMoveStatus.MergeExceedsStackLimit,
        ItemMoveRefusal.PartialSwapNotAllowed => ItemMoveStatus.PartialSwapNotAllowed,
        ItemMoveRefusal.DisplacedCannotOccupySlot => ItemMoveStatus.DisplacedCannotOccupySlot,
        _ => throw new ArgumentOutOfRangeException(
            nameof(refusal), refusal, "No status was decided for this refusal."),
    };
}

/// <summary>
/// Turns the request into a command and the command's answer into something the client can read.
/// </summary>
/// <remarks>
/// Both readings of the request are answered with the request's own source and destination echoed
/// back, because the client matches the acknowledgement against what it sent.
/// </remarks>
public sealed class MoveItemSpaceHandler(
    ISelectedSessionDomainService selectedSessions,
    ICommandBus commandBus,
    IGameMessageSender sender,
    ILogger<MoveItemSpaceHandler> logger) : ClientMessageHandler<MoveItemSpaceRequest>
{
    protected override async ValueTask HandleMessageAsync(
        NetworkDispatchContext context,
        MoveItemSpaceRequest message,
        CancellationToken cancellationToken)
    {
        var response = await ResolveAsync(context, message, cancellationToken).ConfigureAwait(false);
        await sender.SendExplicitAsync(
            context.Connection.ConnectionId,
            new GameExplicitMessageDelivery([response], []),
            cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<MoveItemSpaceResponse> ResolveAsync(
        NetworkDispatchContext context,
        MoveItemSpaceRequest message,
        CancellationToken cancellationToken)
    {
        var session = selectedSessions.FindByConnection(context.Connection.ConnectionId);
        if (session is null)
        {
            logger.LogWarning(
                "Move item space {ConnectionId}: no published session",
                context.Connection.ConnectionId);
            return MoveItemSpaceResponse.Failure(message, MoveItemSpaceResponse.Silent);
        }

        var result = await commandBus.SendAsync(
            new MoveItemSpaceCommand(
                session.CharacterId,
                message.SourceListType,
                message.SourceSlotIndex,
                message.MoveCount,
                message.DestinationListType,
                message.DestinationSlotIndex,
                message.SourceInstanceValue),
            cancellationToken).ConfigureAwait(false);

        if (result.Status is ItemMoveStatus.Moved or ItemMoveStatus.Unchanged)
        {
            logger.LogInformation(
                "Move item space {ConnectionId} character={CharacterId}: item {ItemId} "
                    + "from list {FromList} slot {From} to list {ToList} slot {To} count={Count}",
                context.Connection.ConnectionId,
                session.CharacterId.Value,
                result.ItemId,
                message.SourceListType,
                message.SourceSlotIndex,
                message.DestinationListType,
                message.DestinationSlotIndex,
                result.MoveValue);
            return MoveItemSpaceResponse.Success(
                message, result.MoveValue, message.DestinationSlotIndex);
        }

        // `{ItemId}` goes in beside the two ends the request already named. That pair is what a
        // slot map was read from, and the costume map was read from it: while
        // `PvfEquipmentSlots.ByName` carried no name ending in `avatar]`, a costume reaching
        // `PutOnAsync` got nothing back from the archive and was refused as `NotWearable`, and this
        // line carried its template beside the slot the client had asked for. Eleven costumes
        // dragged one at a time gave eleven slots that way, and those are the numbers now in the
        // table -- so a costume aimed at the wrong slot is the ordinary `ItemCannotOccupySlot`
        // refusal today, and `NotWearable` is left for a template the archive names no slot for at
        // all. The cases are in `RefusedEquipIsRecordedWithItsTemplateTests`. The server has no
        // opinion about which slot an avatar belongs in and the client does, so `{To}` is the
        // client's answer and `{ItemId}` says what it was answering about. The one-argument `ItemMoveResult.Refused`
        // puts a zero here, for a refusal decided before any slot was read.
        logger.LogInformation(
            "Move item space {ConnectionId} character={CharacterId}: item {ItemId} "
                + "from list {FromList} slot {From} to list {ToList} slot {To} refused as {Status}",
            context.Connection.ConnectionId,
            session.CharacterId.Value,
            result.ItemId,
            message.SourceListType,
            message.SourceSlotIndex,
            message.DestinationListType,
            message.DestinationSlotIndex,
            result.Status);
        return MoveItemSpaceResponse.Failure(message, ErrorCodeFor(result.Status));
    }

    /// <summary>
    /// The client sentence that is true of this refusal, or silence when it owns none.
    /// </summary>
    /// <remarks>
    /// Two empty slots, a missing session and a missing binding each take silence. None of them is
    /// something the player did, and the client has no sentence for "we disagree about your
    /// inventory" -- so the drag simply springs back, which is what it would do if the frame had
    /// been lost.
    /// <para>
    /// A lost race used to be on that list. It is not any more, because it can no longer happen:
    /// a move reads and writes inside one use case boundary and has no second read to disagree with
    /// the first. Two moves arriving at once now both go through, the later settling over the
    /// earlier, which `MoveItemSpaceHandlerTests` records as the attempt to construct it.
    /// </para>
    /// <para>
    /// Every refusal is listed rather than falling through, so the next one someone adds cannot
    /// become silence by default while the test that walks this enum stays green.
    /// </para>
    /// <para>
    /// Raising is not free: the dispatcher does not catch, so the plugin logs and rethrows and the
    /// player's connection drops. That is the right trade only because the raise is unreachable
    /// except through a coding mistake, and because the test that walks this enum is what keeps it
    /// unreachable.
    /// </para>
    /// </remarks>
    internal static byte ErrorCodeFor(ItemMoveStatus status) => status switch
    {
        ItemMoveStatus.NotWearable => MoveItemSpaceResponse.NotMovable,
        ItemMoveStatus.ListNotServed => MoveItemSpaceResponse.NotMovable,
        ItemMoveStatus.ItemCannotOccupySlot => MoveItemSpaceResponse.NotMovable,
        ItemMoveStatus.PartialSwapNotAllowed => MoveItemSpaceResponse.NotMovable,
        ItemMoveStatus.DisplacedCannotOccupySlot => MoveItemSpaceResponse.NotMovable,
        ItemMoveStatus.MergeExceedsStackLimit => MoveItemSpaceResponse.StackLimitReached,
        ItemMoveStatus.NoSession => MoveItemSpaceResponse.Silent,
        ItemMoveStatus.NoBoundContainer => MoveItemSpaceResponse.Silent,
        ItemMoveStatus.BothSlotsEmpty => MoveItemSpaceResponse.Silent,
        ItemMoveStatus.MoveAlreadyMade => MoveItemSpaceResponse.Silent,
        ItemMoveStatus.Moved or ItemMoveStatus.Unchanged => throw new ArgumentOutOfRangeException(
            nameof(status), status, "A move that happened has no error code."),
        _ => throw new ArgumentOutOfRangeException(
            nameof(status), status, "No client code was decided for this refusal."),
    };
}
