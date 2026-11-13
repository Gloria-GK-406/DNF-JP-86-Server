using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class ItemTransferArrivalTests
{
    private static readonly ItemArrivalRuleService Rules = new();
    private static BusinessSlot Cargo(int index) => new(BusinessInventorySpace.PersonalCargo, index);

    [Fact]
    public void An_unlimited_pile_joins_the_lowest_existing_pile_whatever_slot_is_nominated()
    {
        var result = Rules.ForTransfer([new(Cargo(9), 53), new(Cargo(4), 6)], 14, 0, Cargo(20),
            new HashSet<BusinessSlot> { Cargo(4), Cargo(9) }, 24);
        var step = Assert.IsType<ItemArrival.OntoExistingPile>(Assert.Single(Assert.IsType<ItemTransferArrival.Placed>(result).Steps));
        Assert.Equal(new ItemArrival.OntoExistingPile(Cargo(4), 14, 20), step);
    }

    [Fact]
    public void What_does_not_fit_opens_the_nominated_slot_or_the_first_free_one_when_it_is_taken()
    {
        var occupied = new HashSet<BusinessSlot> { Cargo(0), Cargo(2) };
        Assert.Equal([new ItemArrival.OntoExistingPile(Cargo(2), 3, 10), new ItemArrival.IntoEmptySlot(Cargo(5), 4)],
            Assert.IsType<ItemTransferArrival.Placed>(Rules.ForTransfer([new(Cargo(2), 7)], 7, 10, Cargo(5), occupied, 8)).Steps);
        Assert.Equal([new ItemArrival.OntoExistingPile(Cargo(2), 3, 10), new ItemArrival.IntoEmptySlot(Cargo(1), 4)],
            Assert.IsType<ItemTransferArrival.Placed>(Rules.ForTransfer([new(Cargo(2), 7)], 7, 10, Cargo(0), occupied, 8)).Steps);
    }

    [Fact]
    public void A_stack_limit_of_one_joins_nothing_and_a_full_list_or_oversized_rest_is_refused()
    {
        var occupied = new HashSet<BusinessSlot> { Cargo(0) };
        Assert.Equal([new ItemArrival.IntoEmptySlot(Cargo(1), 1)],
            Assert.IsType<ItemTransferArrival.Placed>(Rules.ForTransfer([new(Cargo(0), 1)], 1, 1, Cargo(0), occupied, 2)).Steps);
        Assert.IsType<ItemTransferArrival.NoRoom>(Rules.ForTransfer([new(Cargo(0), 1)], 1, 1, Cargo(0), occupied, 1));
        Assert.IsType<ItemTransferArrival.ExceedsStackLimit>(Rules.ForTransfer([new(Cargo(0), 8)], 15, 10, Cargo(1), occupied, 4));
        // A pile beyond the open slots is a closed legacy slot and is not joined.
        Assert.IsType<ItemArrival.IntoEmptySlot>(Assert.Single(Assert.IsType<ItemTransferArrival.Placed>(
            Rules.ForTransfer([new(Cargo(20), 2)], 1, 0, Cargo(1), new HashSet<BusinessSlot> { Cargo(20) }, 8)).Steps));
    }
}
