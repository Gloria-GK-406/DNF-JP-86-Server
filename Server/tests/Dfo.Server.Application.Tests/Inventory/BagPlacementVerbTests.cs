using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Tests.Inventory;

/// <summary>
/// What the bag itself answers when something is offered to it.
/// </summary>
/// <remarks>
/// The callers each cover their own reading of these answers. What is here is the bag's side: the
/// four things it can say, and the one comparison that shows why putting an item away and topping a
/// character up are two verbs rather than one spelling of the same verb.
/// </remarks>
public sealed class BagPlacementVerbTests
{
    private const int Potion = 8474;

    private const int Unknown = 12345;

    [Fact]
    public async Task Adding_joins_a_pile_that_has_room_and_says_how_much_it_took()
    {
        var bag = new BagInventory { [65] = (Potion, 3) };

        var outcome = await bag.InUseCase(container => container.AddAsync(Potion, 4, default));

        var joined = Assert.IsType<ItemPlacementOutcome.AddedToPile>(outcome);
        Assert.Equal(65, joined.SlotIndex);
        Assert.Equal(4, joined.Added);
        Assert.Equal(7, joined.Total);
    }

    [Fact]
    public async Task Adding_with_no_pile_opens_the_first_free_slot_of_the_band()
    {
        var bag = new BagInventory();

        var outcome = await bag.InUseCase(container => container.AddAsync(Potion, 4, default));

        var opened = Assert.IsType<ItemPlacementOutcome.PutInEmptySlot>(outcome);
        Assert.Equal(65, opened.SlotIndex);
        Assert.Equal(4, opened.Count);
    }

    [Fact]
    public async Task Adding_answers_that_there_is_no_room_rather_than_spilling_out_of_the_band()
    {
        var bag = new BagInventory { [65] = (Potion, 10), [66] = (Potion, 10) };

        var outcome = await bag.InUseCase(container => container.AddAsync(Potion, 1, default));

        Assert.IsType<ItemPlacementOutcome.NoRoom>(outcome);
        Assert.Equal([(65, Potion, 10), (66, Potion, 10)], bag.Rows());
    }

    [Fact]
    public async Task Adding_something_the_archive_declares_nothing_for_writes_nothing()
    {
        var bag = new BagInventory();

        var outcome = await bag.InUseCase(container => container.AddAsync(Unknown, 1, default));

        Assert.IsType<ItemPlacementOutcome.UnknownItem>(outcome);
        Assert.Empty(bag.Rows());
    }

    /// <summary>
    /// The same held state, offered the same quantity, lands in two different places.
    /// </summary>
    /// <remarks>
    /// This is why the two verbs exist. A grant will not push a pile past the item's stack limit
    /// and opens a slot beside it instead; a refill joins the pile whatever its size, because the
    /// quantity a refill carries was already bounded by the rule that produced it. Folding them
    /// into one verb would have to pick one of these two answers and be wrong about the other.
    /// </remarks>
    [Fact]
    public async Task Adding_and_refilling_disagree_about_a_pile_that_is_already_full()
    {
        var granted = new BagInventory { [65] = (Potion, 10) };
        var refilled = new BagInventory { [65] = (Potion, 10) };

        var grant = await granted.InUseCase(container => container.AddAsync(Potion, 5, default));
        var refill = await refilled.InUseCase(container => container.RefillAsync(
            new ItemRefill(Potion, ItemKind, 5, 65, 120), default));

        var opened = Assert.IsType<ItemPlacementOutcome.PutInEmptySlot>(grant);
        Assert.Equal(66, opened.SlotIndex);
        Assert.Equal([(65, Potion, 10), (66, Potion, 5)], granted.Rows());

        var joined = Assert.IsType<ItemPlacementOutcome.AddedToPile>(refill);
        Assert.Equal(65, joined.SlotIndex);
        Assert.Equal(15, joined.Total);
        Assert.Equal([(65, Potion, 15)], refilled.Rows());
    }

    private const byte ItemKind = 2;

    /// <summary>A band of 65 to 120, piles of ten, so a full pile costs one seeded slot.</summary>
    private sealed class TenToAPile : IItemPlacementDefinitionSource
    {
        public ValueTask<ItemPlacement?> GetAsync(int itemId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(
                itemId == Potion ? new ItemPlacement(ItemKind, 10, 65, 66) : null);
    }

    private sealed class BagInventory : FakeMainInventory
    {
        public (int ItemId, int Count) this[int slotIndex]
        {
            set => this.Seed(slotIndex, value.ItemId, value.Count);
        }

        /// <summary>
        /// Runs one call against the bag inside a use case, which is the only way it accepts a
        /// change.
        /// </summary>
        public async Task<TResult> InUseCase<TResult>(
            Func<IItemContainer, ValueTask<TResult>> work)
        {
            var container = this.Containers(new TenToAPile())
                .GetContainer(new CharacterId(60), InventoryContainerType.Main);
            await using var boundary = this.Scope.Begin();
            var result = await work(container);
            await boundary.CommitAsync(CancellationToken.None);
            return result;
        }
    }
}
