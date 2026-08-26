using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Characters;
using Dfo.Server.Protocol.Game.TaggedIdReport;

namespace Dfo.Server.Application.Tests.Inventory;

/// <summary>
/// Where a tutorial step's items land, and which of them the acknowledgement names.
/// </summary>
/// <remarks>
/// Written against the behaviour as it stands, before the placement moves into the bag. Two of the
/// things pinned here were carried by comments alone: an item whose band is full is left out of the
/// acknowledgement rather than reported as placed, and the second item of one step sees what the
/// first already wrote. Both would survive a careless migration only by luck, because nothing else
/// asserts them.
/// </remarks>
public sealed class TutorialRewardPlacementTests
{
    private const int Potion = 8474;

    private const int Scroll = 9001;

    private const int Unknown = 12345;

    private static readonly CommandExecutionContext Context = new(
        Guid.Empty,
        typeof(GrantTutorialRewardCommand),
        typeof(GrantTutorialRewardCommandHandler),
        CommandExecutionKind.Transactional);

    [Fact]
    public async Task A_grant_joins_a_pile_the_character_already_has()
    {
        var inventory = new RewardInventory { [65] = (Potion, 3) };

        var result = await Grant(inventory, (Potion, 2));

        var placed = Assert.Single(result.Placed);
        Assert.Equal(new PlacedRewardItem(65, Potion, 2), placed);
        Assert.Equal([(65, Potion, 5)], inventory.Rows());
    }

    [Fact]
    public async Task A_grant_with_no_pile_opens_the_first_quick_slot()
    {
        var inventory = new RewardInventory();

        var result = await Grant(inventory, (Potion, 2));

        // The bar before the band, which is where the tutorial's own potion has to arrive: the
        // step that follows tells the player to press its key. See BagPlacementVerbTests.
        Assert.Equal(new PlacedRewardItem(3, Potion, 2), Assert.Single(result.Placed));
        Assert.Equal([(3, Potion, 2)], inventory.Rows());
    }

    /// <remarks>
    /// The pile is filled to the stack limit and the rest is dropped, not spilled into a second
    /// slot: the arrival answers with one landing per call and the handler reports what it says.
    /// Pinned because it is the kind of thing a rewrite would "fix" without anyone asking for it.
    /// </remarks>
    [Fact]
    public async Task A_grant_larger_than_the_pile_can_take_places_only_what_fits()
    {
        var inventory = new RewardInventory { [65] = (Potion, 8) };

        var result = await Grant(inventory, (Potion, 5));

        Assert.Equal(new PlacedRewardItem(65, Potion, 2), Assert.Single(result.Placed));
        Assert.Equal([(65, Potion, 10)], inventory.Rows());
    }

    /// <remarks>
    /// Naming it in the acknowledgement would tell the client to draw something the server does not
    /// hold. The other item of the same step is unaffected.
    /// </remarks>
    [Fact]
    public async Task An_item_whose_band_is_full_is_left_out_and_the_rest_of_the_step_proceeds()
    {
        // Both of the potion's homes are taken: its own band by the scroll, and the quick bar by
        // six of something else. Seeding only the band would leave the bar free, and the potion
        // this is meant to leave out would quietly land there instead.
        var inventory = new RewardInventory { [65] = (Scroll, 1) };
        for (var slot = 3; slot <= 8; slot++)
            inventory[slot] = (Unknown, 1);

        var result = await Grant(inventory, (Potion, 1), (Scroll, 1));

        Assert.Equal(new PlacedRewardItem(65, Scroll, 1), Assert.Single(result.Placed));
        Assert.Equal(
            [(3, Unknown, 1), (4, Unknown, 1), (5, Unknown, 1), (6, Unknown, 1), (7, Unknown, 1),
             (8, Unknown, 1), (65, Scroll, 2)],
            inventory.Rows());
    }

    [Fact]
    public async Task An_item_the_archive_declares_nothing_for_is_left_out_and_the_rest_proceeds()
    {
        var inventory = new RewardInventory();

        var result = await Grant(inventory, (Unknown, 1), (Scroll, 1));

        Assert.Equal(new PlacedRewardItem(3, Scroll, 1), Assert.Single(result.Placed));
        Assert.Equal([(3, Scroll, 1)], inventory.Rows());
    }

    /// <remarks>
    /// The step is one use case, and the container's reads inside a use case include what that use
    /// case has already written -- so the second declaration finds the pile the first one opened
    /// instead of opening a second beside it.
    /// </remarks>
    [Fact]
    public async Task The_second_of_two_declarations_sees_what_the_first_wrote()
    {
        var inventory = new RewardInventory();

        var result = await Grant(inventory, (Potion, 2), (Potion, 3));

        Assert.Equal(
            [new PlacedRewardItem(3, Potion, 2), new PlacedRewardItem(3, Potion, 3)],
            result.Placed);
        Assert.Equal([(3, Potion, 5)], inventory.Rows());
    }

    [Fact]
    public async Task A_character_with_no_bound_container_receives_nothing()
    {
        var handler = new GrantTutorialRewardCommandHandler(
            new CharacterContainerDomainService());

        var result = await handler.HandleAsync(
            Context,
            new GrantTutorialRewardCommand(new CharacterId(60), [new DeclaredRewardItem(Potion, 1)]),
            CancellationToken.None);

        Assert.Empty(result.Placed);
    }

    private static async Task<TutorialRewardResult> Grant(
        RewardInventory inventory, params (int ItemId, int Count)[] declared)
    {
        var handler = new GrantTutorialRewardCommandHandler(
            inventory.Containers(new RewardPlacements()));
        var bus = new DirectCommandBus(reward: handler, scope: inventory.Scope);
        return await bus.SendAsync(
            new GrantTutorialRewardCommand(
                new CharacterId(60),
                [.. declared.Select(item => new DeclaredRewardItem(item.ItemId, item.Count))]),
            CancellationToken.None);
    }

    /// <summary>
    /// A potion band of one slot with room for ten, and a scroll band of one slot with room for
    /// twenty, so "the band is full" can be constructed without filling a hundred slots.
    /// </summary>
    private sealed class RewardPlacements : IItemPlacementDefinitionSource
    {
        public ValueTask<ItemPlacement?> GetAsync(int itemId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(itemId switch
            {
                Potion => new ItemPlacement(2, 10, 65, 65),
                Scroll => new ItemPlacement(2, 20, 65, 65),
                _ => null,
            });
    }

    private sealed class RewardInventory : FakeMainInventory
    {
        public (int ItemId, int Count) this[int slotIndex]
        {
            set => this.Seed(slotIndex, value.ItemId, value.Count);
        }
    }
}
