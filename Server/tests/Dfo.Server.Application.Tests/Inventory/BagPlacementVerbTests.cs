using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Characters;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.Inventory;

/// <summary>Business grants merge piles, prefer quick slots, and report refusals without writing.</summary>
public sealed class BagPlacementVerbTests
{
    private const int Potion = 8474;

    private const int Unknown = 12345;

    [Fact]
    public async Task Adding_joins_a_pile_that_has_room_and_says_how_much_it_took()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var bag = new BagInventory { [65] = (Potion, 3) };

            var outcome = await bag.InUseCase(container => container.AddAsync(Potion, 4, default));

            var joined = Assert.IsType<ItemPlacementOutcome.AddedToPile>(outcome.Placement);
            Assert.Equal(0, joined.SlotIndex);
            Assert.Equal(BusinessInventorySpace.Consumable, outcome.Container.Space);
            Assert.Equal(4, joined.Added);
            Assert.Equal(7, joined.Total);
        });
    }

    /// <summary>
    /// A consumable with no pile opens a quick slot, not the first slot of its own band.
    /// </summary>
    /// <remarks>
    /// The tutorial is the reason this matters: it hands over a potion and then tells the player to
    /// press its key. A potion that landed in the bag leaves the bar empty and that step
    /// unfinishable, which is what a live A21 session showed on 2026-08-26 -- our acknowledgement
    /// naming slot 65 where the reference server's named slot 3.
    /// </remarks>
    [Fact]
    public async Task Adding_a_consumable_with_no_pile_opens_the_quick_bar_first()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var bag = new BagInventory();

            var outcome = await bag.InUseCase(container => container.AddAsync(Potion, 4, default));

            var opened = Assert.IsType<ItemPlacementOutcome.PutInEmptySlot>(outcome.Placement);
            Assert.Equal(0, opened.SlotIndex);
            Assert.Equal(BusinessInventorySpace.QuickSlot, outcome.Container.Space);
            Assert.Equal(4, opened.Count);
        });
    }

    /// <summary>With the bar full, the same offer lands in the item's own band.</summary>
    /// <remarks>
    /// The bar is a preference, not a home. Without this the rule above would be indistinguishable
    /// from one that puts every consumable on the bar and drops the rest on the floor.
    /// </remarks>
    [Fact]
    public async Task Adding_a_consumable_falls_back_to_its_own_band_when_the_bar_is_full()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var bag = FullBar();

            var outcome = await bag.InUseCase(container => container.AddAsync(Potion, 4, default));

            var opened = Assert.IsType<ItemPlacementOutcome.PutInEmptySlot>(outcome.Placement);
            Assert.Equal(0, opened.SlotIndex);
            Assert.Equal(BusinessInventorySpace.Consumable, outcome.Container.Space);
            Assert.Equal(4, opened.Count);
        });
    }

    /// <remarks>
    /// Every slot the item is allowed has to be taken before this can be asserted, and since
    /// 2026-08-26 a consumable is allowed the quick bar as well as its band. Seeding only the band
    /// would leave six slots free and this would be asserting nothing.
    /// </remarks>
    [Fact]
    public async Task Adding_answers_that_there_is_no_room_rather_than_spilling_out_of_the_band()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var bag = FullBar();
            bag[65] = (Potion, 10);
            bag[66] = (Potion, 10);

            var outcome = await bag.InUseCase(container => container.AddAsync(Potion, 1, default));

            Assert.IsType<ItemPlacementOutcome.NoRoom>(outcome.Placement);
            Assert.Equal(
                [(3, Unknown, 1), (4, Unknown, 1), (5, Unknown, 1), (6, Unknown, 1), (7, Unknown, 1),
                 (8, Unknown, 1), (65, Potion, 10), (66, Potion, 10)],
                bag.Rows());
        });
    }

    /// <summary>A bag whose six quick slots are all taken by something that is not the potion.</summary>
    private static BagInventory FullBar()
    {
        var bag = new BagInventory();
        for (var slot = 3; slot <= 8; slot++)
            bag[slot] = (Unknown, 1);
        return bag;
    }

    [Fact]
    public async Task Adding_something_the_archive_declares_nothing_for_writes_nothing()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var bag = new BagInventory();

            var outcome = await bag.InUseCase(container => container.AddAsync(Unknown, 1, default));

            Assert.IsType<ItemPlacementOutcome.UnknownItem>(outcome.Placement);
            Assert.Empty(bag.Rows());
        });
    }

    private const byte ItemKind = 2;

    /// <summary>A band of 65 to 120, piles of ten, so a full pile costs one seeded slot.</summary>
    private sealed class TenToAPile : IItemPlacementDefinitionSource
    {
        public ValueTask<ItemPlacement?> GetAsync(int itemId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(
                itemId == Potion ? new ItemPlacement(ItemKind, 10,
                    new(BusinessInventorySpace.Consumable, 0, 1)) : null);
    }

    private sealed class NoWearables : IEquipmentSlotDefinitionSource
    {
        public ValueTask<BusinessSlot?> GetAsync(int itemId, CancellationToken ct) =>
            ValueTask.FromResult<BusinessSlot?>(null);
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
            Func<IBusinessItemContainer, ValueTask<TResult>> work)
        {
            var physical = this.Containers(new TenToAPile())
                .GetContainer(new CharacterId(60), InventoryContainerType.Main);
            var container = new BusinessItemContainer(BusinessInventorySpace.Consumable, physical,
                new ItemPlacementRuleService(new TenToAPile(), new NoWearables()),
                new ItemArrivalRuleService(), lifecycle: this.LifecycleFor(new TenToAPile()));
            await using var boundary = this.Scope.Begin();
            var result = await work(container);
            await boundary.CommitAsync(CancellationToken.None);
            return result;
        }
    }
}
