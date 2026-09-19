using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Tests.Inventory;

/// <summary>
/// Where an item may sit in the main inventory: its own band, plus the quick slots.
/// </summary>
public sealed class ItemSlotRuleServiceTests
{
    private static readonly ItemSlotRuleService Rules = new();
    private static readonly InventorySlotMappingRuleService Mappings = new();

    /// <summary>A potion: kind 2, consumable band 65 to 120.</summary>
    private static readonly ItemPlacement Potion =
        new(2, 100, new(BusinessInventorySpace.Consumable, 0, 55));

    [Theory]
    [InlineData(65)]
    [InlineData(90)]
    [InlineData(120)]
    public void An_item_may_sit_anywhere_in_its_own_band(int slotIndex)
    {
        Assert.True(Rules.CanOccupySlot(BusinessMain(slotIndex), Potion));
    }

    [Theory]
    [InlineData(64)]
    [InlineData(121)]
    [InlineData(177)]
    public void An_item_may_not_sit_in_another_kinds_band(int slotIndex)
    {
        Assert.False(Rules.CanOccupySlot(BusinessMain(slotIndex), Potion));
    }

    /// <remarks>
    /// Slots 3 to 8, ahead of the equipment band that starts at 9. This is the range the client
    /// paints as the number-key bar, and it is why the tutorial's potion could be asked for at
    /// slot 3 when its own band starts at 65.
    /// </remarks>
    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(8)]
    public void The_quick_slots_take_an_item_from_outside_its_band(int slotIndex)
    {
        Assert.True(Rules.CanOccupySlot(BusinessMain(slotIndex), Potion));
    }

    [Theory]
    [InlineData(9)]
    public void The_quick_slots_end_where_the_measured_range_ends(int slotIndex)
    {
        Assert.False(Rules.CanOccupySlot(BusinessMain(slotIndex), Potion));
    }

    /// <remarks>
    /// Creatures and avatars live in other lists entirely. A placement that names a main-inventory
    /// band for them would still not earn them a place on the bar.
    /// </remarks>
    [Theory]
    [InlineData((byte)5)]
    [InlineData((byte)6)]
    [InlineData((byte)7)]
    [InlineData((byte)8)]
    [InlineData((byte)9)]
    public void The_quick_slots_refuse_the_kinds_that_belong_to_another_list(byte itemKind)
    {
        Assert.False(Rules.CanOccupySlot(
            new BusinessSlot(BusinessInventorySpace.QuickSlot, 0),
            new ItemPlacement(itemKind, 1,
                new(BusinessInventorySpace.AvatarEmblem, 0, 55))));
    }

    /// <remarks>
    /// The emblem band is this kind's own, so the exclusion is about the bar and not about the
    /// item being unplaceable.
    /// </remarks>
    [Fact]
    public void An_excluded_kind_still_reaches_its_own_band()
    {
        Assert.True(Rules.CanOccupySlot(
            BusinessMain(300), new ItemPlacement(9, 1,
                new(BusinessInventorySpace.AvatarEmblem, 0, 55))));
    }

    private static BusinessSlot BusinessMain(int index) =>
        Mappings.ToBusinessSlot(new GameSlot(InventoryContainerType.Main, index));
}
