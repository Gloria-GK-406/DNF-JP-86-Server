using Dfo.Server.Abstractions.Creatures;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Inventory;

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
internal sealed class InventoryMoveDomainService(
    IBusinessInventorySlotDomainService inventory,
    IItemMovementDomainService moves,
    ICreatureRuntimeDomainService? creatures = null, ISelectedSessionDomainService? sessions = null,
    ICargoTransferDomainService? cargo = null)
    : IInventoryMoveDomainService
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
        space.IsCarriedItemSpace() || space is BusinessInventorySpace.Avatar
            or BusinessInventorySpace.CreatureInventory
            or BusinessInventorySpace.CreatureEquipment
            or BusinessInventorySpace.CreatureConsumable;

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
    private static InventoryMoveRequest Reversed(InventoryMoveRequest command) => command with
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
        ItemReference? atTheEndItNames,
        ItemReference? atTheOtherEnd) =>
        namedInstanceValue > 0
        && atTheEndItNames is null
        && atTheOtherEnd is not null
        && atTheOtherEnd.ItemId == namedInstanceValue;

    public async ValueTask<ItemMoveResult> MoveAsync(InventoryMoveRequest command, CancellationToken cancellationToken)
    {
        var touchesPet = command.SourceSlot.Space == BusinessInventorySpace.WornCreature
            || command.DestinationSlot.Space == BusinessInventorySpace.WornCreature;
        var session = touchesPet ? sessions?.FindByCharacter(command.CharacterId) : null;
        var before = creatures is not null && session is not null
            ? await creatures.AdvanceAsync(session, cancellationToken).ConfigureAwait(false) : [];
        var result = await MoveCoreAsync(command, cancellationToken).ConfigureAwait(false);
        var after = creatures is not null && session is not null
            ? await creatures.AdvanceAsync(session, cancellationToken).ConfigureAwait(false) : [];
        return result with { CreatureChanges = [..before, ..after] };
    }

    private async ValueTask<ItemMoveResult> MoveCoreAsync(
        InventoryMoveRequest command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var boundSource = inventory.Find(command.CharacterId, command.SourceSlot);
        var boundDestination = inventory.Find(command.CharacterId, command.DestinationSlot);
        if (boundSource is not null && boundDestination is not null
            && boundSource.Container.IsPersistent != boundDestination.Container.IsPersistent)
            return ItemMoveResult.Refused(ItemMoveStatus.ItemCannotOccupySlot);

        if (command.SourceSlot.Space is BusinessInventorySpace.PersonalCargo or BusinessInventorySpace.AccountCargo
            || command.DestinationSlot.Space is BusinessInventorySpace.PersonalCargo or BusinessInventorySpace.AccountCargo)
        {
            if (cargo is null || command.Session is not { } selected || selected.CharacterId != command.CharacterId)
                return ItemMoveResult.Refused(ItemMoveStatus.NoBoundContainer);
            var cargoFrom = inventory.Find(command.CharacterId, command.SourceSlot);
            var cargoInto = inventory.Find(command.CharacterId, command.DestinationSlot);
            if (cargoFrom is null || cargoInto is null) return ItemMoveResult.Refused(ItemMoveStatus.NoBoundContainer);
            var cargoFirst = await cargoFrom.Container.GetAsync(command.SourceSlot.Index, cancellationToken).ConfigureAwait(false);
            var cargoSecond = await cargoInto.Container.GetAsync(command.DestinationSlot.Index, cancellationToken).ConfigureAwait(false);
            if (AlreadyCarriedOut(command.SourceInstanceValue, cargoFirst, cargoSecond)
                || AlreadyCarriedOut(command.DestinationInstanceValue, cargoSecond, cargoFirst))
                return ItemMoveResult.Refused(ItemMoveStatus.MoveAlreadyMade);
            var transfer = await cargo.MoveAsync(selected, command.SourceSlot, command.DestinationSlot,
                command.MoveCount, cancellationToken).ConfigureAwait(false);
            return transfer switch
            {
                ItemMoveOutcome.Moved moved => new(ItemMoveStatus.Moved, moved.MoveCount, moved.ItemId)
                    { ActualDestinationSlot = moved.ActualDestinationSlot },
                ItemMoveOutcome.MirrorRemoved removed => new(ItemMoveStatus.MirrorRemoved, 1, removed.ItemId),
                ItemMoveOutcome.Unchanged => new(ItemMoveStatus.Unchanged, 0, 0),
                ItemMoveOutcome.Refused refused => ItemMoveResult.Refused(StatusFor(refused.Reason)),
                _ => throw new InvalidOperationException("Unknown cargo move outcome."),
            };
        }

        if (command.SourceSlot == command.DestinationSlot && boundSource is not null)
            return new(ItemMoveStatus.Unchanged, 0, 0);
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

        if (source.Container.StorageId != destination.Container.StorageId)
        {
            return await this.CarryAcrossAsync(
                command, source, destination, cancellationToken).ConfigureAwait(false);
        }

        // The fourth path the rule reaches. Moving inside one list is the container's own business
        // and it decides by the same reading -- whichever end holds something is the end that moves
        // -- so a restatement undoes itself here exactly as it did across lists. The container is
        // never told what the client named, so the judgement is made before handing over.
        var atSource = await source.Container.GetAsync(source.Slot.Index, cancellationToken)
            .ConfigureAwait(false);
        var atDestination = await destination.Container
            .GetAsync(destination.Slot.Index, cancellationToken)
            .ConfigureAwait(false);
        if (AlreadyCarriedOut(command.SourceInstanceValue, atSource, atDestination))
            return ItemMoveResult.Refused(ItemMoveStatus.MoveAlreadyMade, atDestination!.ItemId);
        if (AlreadyCarriedOut(command.DestinationInstanceValue, atDestination, atSource))
            return ItemMoveResult.Refused(ItemMoveStatus.MoveAlreadyMade, atSource!.ItemId);

        // The business movement service judges both local slots and composes their writes. The
        // transactional command keeps the whole move atomic even when both positions are written.
        var outcome = await moves.MoveWithinBackingAsync(
            source, destination, command.MoveCount, cancellationToken).ConfigureAwait(false);

        return outcome switch
        {
            ItemMoveOutcome.Moved moved =>
                new ItemMoveResult(ItemMoveStatus.Moved, moved.MoveCount, moved.ItemId),
            ItemMoveOutcome.MirrorRemoved removed => new(ItemMoveStatus.MirrorRemoved, 1, removed.ItemId),
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
        InventoryMoveRequest command,
        BoundInventorySlot bag,
        BoundInventorySlot worn,
        CancellationToken cancellationToken)
    {
        var piece = await bag.Container.GetAsync(bag.Slot.Index, cancellationToken)
            .ConfigureAwait(false);
        var displaced = await worn.Container.GetAsync(worn.Slot.Index, cancellationToken)
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

        if (displaced?.Snapshot.IsMirror == true)
        {
            await moves.RemoveMirrorAsync(worn, displaced, cancellationToken).ConfigureAwait(false);
            displaced = null;
        }
        if (displaced is not null && bag.Slot.Index >= await bag.Container.OpenSlotCountAsync(cancellationToken).ConfigureAwait(false))
            return ItemMoveResult.Refused(ItemMoveStatus.DisplacedCannotOccupySlot);
        if (displaced is not null && await moves.MayOccupySlotAsync(
                command.SourceSlot, displaced.ItemId, cancellationToken).ConfigureAwait(false)
            != SlotAdmission.Allowed)
            return ItemMoveResult.Refused(ItemMoveStatus.DisplacedCannotOccupySlot, displaced.ItemId);

        var prepared = await moves.PrepareForWearAsync(piece.Snapshot, displaced?.Snapshot, cancellationToken).ConfigureAwait(false);
        if (prepared != piece.Snapshot)
            piece = await bag.Container.UpdateAsync(piece, _ => prepared, cancellationToken).ConfigureAwait(false);
        var movedRecord = await bag.Container.TakeAsync(piece, null, cancellationToken).ConfigureAwait(false);
        var displacedRecord = displaced is null ? null
            : await worn.Container.TakeAsync(displaced, null, cancellationToken).ConfigureAwait(false);
        await worn.Container.PutAsync(worn.Slot.Index, movedRecord, cancellationToken).ConfigureAwait(false);
        if (displacedRecord is not null)
            await bag.Container.PutAsync(bag.Slot.Index, displacedRecord, cancellationToken).ConfigureAwait(false);

        await bag.Container.FlushAsync(cancellationToken).ConfigureAwait(false);

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
        InventoryMoveRequest command,
        BoundInventorySlot from,
        BoundInventorySlot into,
        CancellationToken cancellationToken)
    {
        var moving = await from.Container.GetAsync(from.Slot.Index, cancellationToken)
            .ConfigureAwait(false);
        var displaced = await into.Container.GetAsync(into.Slot.Index, cancellationToken)
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

        if (into.Slot.Index >= await into.Container.OpenSlotCountAsync(cancellationToken).ConfigureAwait(false))
            return ItemMoveResult.Refused(ItemMoveStatus.ItemCannotOccupySlot);
        if (displaced is not null && from.Slot.Index >= await from.Container.OpenSlotCountAsync(cancellationToken).ConfigureAwait(false))
            return ItemMoveResult.Refused(ItemMoveStatus.DisplacedCannotOccupySlot);
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

        if (await moves.RemoveMirrorAsync(from, moving, cancellationToken).ConfigureAwait(false))
            return new ItemMoveResult(ItemMoveStatus.MirrorRemoved, OnePiece, moving.ItemId);
        if (displaced?.Snapshot.IsMirror == true)
        {
            await moves.RemoveMirrorAsync(into, displaced, cancellationToken).ConfigureAwait(false);
            displaced = null;
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

        var movedRecord = await from.Container.TakeAsync(moving, null, cancellationToken).ConfigureAwait(false);
        var displacedRecord = displaced is null ? null
            : await into.Container.TakeAsync(displaced, null, cancellationToken).ConfigureAwait(false);
        await into.Container.PutAsync(into.Slot.Index, movedRecord, cancellationToken).ConfigureAwait(false);
        if (displacedRecord is not null)
            await from.Container.PutAsync(from.Slot.Index, displacedRecord, cancellationToken).ConfigureAwait(false);

        await from.Container.FlushAsync(cancellationToken).ConfigureAwait(false);

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
        InventoryMoveRequest command,
        BoundInventorySlot worn,
        BoundInventorySlot bag,
        CancellationToken cancellationToken)
    {
        var piece = await worn.Container.GetAsync(worn.Slot.Index, cancellationToken)
            .ConfigureAwait(false);
        var displaced = await bag.Container.GetAsync(bag.Slot.Index, cancellationToken)
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

        if (bag.Slot.Index >= await bag.Container.OpenSlotCountAsync(cancellationToken).ConfigureAwait(false))
            return ItemMoveResult.Refused(ItemMoveStatus.ItemCannotOccupySlot);
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

        if (await moves.RemoveMirrorAsync(worn, piece, cancellationToken).ConfigureAwait(false))
            return new ItemMoveResult(ItemMoveStatus.MirrorRemoved, OnePiece, piece.ItemId);

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

        if (displaced is not null)
        {
            var prepared = await moves.PrepareForWearAsync(displaced.Snapshot, piece.Snapshot, cancellationToken).ConfigureAwait(false);
            if (prepared != displaced.Snapshot)
                displaced = await bag.Container.UpdateAsync(displaced, _ => prepared, cancellationToken).ConfigureAwait(false);
        }
        var movedRecord = await worn.Container.TakeAsync(piece, null, cancellationToken).ConfigureAwait(false);
        var displacedRecord = displaced is null ? null
            : await bag.Container.TakeAsync(displaced, null, cancellationToken).ConfigureAwait(false);
        await bag.Container.PutAsync(bag.Slot.Index, movedRecord, cancellationToken).ConfigureAwait(false);
        if (displacedRecord is not null)
            await worn.Container.PutAsync(worn.Slot.Index, displacedRecord, cancellationToken).ConfigureAwait(false);

        await worn.Container.FlushAsync(cancellationToken).ConfigureAwait(false);

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
        ItemMoveRefusal.ContainerFull => ItemMoveStatus.ContainerFull,
        ItemMoveRefusal.PartialSwapNotAllowed => ItemMoveStatus.PartialSwapNotAllowed,
        ItemMoveRefusal.DisplacedCannotOccupySlot => ItemMoveStatus.DisplacedCannotOccupySlot,
        _ => throw new ArgumentOutOfRangeException(
            nameof(refusal), refusal, "No status was decided for this refusal."),
    };
}

