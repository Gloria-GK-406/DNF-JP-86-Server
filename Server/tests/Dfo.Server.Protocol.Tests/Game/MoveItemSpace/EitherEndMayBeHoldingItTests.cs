using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.MoveItemSpace;
using Microsoft.Extensions.Logging;
using Dfo.Server.Data.Game;

namespace Dfo.Server.Protocol.Tests.Game.MoveItemSpace;

/// <summary>
/// Which of a move's two ends is holding the item, and what follows from not knowing in advance.
/// </summary>
/// <remarks>
/// A request names two ends and does not say which one the item is on. The client puts it on the
/// end the drag started from, so a drag out of the bag names it first and a drag off the character
/// names it second -- both observed live, on 2026-09-06 and 2026-09-08 respectively.
/// <para>
/// The move inside one list has read both ends since before this file existed
/// (<c>StoreBackedItemContainer.MoveToAsync</c>). The three paths that cross two lists did not, so
/// every drag arriving the second way was refused as though the inventory were empty -- which is
/// what a player taking a costume off was answered with, `BothSlotsEmpty` naming no item at all.
/// </para>
/// <para>
/// The pairs these cases walk are not written out here. They are read back from the dispatch by
/// asking it about every pair of list numbers and keeping the ones it does not turn away, so a
/// list added to or taken from the dispatch changes what is walked instead of leaving a
/// hand-written list behind. What that enumeration comes to is asserted separately, once, so the
/// change shows up as a failure rather than as silently fewer cases.
/// </para>
/// </remarks>
public sealed class EitherEndMayBeHoldingItTests(ArchiveContentFixture archive)
    : IClassFixture<ArchiveContentFixture>
{
    private const int Character = 60;

    /// <summary>A costume the archive declares under <c>[coat avatar]</c>, worn at slot 3.</summary>
    private const int Costume = 413500000;

    /// <summary>An ordinary coat, which the archive declares under <c>[coat]</c>, worn at 14.</summary>
    private const int Ordinary = 10018;

    /// <summary>An identifier the archive declares nothing under.</summary>
    private const int Unknown = 999999999;

    /// <summary>A bag slot inside the equipment band.</summary>
    private const short BagSlot = 12;
    private const short OtherBagSlot = 13;

    /// <summary>A bag slot in the consumable band, where one may not.</summary>
    private const short ConsumableBagSlot = 65;

    private const short AvatarSlot = 0;

    /// <summary>Where the archive says a coat avatar is worn.</summary>
    private const short WornCostumeSlot = 3;

    private static readonly NetworkConnectionId Connection = new("either-end");

    private static readonly NetworkDispatchContext Dispatch = new(
        new NetworkConnectionContext(Connection, NetworkEndpointKind.Game, 10011));

    /// <summary>The lists the dispatch serves, as the dispatch answers rather than as a list.</summary>
    /// <remarks>
    /// Read by asking about every ordered pair of container types with both ends empty and keeping
    /// the pairs that come back as anything other than <c>ListNotServed</c> -- that refusal is
    /// decided from the two list numbers alone, before either slot is read, so an empty inventory
    /// separates served from unserved and nothing else does.
    /// </remarks>
    [Dfo.Server.Testing.PvfFact]
    public async Task The_dispatch_serves_these_pairs_of_lists_and_no_others()
    {
        var served = await this.ServedPairsAsync();

        Assert.Equal(
            [
                (InventoryContainerType.Main, InventoryContainerType.Main),
                (InventoryContainerType.Main, InventoryContainerType.Avatar),
                (InventoryContainerType.Main, InventoryContainerType.Equipment),
                (InventoryContainerType.Main, InventoryContainerType.Pet),
                (InventoryContainerType.Avatar, InventoryContainerType.Main),
                (InventoryContainerType.Avatar, InventoryContainerType.Avatar),
                (InventoryContainerType.Avatar, InventoryContainerType.Equipment),
                (InventoryContainerType.Avatar, InventoryContainerType.Pet),
                (InventoryContainerType.Equipment, InventoryContainerType.Main),
                (InventoryContainerType.Equipment, InventoryContainerType.Avatar),
                (InventoryContainerType.Equipment, InventoryContainerType.Pet),
                (InventoryContainerType.Pet, InventoryContainerType.Main),
                (InventoryContainerType.Pet, InventoryContainerType.Avatar),
                (InventoryContainerType.Pet, InventoryContainerType.Equipment),
                (InventoryContainerType.Pet, InventoryContainerType.Pet),
            ],
            served);
    }

    /// <summary>
    /// For every served pair that crosses two lists, the end that is holding something is the end
    /// that moves.
    /// </summary>
    [Dfo.Server.Testing.PvfFact]
    public async Task Whichever_end_holds_the_item_is_the_end_that_moves()
    {
        foreach (var (first, firstSlot, second, secondSlot, itemId) in LegalCrossListPairs())
        {
            var side = this.Build();
            side.Seed(second, secondSlot, itemId);

            var result = await side.MoveAsync(first, firstSlot, second, secondSlot);

            Assert.Equal(ItemMoveStatus.Moved, result.Status);
            Assert.Equal(itemId, result.ItemId);
            Assert.Equal(1, result.MoveValue);
            Assert.Equal(itemId, (await side.ReadAsync(first, firstSlot))!.ItemId);
            Assert.Null(await side.ReadAsync(second, secondSlot));
        }
    }

    /// <summary>The forward direction of each of those pairs still works, unchanged.</summary>
    [Dfo.Server.Testing.PvfFact]
    public async Task The_end_named_first_still_moves_when_it_is_the_one_holding_the_item()
    {
        foreach (var (first, firstSlot, second, secondSlot, itemId) in LegalCrossListPairs())
        {
            var side = this.Build();
            side.Seed(first, firstSlot, itemId);

            var result = await side.MoveAsync(first, firstSlot, second, secondSlot);

            Assert.Equal(ItemMoveStatus.Moved, result.Status);
            Assert.Equal(itemId, result.ItemId);
            Assert.Equal(itemId, (await side.ReadAsync(second, secondSlot))!.ItemId);
            Assert.Null(await side.ReadAsync(first, firstSlot));
        }
    }

    /// <summary>
    /// A costume taken off into a bag slot that will not have it is refused the same way whichever
    /// end the client named it on.
    /// </summary>
    /// <remarks>
    /// The consumable band takes no equipment. This is the rule that would be lost if reversing a
    /// move meant writing it without asking, and the pair of assertions is the point: the reversed
    /// request and the forward one are refused alike, so nothing was widened to make the reversal
    /// work.
    /// </remarks>
    [Dfo.Server.Testing.PvfFact]
    public async Task A_reversed_take_off_is_refused_by_the_bag_rule_the_forward_one_obeys()
    {
        var reversed = this.Build();
        reversed.Seed(InventoryContainerType.Equipment, WornCostumeSlot, Costume);

        var whenNamedSecond = await reversed.MoveAsync(
            InventoryContainerType.Main, ConsumableBagSlot,
            InventoryContainerType.Equipment, WornCostumeSlot);

        var forward = this.Build();
        forward.Seed(InventoryContainerType.Equipment, WornCostumeSlot, Costume);

        var whenNamedFirst = await forward.MoveAsync(
            InventoryContainerType.Equipment, WornCostumeSlot,
            InventoryContainerType.Main, ConsumableBagSlot);

        Assert.Equal(ItemMoveStatus.ItemCannotOccupySlot, whenNamedFirst.Status);
        Assert.Equal(whenNamedFirst.Status, whenNamedSecond.Status);
        Assert.Equal(Costume, whenNamedSecond.ItemId);
        Assert.Null(await reversed.ReadAsync(InventoryContainerType.Main, ConsumableBagSlot));
        Assert.Equal(
            Costume,
            (await reversed.ReadAsync(InventoryContainerType.Equipment, WornCostumeSlot))!.ItemId);
    }

    /// <summary>
    /// A piece put on a slot its archive entry does not name is refused the same way whichever end
    /// the client named it on.
    /// </summary>
    [Dfo.Server.Testing.PvfFact]
    public async Task A_reversed_put_on_is_refused_by_the_slot_rule_the_forward_one_obeys()
    {
        var reversed = this.Build();
        reversed.Seed(InventoryContainerType.Main, BagSlot, Ordinary);

        var whenNamedSecond = await reversed.MoveAsync(
            InventoryContainerType.Equipment, WornCostumeSlot,
            InventoryContainerType.Main, BagSlot);

        var forward = this.Build();
        forward.Seed(InventoryContainerType.Main, BagSlot, Ordinary);

        var whenNamedFirst = await forward.MoveAsync(
            InventoryContainerType.Main, BagSlot,
            InventoryContainerType.Equipment, WornCostumeSlot);

        Assert.Equal(ItemMoveStatus.ItemCannotOccupySlot, whenNamedFirst.Status);
        Assert.Equal(whenNamedFirst.Status, whenNamedSecond.Status);
        Assert.Equal(Ordinary, whenNamedSecond.ItemId);
        Assert.Null(await reversed.ReadAsync(InventoryContainerType.Equipment, WornCostumeSlot));
        Assert.Equal(
            Ordinary, (await reversed.ReadAsync(InventoryContainerType.Main, BagSlot))!.ItemId);
    }

    /// <summary>
    /// A reversed carry-across is refused by the receiving list's rule, the same as the forward one.
    /// </summary>
    /// <remarks>
    /// The avatar list takes costumes and nothing else. Without this the reversed carry-across
    /// would be walked only where it succeeds, and the third of the three paths would be the one
    /// with no evidence that its rules survived the reversal.
    /// </remarks>
    [Dfo.Server.Testing.PvfFact]
    public async Task A_reversed_carry_across_is_refused_by_the_list_rule_the_forward_one_obeys()
    {
        var reversed = this.Build();
        reversed.Seed(InventoryContainerType.Main, BagSlot, Ordinary);

        var whenNamedSecond = await reversed.MoveAsync(
            InventoryContainerType.Avatar, AvatarSlot, InventoryContainerType.Main, BagSlot);

        var forward = this.Build();
        forward.Seed(InventoryContainerType.Main, BagSlot, Ordinary);

        var whenNamedFirst = await forward.MoveAsync(
            InventoryContainerType.Main, BagSlot, InventoryContainerType.Avatar, AvatarSlot);

        Assert.Equal(ItemMoveStatus.ItemCannotOccupySlot, whenNamedFirst.Status);
        Assert.Equal(whenNamedFirst.Status, whenNamedSecond.Status);
        Assert.Equal(Ordinary, whenNamedSecond.ItemId);
        Assert.Null(await reversed.ReadAsync(InventoryContainerType.Avatar, AvatarSlot));
        Assert.Equal(
            Ordinary, (await reversed.ReadAsync(InventoryContainerType.Main, BagSlot))!.ItemId);
    }

    /// <summary>
    /// A template the archive names no slot for is refused on the reversed path too.
    /// </summary>
    /// <remarks>
    /// <c>NotWearable</c> is the refusal left for a template the archive declares nothing wearable
    /// under, and it is asked through the same call the forward path asks it through. Walking it
    /// from the reversed side is what shows the delegation reaches the rule rather than going
    /// around it.
    /// </remarks>
    [Dfo.Server.Testing.PvfFact]
    public async Task A_template_the_archive_does_not_know_is_refused_on_the_reversed_path()
    {
        var side = this.Build();
        side.Seed(InventoryContainerType.Main, BagSlot, Unknown);

        var result = await side.MoveAsync(
            InventoryContainerType.Equipment, WornCostumeSlot,
            InventoryContainerType.Main, BagSlot);

        Assert.Equal(ItemMoveStatus.NotWearable, result.Status);
        Assert.Equal(Unknown, result.ItemId);
        Assert.Null(await side.ReadAsync(InventoryContainerType.Equipment, WornCostumeSlot));
    }

    /// <summary>Two empty ends are still two empty ends.</summary>
    [Dfo.Server.Testing.PvfFact]
    public async Task Neither_end_holding_anything_is_still_refused_as_both_slots_empty()
    {
        foreach (var (first, second) in await this.CrossListPairsAsync())
        {
            var side = this.Build();

            var result = await side.MoveAsync(first, SlotIn(first), second, SlotIn(second));

            Assert.Equal(ItemMoveStatus.BothSlotsEmpty, result.Status);
            Assert.Equal(0, result.ItemId);
        }
    }

    /// <summary>A list this server does not serve is still turned away.</summary>
    /// <remarks>
    /// The refusal is decided from the two list numbers before either slot is read, but that is
    /// read off the dispatch rather than observed here: what these assertions can see is the
    /// refusal and a store nobody touched.
    /// </remarks>
    [Dfo.Server.Testing.PvfFact]
    public async Task Unbound_cargo_and_direct_worn_to_worn_moves_are_refused()
    {
        var side = this.Build();
        side.Seed(InventoryContainerType.Equipment, WornCostumeSlot, Costume);

        var fromCargo = await side.MoveAsync(
            InventoryContainerType.PersonalCargo, 0,
            InventoryContainerType.Equipment, WornCostumeSlot);
        var wornToWorn = await side.MoveAsync(
            InventoryContainerType.Equipment, WornCostumeSlot,
            InventoryContainerType.Equipment, 4);

        Assert.Equal(ItemMoveStatus.NoBoundContainer, fromCargo.Status);
        Assert.Equal(ItemMoveStatus.ListNotServed, wornToWorn.Status);
        Assert.Equal(
            Costume,
            (await side.ReadAsync(InventoryContainerType.Equipment, WornCostumeSlot))!.ItemId);
    }

    /// <summary>
    /// A request restating a move the client already has is left alone rather than undone.
    /// </summary>
    /// <remarks>
    /// The three cases together are what makes this narrow. Naming the worn piece's own
    /// identifier on the empty end is the whole trigger: naming nothing lets the take-off through,
    /// and naming something else lets it through too. The identifier equality is what does that
    /// work -- the guard's "greater than zero" clause is upstream's wording carried over, and no
    /// real template identifier is zero, so widening it changes nothing here.
    /// </remarks>
    [Dfo.Server.Testing.PvfFact]
    public async Task A_request_naming_the_worn_piece_on_its_empty_end_does_not_take_it_off()
    {
        var restated = this.Build();
        restated.Seed(InventoryContainerType.Equipment, WornCostumeSlot, Costume);

        var echoed = await restated.MoveAsync(
            InventoryContainerType.Avatar, AvatarSlot,
            InventoryContainerType.Equipment, WornCostumeSlot,
            sourceInstanceValue: Costume);

        Assert.Equal(ItemMoveStatus.MoveAlreadyMade, echoed.Status);
        Assert.Equal(Costume, echoed.ItemId);
        Assert.Equal(0, echoed.MoveValue);
        Assert.Null(await restated.ReadAsync(InventoryContainerType.Avatar, AvatarSlot));
        Assert.Equal(
            Costume,
            (await restated.ReadAsync(InventoryContainerType.Equipment, WornCostumeSlot))!.ItemId);
    }

    [Dfo.Server.Testing.PvfFact]
    public async Task A_request_naming_nothing_on_its_empty_end_takes_the_piece_off()
    {
        var side = this.Build();
        side.Seed(InventoryContainerType.Equipment, WornCostumeSlot, Costume);

        var result = await side.MoveAsync(
            InventoryContainerType.Avatar, AvatarSlot,
            InventoryContainerType.Equipment, WornCostumeSlot,
            sourceInstanceValue: 0);

        Assert.Equal(ItemMoveStatus.Moved, result.Status);
        Assert.Equal(Costume, (await side.ReadAsync(InventoryContainerType.Avatar, AvatarSlot))!.ItemId);
    }

    [Dfo.Server.Testing.PvfFact]
    public async Task A_request_naming_some_other_piece_on_its_empty_end_takes_the_piece_off()
    {
        var side = this.Build();
        side.Seed(InventoryContainerType.Equipment, WornCostumeSlot, Costume);

        var result = await side.MoveAsync(
            InventoryContainerType.Avatar, AvatarSlot,
            InventoryContainerType.Equipment, WornCostumeSlot,
            sourceInstanceValue: Ordinary);

        Assert.Equal(ItemMoveStatus.Moved, result.Status);
        Assert.Equal(Costume, (await side.ReadAsync(InventoryContainerType.Avatar, AvatarSlot))!.ItemId);
    }

    /// <summary>
    /// A take-off the client restates does not put the piece back on.
    /// </summary>
    /// <remarks>
    /// This is the mirror of <see cref="A_request_naming_the_worn_piece_on_its_empty_end_does_not_take_it_off"/>
    /// and the reason the rule is one function rather than two conditions. The client names an item
    /// on the end it is coming from; a take-off names it on the destination end, because the cell
    /// the player released over goes in the source field. Once the take-off has been carried out
    /// the source end is the one holding it, so the frame arrives at the put-on path with a piece
    /// under its hand and, read literally, asks for the move back.
    /// <para>
    /// <b>What no case here can establish is that a client restates verbatim.</b> No restatement has
    /// been observed at all. If the client rebuilds the frame from its new state instead, it names
    /// the item on the source end, is indistinguishable from a fresh drag, and this guard never
    /// fires -- which costs nothing, because the guard only ever refuses a frame that contradicts
    /// itself.
    /// </para>
    /// </remarks>
    [Dfo.Server.Testing.PvfFact]
    public async Task A_restated_take_off_does_not_put_the_piece_back_on()
    {
        var restated = this.Build();
        restated.Seed(InventoryContainerType.Avatar, AvatarSlot, Costume);

        var echoed = await restated.MoveAsync(
            InventoryContainerType.Avatar, AvatarSlot,
            InventoryContainerType.Equipment, WornCostumeSlot,
            destinationInstanceValue: Costume);

        Assert.Equal(ItemMoveStatus.MoveAlreadyMade, echoed.Status);
        Assert.Equal(Costume, echoed.ItemId);
        Assert.Equal(0, echoed.MoveValue);
        Assert.Equal(
            Costume,
            (await restated.ReadAsync(InventoryContainerType.Avatar, AvatarSlot))!.ItemId);
        Assert.Null(
            await restated.ReadAsync(InventoryContainerType.Equipment, WornCostumeSlot));
    }

    /// <summary>
    /// The same two cells, named the other way round, are a fresh put-on and are carried out.
    /// </summary>
    /// <remarks>
    /// It differs from the case above in one field: which end carries the identifier. A guard that
    /// judged the shape of the move rather than which end names what would refuse this one too, so
    /// this is what holds the rule to being about the contradiction and nothing else.
    /// </remarks>
    [Dfo.Server.Testing.PvfFact]
    public async Task The_same_two_cells_named_the_other_way_round_are_a_fresh_put_on()
    {
        var side = this.Build();
        side.Seed(InventoryContainerType.Avatar, AvatarSlot, Costume);

        var result = await side.MoveAsync(
            InventoryContainerType.Avatar, AvatarSlot,
            InventoryContainerType.Equipment, WornCostumeSlot,
            sourceInstanceValue: Costume);

        Assert.Equal(ItemMoveStatus.Moved, result.Status);
        Assert.Equal(
            Costume,
            (await side.ReadAsync(InventoryContainerType.Equipment, WornCostumeSlot))!.ItemId);
    }

    /// <summary>
    /// Naming the destination end while that end holds the piece is a take-off, not a restatement.
    /// </summary>
    /// <remarks>
    /// The guard asks for the named end to be empty. Here it is not: the client is naming the piece
    /// it can see in the worn slot, which is what a take-off does, and the store agrees with it.
    /// Agreement is never a restatement.
    /// </remarks>
    [Dfo.Server.Testing.PvfFact]
    public async Task Naming_the_worn_piece_on_the_end_that_holds_it_still_takes_it_off()
    {
        var side = this.Build();
        side.Seed(InventoryContainerType.Equipment, WornCostumeSlot, Costume);

        var result = await side.MoveAsync(
            InventoryContainerType.Avatar, AvatarSlot,
            InventoryContainerType.Equipment, WornCostumeSlot,
            destinationInstanceValue: Costume);

        Assert.Equal(ItemMoveStatus.Moved, result.Status);
        Assert.Equal(
            Costume,
            (await side.ReadAsync(InventoryContainerType.Avatar, AvatarSlot))!.ItemId);
    }

    /// <summary>
    /// Carrying a piece across, restated, does not carry it back.
    /// </summary>
    /// <remarks>
    /// The same rule, on the path between the bag and the avatar page. Included because the defect
    /// was reported about a costume coming off, and a reader could reasonably think this were only
    /// about the worn list.
    /// </remarks>
    [Dfo.Server.Testing.PvfFact]
    public async Task A_restated_carry_across_does_not_carry_it_back()
    {
        var restated = this.Build();
        restated.Seed(InventoryContainerType.Main, BagSlot, Ordinary);

        var echoed = await restated.MoveAsync(
            InventoryContainerType.Main, BagSlot,
            InventoryContainerType.Main, OtherBagSlot,
            destinationInstanceValue: Ordinary);

        Assert.Equal(ItemMoveStatus.MoveAlreadyMade, echoed.Status);
        Assert.Equal(Ordinary, echoed.ItemId);
        Assert.Equal(
            Ordinary,
            (await restated.ReadAsync(InventoryContainerType.Main, BagSlot))!.ItemId);
        Assert.Null(await restated.ReadAsync(InventoryContainerType.Main, OtherBagSlot));
    }

    /// <summary>
    /// A drag inside one list, restated, does not drag it back.
    /// </summary>
    /// <remarks>
    /// The fourth path. Moving within a list is the container's own business and it decides the
    /// same way, so this had the same defect as the three that cross lists -- and it was missed
    /// when the rule was first applied, because the walk started from the paths the report named.
    /// </remarks>
    [Dfo.Server.Testing.PvfFact]
    public async Task A_restated_drag_inside_one_list_does_not_drag_it_back()
    {
        var restated = this.Build();
        restated.Seed(InventoryContainerType.Main, BagSlot, Ordinary);

        var echoed = await restated.MoveAsync(
            InventoryContainerType.Main, BagSlot,
            InventoryContainerType.Main, OtherBagSlot,
            destinationInstanceValue: Ordinary);

        Assert.Equal(ItemMoveStatus.MoveAlreadyMade, echoed.Status);
        Assert.Equal(Ordinary, echoed.ItemId);
        Assert.Equal(
            Ordinary,
            (await restated.ReadAsync(InventoryContainerType.Main, BagSlot))!.ItemId);
        Assert.Null(await restated.ReadAsync(InventoryContainerType.Main, OtherBagSlot));
    }

    /// <summary>
    /// Putting a piece onto a worn slot that already holds one is carried out, whatever either end
    /// names.
    /// </summary>
    /// <remarks>
    /// Both ends hold something, so the rule's first clause -- that the end being named is empty --
    /// is false whichever way it is asked, and the move goes through.
    /// <para>
    /// <b>The status is what carries this case; the two content assertions cannot.</b> Both cells
    /// hold the same template before and after, so reading them back proves only that nothing was
    /// lost -- a no-op would satisfy them too. <c>Moved</c> is what a refusal and a no-op cannot
    /// both produce, and it is asserted positively for that reason.
    /// </para>
    /// <para>
    /// <b>Both ends hold the same template on purpose.</b> That is the closest thing to a
    /// counter-example the rule has: if the judgement compared identifiers before it looked at
    /// occupancy, two pieces of one template would read as the item having moved. It does not --
    /// occupancy is asked first, of the end being named.
    /// </para>
    /// <para>
    /// An earlier version of this case moved an ordinary coat onto the avatar page and asserted
    /// only that the answer was not <c>MoveAlreadyMade</c>. The avatar list refuses coats, so the
    /// answer was <c>ItemCannotOccupySlot</c> and nothing was ever swapped; the negative assertion
    /// passed anyway. It was found by a reviewer strengthening the assertion, and it is the reason
    /// this one asserts where each piece ended up.
    /// </para>
    /// </remarks>
    [Dfo.Server.Testing.PvfFact]
    public async Task A_swap_onto_an_occupied_worn_slot_is_carried_out_whatever_either_end_names()
    {
        var side = this.Build();
        side.Seed(InventoryContainerType.Avatar, AvatarSlot, Costume);
        side.Seed(InventoryContainerType.Equipment, WornCostumeSlot, Costume);

        var result = await side.MoveAsync(
            InventoryContainerType.Avatar, AvatarSlot,
            InventoryContainerType.Equipment, WornCostumeSlot,
            sourceInstanceValue: Costume,
            destinationInstanceValue: Costume);

        Assert.Equal(ItemMoveStatus.Moved, result.Status);
        Assert.Equal(
            Costume,
            (await side.ReadAsync(InventoryContainerType.Equipment, WornCostumeSlot))!.ItemId);
        Assert.Equal(
            Costume,
            (await side.ReadAsync(InventoryContainerType.Avatar, AvatarSlot))!.ItemId);
    }

    /// <summary>
    /// Dropping a piece back where it already is stays the no-op it was.
    /// </summary>
    /// <remarks>
    /// The client sends this and expects it to succeed, so it is the busiest frame arriving in
    /// front of the same-list judgement. Both reads look at one cell: occupied, and the end being
    /// named is not empty; empty, and neither end holds anything. The rule cannot reach it either
    /// way, and this is what says so out loud rather than by argument.
    /// </remarks>
    [Dfo.Server.Testing.PvfFact]
    public async Task Dropping_a_piece_back_where_it_already_is_changes_nothing()
    {
        var side = this.Build();
        side.Seed(InventoryContainerType.Main, BagSlot, Ordinary);

        var result = await side.MoveAsync(
            InventoryContainerType.Main, BagSlot,
            InventoryContainerType.Main, BagSlot,
            sourceInstanceValue: Ordinary,
            destinationInstanceValue: Ordinary);

        Assert.NotEqual(ItemMoveStatus.MoveAlreadyMade, result.Status);
        Assert.Equal(Ordinary, (await side.ReadAsync(InventoryContainerType.Main, BagSlot))!.ItemId);
    }

    /// <summary>
    /// A move naming neither end behaves exactly as it did before the rule existed.
    /// </summary>
    /// <remarks>
    /// Both instance values zero is what a frame shorter than eighteen bytes decodes to, and what a
    /// client that does not fill them sends. The rule's first clause is the one that keeps those
    /// out of its way.
    /// </remarks>
    [Dfo.Server.Testing.PvfFact]
    public async Task A_move_naming_neither_end_is_carried_out()
    {
        var side = this.Build();
        side.Seed(InventoryContainerType.Avatar, AvatarSlot, Costume);

        var result = await side.MoveAsync(
            InventoryContainerType.Avatar, AvatarSlot,
            InventoryContainerType.Equipment, WornCostumeSlot);

        Assert.Equal(ItemMoveStatus.Moved, result.Status);
        Assert.Equal(Costume, (await side.ReadAsync(InventoryContainerType.Equipment, WornCostumeSlot))!.ItemId);
    }

    /// <summary>
    /// The acknowledgement names the request's own two ends, and the store agrees with a client
    /// that reads it as those two cells exchanging what they hold.
    /// </summary>
    /// <remarks>
    /// Upstream answers the same way and is explicit about it: on its reverse branch it sets the
    /// result's destination back to the request's own destination
    /// (<c>InventoryMoveService</c>, read from source). A move into an empty cell and a swap with
    /// an empty cell reach the same place, so a client that applies the acknowledgement as a swap
    /// of the two cells it names is left holding what the store holds.
    /// </remarks>
    [Dfo.Server.Testing.PvfFact]
    public async Task The_acknowledgement_for_a_reversed_move_names_the_two_cells_the_request_did()
    {
        var (handler, sender, _, side) = this.BuildHandler();
        side.Seed(InventoryContainerType.Equipment, WornCostumeSlot, Costume);

        await handler.HandleAsync(Dispatch, TakeOffRequest(), CancellationToken.None);

        var acknowledgement = (MoveItemSpaceResponse)Assert.Single(sender.Sent);
        Assert.Null(acknowledgement.ErrorCode);
        Assert.Equal(InventoryContainerType.Avatar, acknowledgement.SourceSlot.Space);
        Assert.Equal(AvatarSlot, acknowledgement.SourceSlot.Index);
        Assert.Equal(InventoryContainerType.Equipment, acknowledgement.DestinationSlot.Space);
        Assert.Equal(WornCostumeSlot, acknowledgement.DestinationSlot.Index);
        Assert.Equal(1, acknowledgement.MoveValue);

        Assert.Equal(Costume, (await side.ReadAsync(InventoryContainerType.Avatar, AvatarSlot))!.ItemId);
        Assert.Null(await side.ReadAsync(InventoryContainerType.Equipment, WornCostumeSlot));
    }

    /// <summary>The line a move leaves says which lists it was between.</summary>
    /// <remarks>
    /// It named the two slots and neither list until 2026-09-08, and the same pair of numbers
    /// belongs to several different paths -- which is why the take-off defect above took three
    /// live rounds to place rather than one.
    /// </remarks>
    [Dfo.Server.Testing.PvfFact]
    public async Task The_line_a_move_leaves_names_the_list_at_both_ends()
    {
        var (handler, _, logger, side) = this.BuildHandler();
        side.Seed(InventoryContainerType.Equipment, WornCostumeSlot, Costume);

        await handler.HandleAsync(Dispatch, TakeOffRequest(), CancellationToken.None);

        Assert.Equal(
            $"Move item space either-end character={Character}: item {Costume} "
                + $"from list {(byte)InventoryContainerType.Avatar} slot {AvatarSlot} "
                + $"to list {(byte)InventoryContainerType.Equipment} slot {WornCostumeSlot} "
                + "count=1",
            Assert.Single(logger.Entries).Message);
    }

    /// <summary>The drag that started all of this: a costume off the character, into the bag.</summary>
    private static MoveItemSpaceRequest TakeOffRequest() =>
        new(GameMessageMetadata.Empty,
            new GameSlot(InventoryContainerType.Avatar, AvatarSlot),
            SourceInstanceValue: 0,
            MoveCount: 1,
            new GameSlot(InventoryContainerType.Equipment, WornCostumeSlot),
            DestinationInstanceValue: Costume);

    /// <summary>A valid slot in each served list, used with empty ends.</summary>
    private static short SlotIn(InventoryContainerType type) => type switch
    {
        InventoryContainerType.Main => BagSlot,
        InventoryContainerType.Avatar => AvatarSlot,
        InventoryContainerType.Equipment => WornCostumeSlot,
        InventoryContainerType.Pet => 0,
        _ => throw new ArgumentOutOfRangeException(
            nameof(type), type, "No costume slot was chosen for this list."),
    };

    private async ValueTask<IReadOnlyList<(InventoryContainerType First, InventoryContainerType Second)>>
        ServedPairsAsync()
    {
        var served = new List<(InventoryContainerType, InventoryContainerType)>();
        foreach (var first in (InventoryContainerType[])
            [InventoryContainerType.Main, InventoryContainerType.Avatar,
             InventoryContainerType.Equipment, InventoryContainerType.Pet])
        {
            foreach (var second in (InventoryContainerType[])
                [InventoryContainerType.Main, InventoryContainerType.Avatar,
                 InventoryContainerType.Equipment, InventoryContainerType.Pet])
            {
                var side = this.Build();
                var result = await side.MoveAsync(first, SlotIn(first), second, SlotIn(second));
                if (result.Status is not ItemMoveStatus.ListNotServed)
                    served.Add((first, second));
            }
        }

        return served;
    }

    private async ValueTask<IReadOnlyList<(InventoryContainerType First, InventoryContainerType Second)>>
        CrossListPairsAsync() =>
        [.. (await this.ServedPairsAsync()).Where(pair => pair.First != pair.Second)];

    private static (InventoryContainerType First, short FirstSlot,
        InventoryContainerType Second, short SecondSlot, int ItemId)[] LegalCrossListPairs() =>
    [
        (InventoryContainerType.Main, BagSlot, InventoryContainerType.Equipment, 14, Ordinary),
        (InventoryContainerType.Equipment, 14, InventoryContainerType.Main, BagSlot, Ordinary),
        (InventoryContainerType.Avatar, AvatarSlot, InventoryContainerType.Equipment, WornCostumeSlot, Costume),
        (InventoryContainerType.Equipment, WornCostumeSlot, InventoryContainerType.Avatar, AvatarSlot, Costume),
    ];

    private Side Build()
    {
        var character = new CharacterId(Character);
        var containers = new CharacterContainerDomainService();
        foreach (var type in (InventoryContainerType[])
            [InventoryContainerType.Main, InventoryContainerType.Avatar,
             InventoryContainerType.Equipment, InventoryContainerType.Pet])
        {
            containers.Register(character, new SlotBook(type, character));
        }

        var mappings = new InventorySlotMappingRuleService();
        return new Side(
            containers,
            new MoveItemSpaceCommandHandler(
                new BusinessInventorySlotDomainService(containers, mappings),
                new ItemMovementDomainService(
                    new ItemPlacementRuleService(archive.Placements, archive.EquipmentSlots),
                    new ItemSlotRuleService(), new EitherEndMayBeHoldingItTestsWearEligibility())),
            character);
    }

    private (MoveItemSpaceHandler Handler, RecordingSender Sender, RecordingLogger Logger, Side Side)
        BuildHandler()
    {
        var side = this.Build();
        var sender = new RecordingSender();
        var logger = new RecordingLogger();
        return (
            new MoveItemSpaceHandler(
                new OneSession(), new NoPublishedRuntime(),
                new InventorySlotMappingRuleService(),
                new DirectMoveBus(side.Handler), sender, logger),
            sender,
            logger,
            side);
    }

    private sealed record Side(
        CharacterContainerDomainService Containers,
        MoveItemSpaceCommandHandler Handler,
        CharacterId Character)
    {
        public void Seed(InventoryContainerType type, short slotIndex, int itemId) =>
            ((SlotBook)this.Containers.GetContainer(this.Character, type)).Seed(slotIndex, itemId);

        public ValueTask<ItemMoveResult> MoveAsync(
            InventoryContainerType source,
            short sourceSlot,
            InventoryContainerType destination,
            short destinationSlot,
            int sourceInstanceValue = 0,
            int destinationInstanceValue = 0) =>
            this.Handler.HandleAsync(
                new CommandExecutionContext(
                    Guid.Empty,
                    typeof(MoveItemSpaceCommand),
                    typeof(Side),
                    CommandExecutionKind.Transactional),
                new MoveItemSpaceCommand(
                    this.Character,
                    ToBusinessSlot(source, sourceSlot),
                    1,
                    ToBusinessSlot(destination, destinationSlot),
                    sourceInstanceValue,
                    destinationInstanceValue),
                CancellationToken.None);

        private static BusinessSlot ToBusinessSlot(
            InventoryContainerType space, int index) =>
            new InventorySlotMappingRuleService().ToBusinessSlot(new GameSlot(space, index));

        public async ValueTask<ContainerSlotItem?> ReadAsync(
            InventoryContainerType type, short slotIndex) =>
            await ((SlotBook)this.Containers.GetContainer(this.Character, type))
                .ReadSlotAsync(slotIndex, CancellationToken.None);
    }

    /// <summary>
    /// A container that is a book of slots and refuses everything the served paths do not use.
    /// </summary>
    /// <remarks>
    /// What is under test is which end a move reads and what it does with the answer, so nothing
    /// here has to settle or persist. The unused members raise rather than answering emptily: a
    /// path that started using one of them fails here instead of being quietly told the container
    /// is empty, which is the shape of the defect these cases exist for.
    /// </remarks>
    private sealed class SlotBook(InventoryContainerType type, CharacterId character) : IItemContainer
    {
        private readonly Dictionary<int, ContainerSlotItem> bySlot = [];

        public ContainerId Id { get; } = ContainerId.ForCharacter(character, type);

        public InventoryContainerType Type => type;

        public ContainerShape Shape { get; } = new(type, 0);

        public bool IsRemovable => false;

        public void Seed(int slotIndex, int itemId) =>
            this.bySlot[slotIndex] = new ContainerSlotItem(
                slotIndex,
                StackableItemCore.Create(1, itemId, 1),
                ItemWorldClass.Equipment);

        public ValueTask<ContainerSlotItem?> ReadSlotAsync(
            int slotIndex, CancellationToken cancellationToken) =>
            ValueTask.FromResult(this.bySlot.GetValueOrDefault(slotIndex));

        public ValueTask<ContainerSlotPair> ReadSlotsAsync(
            int firstSlotIndex, int secondSlotIndex, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new ContainerSlotPair(
                this.bySlot.GetValueOrDefault(firstSlotIndex),
                this.bySlot.GetValueOrDefault(secondSlotIndex)));

        public ValueTask PutAsync(
            int slotIndex, InventoryItemCoreState core, CancellationToken cancellationToken)
        {
            this.bySlot[slotIndex] = new ContainerSlotItem(
                slotIndex, core, ItemWorldClass.Equipment);
            return ValueTask.CompletedTask;
        }

        public ValueTask ClearAsync(int slotIndex, CancellationToken cancellationToken)
        {
            this.bySlot.Remove(slotIndex);
            return ValueTask.CompletedTask;
        }

        public ValueTask<ItemMoveOutcome> MoveToAsync(
            int fromSlotIndex, int toSlotIndex, int count, CancellationToken cancellationToken)
        {
            var moving = this.bySlot.GetValueOrDefault(fromSlotIndex)
                ?? this.bySlot.GetValueOrDefault(toSlotIndex);
            return ValueTask.FromResult<ItemMoveOutcome>(moving is null
                ? new ItemMoveOutcome.Refused(ItemMoveRefusal.BothSlotsEmpty)
                : new ItemMoveOutcome.Moved(count, moving.ItemId));
        }

        public ValueTask<IReadOnlyList<InventoryItemState>> SnapshotAsync(
            CancellationToken cancellationToken) => throw Unused();

        public ValueTask<IReadOnlyList<ContainerSlotItem>> ReadAllAsync(
            CancellationToken cancellationToken) => throw Unused();

        public ValueTask<ItemPlacementOutcome> AddAsync(
            int itemId, int count, CancellationToken cancellationToken) => throw Unused();

        public ValueTask<ItemPlacementOutcome> RefillAsync(
            ItemRefill refill, CancellationToken cancellationToken) => throw Unused();

        private static NotSupportedException Unused() =>
            new("This container answers slot reads, writes and moves and nothing else.");
    }

    /// <summary>A bus that runs the move command on the handler that owns it.</summary>
    private sealed class DirectMoveBus(
        ICommandHandler<MoveItemSpaceCommand, ItemMoveResult> move) : ICommandBus
    {
        public async ValueTask<TResult> SendAsync<TResult>(
            ICommand<TResult> command, CancellationToken cancellationToken = default)
            where TResult : notnull
        {
            var context = new CommandExecutionContext(
                Guid.Empty, command.GetType(), typeof(DirectMoveBus),
                CommandExecutionKind.Transactional);
            var result = await move.HandleAsync(
                context, (MoveItemSpaceCommand)(object)command, cancellationToken);
            return (TResult)(object)result;
        }
    }

    private sealed class OneSession : ISelectedSessionDomainService
    {
        private static readonly SelectedSessionIdentity Session = SelectedSessionIdentity.Create(
            new AccountId(6), new CharacterId(Character), Connection, 1, 10011);

        public SelectedSessionIdentity? Select(SelectedSessionIdentity session) => null;

        public bool ReleaseExact(SelectedSessionIdentity session) => false;

        public SelectedSessionIdentity? Find(AccountId accountId) => Session;

        public SelectedSessionIdentity? FindByCharacter(CharacterId characterId) => Session;

        public SelectedSessionIdentity? FindByWireUserId(ushort wireUserId) => Session;

        public SelectedSessionIdentity? FindByConnection(NetworkConnectionId connectionId) => Session;
    }

    private sealed class RecordingSender : IGameMessageSender
    {
        public List<IGameServerMessage> Sent { get; } = [];

        public ValueTask SendCmdAsync(
            NetworkConnectionId connectionId,
            IGameCmdMessage message,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The move acknowledgement is sent explicitly.");

        public ValueTask SendNotiAsync(
            NetworkConnectionId connectionId,
            IGameNotiMessage message,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The move acknowledgement is a CMD.");

        public ValueTask SendExplicitAsync(
            NetworkConnectionId requiredDirectConnectionId,
            GameExplicitMessageDelivery delivery,
            CancellationToken cancellationToken)
        {
            this.Sent.AddRange(delivery.RequiredDirectMessages);
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>The log, kept as the sentences it would have written.</summary>
    private sealed class RecordingLogger : ILogger<MoveItemSpaceHandler>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            this.Entries.Add((logLevel, formatter(state, exception)));
    }

    /// <summary>A session directory that has nothing published, so no appearance frame is built.</summary>
    /// <remarks>
    /// These cases are about the acknowledgement and the log line. The frame that follows a move
    /// which changed the character's look is asserted where it belongs, and it is skipped here
    /// rather than mocked: a session that cannot be read is exactly the case the handler answers by
    /// sending the acknowledgement alone.
    /// </remarks>
    private sealed class NoPublishedRuntime : ILegacySessionSnapshotFixture
    {
        public void Publish(
            SelectedSessionIdentity session, SelectCharacterResult result, bool shouldPersistPosition) =>
            throw new NotSupportedException("Nothing publishes here.");

        public void ReplaceExact(SelectedSessionIdentity session, SelectCharacterResult result) =>
            throw new NotSupportedException("Nothing replaces here.");

        public SelectCharacterResult GetRequired(SelectedSessionIdentity session) =>
            throw new NotSupportedException("Nothing is published here.");

        public bool TryGet(
            SelectedSessionIdentity session,
            out SelectCharacterResult? result,
            out bool shouldPersistPosition)
        {
            result = null;
            shouldPersistPosition = false;
            return false;
        }

        public bool RemoveExact(SelectedSessionIdentity session) => false;
    }

    /// <summary>An inventory loader nobody reaches, because no session is published.</summary>
    private sealed class UnreachableInventory : ICharacterInventoryEntryDomainService
    {
        public ValueTask<CharacterInventoryEntryState> ReadCurrentAsync(
            AccountId accountId, CharacterId characterId, CancellationToken cancellationToken) => LoadAsync(accountId, characterId, cancellationToken);
        public ValueTask<CharacterInventoryEntryState> LoadAsync(
            AccountId accountId, CharacterId characterId, CancellationToken cancellationToken) =>
            throw new NotSupportedException(
                "The inventory is loaded only for a session that is published.");
    }

}

internal sealed class EitherEndMayBeHoldingItTestsWearEligibility : Dfo.Server.Abstractions.Inventory.IEquipmentWearDomainService
{
    public ValueTask<bool> CanWearAsync(Dfo.Server.Data.Characters.CharacterId characterId, int itemId, CancellationToken cancellationToken) => ValueTask.FromResult(true);
}
