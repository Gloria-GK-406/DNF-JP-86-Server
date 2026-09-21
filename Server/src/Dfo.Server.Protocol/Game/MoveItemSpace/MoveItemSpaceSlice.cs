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
using Dfo.Server.Protocol.Game.StateSynchronization;
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
    GameSlot SourceSlot,
    int SourceInstanceValue,
    int MoveCount,
    GameSlot DestinationSlot,
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
    GameSlot SourceSlot,
    int MoveValue,
    GameSlot DestinationSlot) : IGameCmdMessage
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
        GameSlot source, int moveValue, GameSlot destination) =>
        new(null, source, moveValue, destination);

    public static MoveItemSpaceResponse Failure(
        GameSlot source, GameSlot destination, byte errorCode) =>
        new(errorCode, source, 0, destination);
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
            return [0x00, errorCode, (byte)message.SourceSlot.Space,
                (byte)message.DestinationSlot.Space];

        var body = new byte[11];
        body[0] = 0x01;
        body[1] = (byte)message.SourceSlot.Space;
        BinaryPrimitives.WriteInt16LittleEndian(
            body.AsSpan(2, 2), checked((short)message.SourceSlot.Index));
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(4, 4), message.MoveValue);
        body[8] = (byte)message.DestinationSlot.Space;
        BinaryPrimitives.WriteInt16LittleEndian(
            body.AsSpan(9, 2), checked((short)message.DestinationSlot.Index));
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
            new GameSlot(
                (InventoryContainerType)body[0],
                BinaryPrimitives.ReadInt16LittleEndian(body[1..])),
            BinaryPrimitives.ReadInt32LittleEndian(body[3..]),
            BinaryPrimitives.ReadInt32LittleEndian(body[7..]),
            new GameSlot(
                (InventoryContainerType)body[11],
                BinaryPrimitives.ReadInt16LittleEndian(body[12..])),
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
public sealed record MoveItemSpaceCommand(
    CharacterId CharacterId,
    BusinessSlot SourceSlot,
    int MoveCount,
    BusinessSlot DestinationSlot,
    int SourceInstanceValue = 0,
    int DestinationInstanceValue = 0) : ICommand<ItemMoveResult>;

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
    IBusinessInventorySlotDomainService inventory,
    IItemMovementDomainService moves)
    : ICommandHandler<MoveItemSpaceCommand, ItemMoveResult>
{
    /// <summary>A piece of equipment does not stack, so exactly one of it ever moves.</summary>
    private const int OnePiece = 1;

    /// <summary>The lists a character carries things in, as opposed to wears them from.</summary>
    /// <remarks>
    /// Both are ordinary containers with a slot rule of their own, so a move within either, or
    /// between them, is the same operation with different ends. Naming them once is what keeps the
    /// four served combinations from being four branches that could drift apart.
    /// </remarks>
    private static bool IsCarried(BusinessInventorySpace space) =>
        space.IsCarriedItemSpace() || space is BusinessInventorySpace.Avatar;

    private static bool IsWorn(BusinessInventorySpace space) => space is
        BusinessInventorySpace.WornAvatar
        or BusinessInventorySpace.WornEquipment
        or BusinessInventorySpace.WornCreature
        or BusinessInventorySpace.WornCreatureEquipment
        or BusinessInventorySpace.WornEquipmentExtension;

    /// <summary>The same move, said from the end that turned out to be holding the item.</summary>
    /// <remarks>
    /// A request does not say which of its two ends the item is on; the client puts it on the end
    /// the drag started from, and that is either end depending on which way the player dragged.
    /// Reversing a served pair always gives another served pair -- carrying across becomes carrying
    /// across the other way, putting on becomes taking off, and taking off becomes putting on --
    /// so the path that answers the reversed request is one of the three already here, and none of
    /// the rules about what may sit where has to be restated to reach it.
    /// <para>
    /// Neither identity travels. A reversed request names no end of its own, and the one decision
    /// that reads them (<see cref="ItemMoveStatus.MoveAlreadyMade"/>) has already been made on both
    /// ends by the time this is called -- carrying them over would put the same frame through that
    /// judgement twice with its ends swapped, which is a different question from the one asked.
    /// </para>
    /// <para>
    /// Reversal happens at most once per request. Each path reverses only on finding its own end
    /// empty, and hands over only after finding the other end occupied -- so the path it hands to
    /// reads a slot that is known to hold something and goes forward from there.
    /// </para>
    /// </remarks>
    private static MoveItemSpaceCommand Reversed(MoveItemSpaceCommand command) => command with
    {
        SourceSlot = command.DestinationSlot,
        DestinationSlot = command.SourceSlot,
        SourceInstanceValue = 0,
        DestinationInstanceValue = 0,
    };

    /// <summary>Whether the request describes a move the store shows as already carried out.</summary>
    /// <remarks>
    /// A request names an item's identity on the end that item is coming from. So when the end it
    /// names is empty and the store holds that very item at the other end, the client is describing
    /// something that has already happened -- it is talking about the picture it had before the
    /// answer reached it.
    /// <para>
    /// <b>Its safety is that it is a contradiction rather than a resemblance</b>, which is what
    /// lets it refuse without judging what a move ought to look like. Every fresh drag that has
    /// been observed names the item on an end that holds it, so none of them reaches this.
    /// </para>
    /// <para>
    /// <b>That is evidence and not a proof, and one shape would break it.</b> A client that named
    /// the identity of the piece it is moving on an <b>empty destination end</b> would satisfy all
    /// four clauses while asking for something legitimate. The eleven put-ons that have been
    /// observed all left that field zero, so nothing in the evidence reaches it -- but "not
    /// observed" is not "cannot happen", and an earlier revision of this remark stated the
    /// stronger claim. There is a second reading of the frame under which the shape cannot exist
    /// at all; the two readings fit every observation equally and settling it needs a capture.
    /// See the attempt's change list under "The one shape that could misfire".
    /// </para>
    /// <para>
    /// Both directions are this one sentence, which is why it is a function rather than two
    /// conditions written out twice the other way round. The forward half of it was here first,
    /// copied from upstream, whose own condition names only the direction it was written for.
    /// </para>
    /// </remarks>
    private static bool AlreadyCarriedOut(
        int namedInstanceValue,
        ContainerSlotItem? atTheEndItNames,
        ContainerSlotItem? atTheOtherEnd) =>
        namedInstanceValue > 0
        && atTheEndItNames is null
        && atTheOtherEnd is not null
        && atTheOtherEnd.ItemId == namedInstanceValue;

    public async ValueTask<ItemMoveResult> HandleAsync(
        CommandExecutionContext context,
        MoveItemSpaceCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var sourceIsCarried = IsCarried(command.SourceSlot.Space);
        var destinationIsCarried = IsCarried(command.DestinationSlot.Space);
        var sourceIsWorn = IsWorn(command.SourceSlot.Space);
        var destinationIsWorn = IsWorn(command.DestinationSlot.Space);

        if (sourceIsCarried && destinationIsWorn)
        {
            var from = inventory.Find(command.CharacterId, command.SourceSlot);
            var into = inventory.Find(command.CharacterId, command.DestinationSlot);
            return from is null
                ? ItemMoveResult.Refused(ItemMoveStatus.NoBoundContainer)
                : into is null
                    ? ItemMoveResult.Refused(ItemMoveStatus.NoBoundContainer)
                    : await this.PutOnAsync(command, from, into, cancellationToken)
                        .ConfigureAwait(false);
        }

        if (sourceIsWorn && destinationIsCarried)
        {
            var from = inventory.Find(command.CharacterId, command.SourceSlot);
            var into = inventory.Find(command.CharacterId, command.DestinationSlot);
            return into is null
                ? ItemMoveResult.Refused(ItemMoveStatus.NoBoundContainer)
                : from is null
                    ? ItemMoveResult.Refused(ItemMoveStatus.NoBoundContainer)
                    : await this.TakeOffAsync(command, from, into, cancellationToken)
                        .ConfigureAwait(false);
        }

        if (!sourceIsCarried || !destinationIsCarried)
            return ItemMoveResult.Refused(ItemMoveStatus.ListNotServed);

        var source = inventory.Find(command.CharacterId, command.SourceSlot);
        var destination = inventory.Find(command.CharacterId, command.DestinationSlot);
        if (source is null || destination is null)
            return ItemMoveResult.Refused(ItemMoveStatus.NoBoundContainer);

        if (!ReferenceEquals(source.Container, destination.Container))
        {
            return await this.CarryAcrossAsync(
                command, source, destination, cancellationToken).ConfigureAwait(false);
        }

        // The fourth path the rule reaches. Moving inside one list is the container's own business
        // and it decides by the same reading -- whichever end holds something is the end that moves
        // -- so a restatement undoes itself here exactly as it did across lists. The container is
        // never told what the client named, so the judgement is made before handing over.
        var atSource = await source.Container.ReadSlotAsync(source.StorageIndex, cancellationToken)
            .ConfigureAwait(false);
        var atDestination = await destination.Container
            .ReadSlotAsync(destination.StorageIndex, cancellationToken)
            .ConfigureAwait(false);
        if (AlreadyCarriedOut(command.SourceInstanceValue, atSource, atDestination))
            return ItemMoveResult.Refused(ItemMoveStatus.MoveAlreadyMade, atDestination!.ItemId);
        if (AlreadyCarriedOut(command.DestinationInstanceValue, atDestination, atSource))
            return ItemMoveResult.Refused(ItemMoveStatus.MoveAlreadyMade, atSource!.ItemId);

        // The move is carried out inside the bag as the clears and writes it is made of. Everything
        // they touch settles together, because the command is transactional and the boundary around
        // it is what makes four writes one change.
        var outcome = await source.Container.MoveToAsync(
            source.StorageIndex,
            destination.StorageIndex,
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
        MoveItemSpaceCommand command,
        BoundInventorySlot bag,
        BoundInventorySlot worn,
        CancellationToken cancellationToken)
    {
        var piece = await bag.Container.ReadSlotAsync(bag.StorageIndex, cancellationToken)
            .ConfigureAwait(false);
        var displaced = await worn.Container.ReadSlotAsync(worn.StorageIndex, cancellationToken)
            .ConfigureAwait(false);

        if (AlreadyCarriedOut(command.SourceInstanceValue, piece, displaced))
            return ItemMoveResult.Refused(ItemMoveStatus.MoveAlreadyMade, displaced!.ItemId);
        if (AlreadyCarriedOut(command.DestinationInstanceValue, displaced, piece))
            return ItemMoveResult.Refused(ItemMoveStatus.MoveAlreadyMade, piece!.ItemId);

        if (piece is null)
        {
            if (displaced is null)
                return ItemMoveResult.Refused(ItemMoveStatus.BothSlotsEmpty);

            return await this.TakeOffAsync(Reversed(command), worn, bag, cancellationToken)
                .ConfigureAwait(false);
        }

        var slot = await moves.WornSlotForAsync(piece.ItemId, cancellationToken)
            .ConfigureAwait(false);
        if (slot is null)
            return ItemMoveResult.Refused(ItemMoveStatus.NotWearable, piece.ItemId);
        if (slot.Value != command.DestinationSlot)
            return ItemMoveResult.Refused(ItemMoveStatus.ItemCannotOccupySlot, piece.ItemId);

        if (!await moves.CanWearAsync(command.CharacterId, piece.ItemId, cancellationToken).ConfigureAwait(false))
            return ItemMoveResult.Refused(ItemMoveStatus.ItemCannotOccupySlot, piece.ItemId);

        await worn.Container.PutAsync(worn.StorageIndex, piece.Core, cancellationToken)
            .ConfigureAwait(false);
        if (displaced is null)
        {
            await bag.Container.ClearAsync(bag.StorageIndex, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await bag.Container.PutAsync(bag.StorageIndex, displaced.Core, cancellationToken)
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
        BoundInventorySlot from,
        BoundInventorySlot into,
        CancellationToken cancellationToken)
    {
        var moving = await from.Container.ReadSlotAsync(from.StorageIndex, cancellationToken)
            .ConfigureAwait(false);
        var displaced = await into.Container.ReadSlotAsync(into.StorageIndex, cancellationToken)
            .ConfigureAwait(false);

        if (AlreadyCarriedOut(command.SourceInstanceValue, moving, displaced))
            return ItemMoveResult.Refused(ItemMoveStatus.MoveAlreadyMade, displaced!.ItemId);
        if (AlreadyCarriedOut(command.DestinationInstanceValue, displaced, moving))
            return ItemMoveResult.Refused(ItemMoveStatus.MoveAlreadyMade, moving!.ItemId);

        if (moving is null)
        {
            return displaced is null
                ? ItemMoveResult.Refused(ItemMoveStatus.BothSlotsEmpty)
                : await this.CarryAcrossAsync(
                    Reversed(command), into, from, cancellationToken)
                    .ConfigureAwait(false);
        }

        switch (await moves.MayOccupySlotAsync(
            command.DestinationSlot, moving.ItemId, cancellationToken)
            .ConfigureAwait(false))
        {
            case SlotAdmission.ItemUnknown:
                return ItemMoveResult.Refused(ItemMoveStatus.NotWearable, moving.ItemId);

            case SlotAdmission.SlotNotAllowed:
                return ItemMoveResult.Refused(ItemMoveStatus.ItemCannotOccupySlot, moving.ItemId);

            default:
                break;
        }

        if (displaced is not null)
        {
            var back = await moves.MayOccupySlotAsync(
                command.SourceSlot, displaced.ItemId, cancellationToken)
                .ConfigureAwait(false);
            if (back != SlotAdmission.Allowed)
                return ItemMoveResult.Refused(
                    ItemMoveStatus.DisplacedCannotOccupySlot, moving.ItemId);
        }

        await into.Container.PutAsync(into.StorageIndex, moving.Core, cancellationToken)
            .ConfigureAwait(false);
        if (displaced is null)
        {
            await from.Container.ClearAsync(from.StorageIndex, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            await from.Container.PutAsync(from.StorageIndex, displaced.Core, cancellationToken)
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
        MoveItemSpaceCommand command,
        BoundInventorySlot worn,
        BoundInventorySlot bag,
        CancellationToken cancellationToken)
    {
        var piece = await worn.Container.ReadSlotAsync(worn.StorageIndex, cancellationToken)
            .ConfigureAwait(false);
        var displaced = await bag.Container.ReadSlotAsync(bag.StorageIndex, cancellationToken)
            .ConfigureAwait(false);

        if (AlreadyCarriedOut(command.SourceInstanceValue, piece, displaced))
            return ItemMoveResult.Refused(ItemMoveStatus.MoveAlreadyMade, displaced!.ItemId);
        if (AlreadyCarriedOut(command.DestinationInstanceValue, displaced, piece))
            return ItemMoveResult.Refused(ItemMoveStatus.MoveAlreadyMade, piece!.ItemId);

        if (piece is null)
        {
            return displaced is null
                ? ItemMoveResult.Refused(ItemMoveStatus.BothSlotsEmpty)
                : await this.PutOnAsync(Reversed(command), bag, worn, cancellationToken)
                    .ConfigureAwait(false);
        }

        switch (await moves.MayOccupySlotAsync(
            command.DestinationSlot, piece.ItemId, cancellationToken)
            .ConfigureAwait(false))
        {
            case SlotAdmission.ItemUnknown:
                return ItemMoveResult.Refused(ItemMoveStatus.NotWearable, piece.ItemId);

            case SlotAdmission.SlotNotAllowed:
                return ItemMoveResult.Refused(ItemMoveStatus.ItemCannotOccupySlot, piece.ItemId);

            default:
                break;
        }

        if (displaced is not null)
        {
            var displacedSlot = await moves
                .WornSlotForAsync(displaced.ItemId, cancellationToken)
                .ConfigureAwait(false);
            if (!await moves.CanWearAsync(command.CharacterId, displaced.ItemId, cancellationToken).ConfigureAwait(false))
                return ItemMoveResult.Refused(ItemMoveStatus.DisplacedCannotOccupySlot, displaced.ItemId);
            if (displacedSlot != command.SourceSlot)
                return ItemMoveResult.Refused(
                    ItemMoveStatus.DisplacedCannotOccupySlot, piece.ItemId);
        }

        await bag.Container.PutAsync(bag.StorageIndex, piece.Core, cancellationToken)
            .ConfigureAwait(false);
        if (displaced is null)
        {
            await worn.Container.ClearAsync(worn.StorageIndex, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            await worn.Container.PutAsync(worn.StorageIndex, displaced.Core, cancellationToken)
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
    ISelectedCharacterCurrentViewDomainService currentViews,
    IInventorySlotMappingRuleService slotMappings,
    ICommandBus commandBus,
    IGameMessageSender sender,
    ILogger<MoveItemSpaceHandler> logger) : ClientMessageHandler<MoveItemSpaceRequest>
{
    protected override async ValueTask HandleMessageAsync(
        NetworkDispatchContext context,
        MoveItemSpaceRequest message,
        CancellationToken cancellationToken)
    {
        var (response, result) = await ResolveAsync(context, message, cancellationToken)
            .ConfigureAwait(false);
        List<IGameServerMessage> frames = [response];
        if (result?.Status is ItemMoveStatus.Moved)
        {
            // Nothing raised while working out the appearance may cost the player the
            // acknowledgement. The move is committed by this point and the client holds the
            // inventory shut until it is answered -- which is the very symptom this frame exists to
            // remove, and it would come back wearing an unrelated cause. So the appearance is
            // attempted, and a failure to build it leaves the answer as it was before this work:
            // the acknowledgement, alone.
            try
            {
                if (await this.RefreshSessionAsync(
                        context,
                        ChangesWhatTheCharacterLooksLike(
                            response.SourceSlot, response.DestinationSlot),
                        cancellationToken)
                        .ConfigureAwait(false)
                    is { } appearance)
                {
                    frames.Add(appearance);
                }
            }
            catch (Exception failure) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogError(
                    failure,
                    "Move item space {ConnectionId}: the move stands; inventory refresh or appearance failed",
                    context.Connection.ConnectionId);
            }
        }

        await sender.SendExplicitAsync(
            context.Connection.ConnectionId,
            new GameExplicitMessageDelivery(frames, []),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Whether this request named a worn slot the character's drawn appearance depends on.
    /// </summary>
    /// <remarks>
    /// Decided from the request's two ends rather than from what the move turned out to do. The
    /// appearance is drawn from the equipment list, so a move that changed it named that list at one
    /// of its ends -- and reversing a request exchanges its two ends without changing either list,
    /// so this holds whichever end the item was actually on.
    /// <para>
    /// Which worn slots the frame depends on is
    /// <see cref="StateSynchronizationPayloadCodec.CurrentUserStateDrawsFrom"/>'s answer, asked
    /// rather than restated. A slot number belonging to the bag or the avatar page is not put to it.
    /// </para>
    /// </remarks>
    private static bool ChangesWhatTheCharacterLooksLike(
        GameSlot source, GameSlot destination) =>
        (source.Space == InventoryContainerType.Equipment
            && StateSynchronizationPayloadCodec.CurrentUserStateDrawsFrom(source.Index))
        || (destination.Space == InventoryContainerType.Equipment
            && StateSynchronizationPayloadCodec.CurrentUserStateDrawsFrom(
                destination.Index));

    /// <summary>Reads the post-move equipment from its owner for the optional appearance frame.</summary>
    private async ValueTask<IGameServerMessage?> RefreshSessionAsync(
        NetworkDispatchContext context, bool draw, CancellationToken cancellationToken)
    {
        var session = selectedSessions.FindByConnection(context.Connection.ConnectionId);
        if (session is null || !draw)
        {
            return null;
        }

        var state = await currentViews.TryLoadAsync(session, cancellationToken).ConfigureAwait(false);
        if (state is null)
            return null;

        var equipment = state.CharacterInventory.Containers
            .SingleOrDefault(value => value.ContainerType == InventoryContainerType.Equipment)
            ?.Items ?? [];

        return new CurrentUserStateMessage(
            state.Character,
            state.CharacterEntry.Public,
            equipment,
            state.CharacterEntry.Features.Pets);
    }

    private async ValueTask<(MoveItemSpaceResponse Response, ItemMoveResult? Result)> ResolveAsync(
        NetworkDispatchContext context,
        MoveItemSpaceRequest message,
        CancellationToken cancellationToken)
    {
        var source = slotMappings.ToBusinessSlot(message.SourceSlot);
        var destination = slotMappings.ToBusinessSlot(message.DestinationSlot);
        var responseSource = slotMappings.ToGameSlot(source);
        var responseDestination = slotMappings.ToGameSlot(destination);
        var session = selectedSessions.FindByConnection(context.Connection.ConnectionId);
        if (session is null)
        {
            logger.LogWarning(
                "Move item space {ConnectionId}: no published session",
                context.Connection.ConnectionId);
            return (MoveItemSpaceResponse.Failure(
                responseSource, responseDestination, MoveItemSpaceResponse.Silent), null);
        }

        var result = await commandBus.SendAsync(
            new MoveItemSpaceCommand(
                session.CharacterId,
                source,
                message.MoveCount,
                destination,
                message.SourceInstanceValue,
                message.DestinationInstanceValue),
            cancellationToken).ConfigureAwait(false);

        if (result.Status is ItemMoveStatus.Moved or ItemMoveStatus.Unchanged)
        {
            logger.LogInformation(
                "Move item space {ConnectionId} character={CharacterId}: item {ItemId} "
                    + "from list {FromList} slot {From} to list {ToList} slot {To} count={Count}",
                context.Connection.ConnectionId,
                session.CharacterId.Value,
                result.ItemId,
                (byte)responseSource.Space,
                responseSource.Index,
                (byte)responseDestination.Space,
                responseDestination.Index,
                result.MoveValue);
            return (
                MoveItemSpaceResponse.Success(
                    responseSource, result.MoveValue, responseDestination),
                result);
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
            (byte)responseSource.Space,
            responseSource.Index,
            (byte)responseDestination.Space,
            responseDestination.Index,
            result.Status);
        return (MoveItemSpaceResponse.Failure(
            responseSource, responseDestination, ErrorCodeFor(result.Status)), result);
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
