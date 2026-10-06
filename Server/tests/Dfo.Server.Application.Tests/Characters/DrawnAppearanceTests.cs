using Dfo.Server.Application.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Tests.Characters;

public sealed class DrawnAppearanceTests
{
    [Fact]
    public void Worn_items_supply_display_state_and_upgrade_flag_in_slot_order()
    {
        var first = StackableItemCore.Create(1, 305419896, 1) with
        {
            Attribute = 0x35, AmplifyType = 2, EnchantUpgradeCount = 200
        };
        var second = StackableItemCore.Create(1, 1000003, 1) with
        {
            Attribute = 7, EnchantUpgradeCount = 99
        };

        var parts = DrawnAppearance.Of([new(null, 23, second), new(null, 7, first)]);

        Assert.Equal([7, 23], parts.Select(part => (int)part.Slot));
        Assert.Equal([305419896, 1000003], parts.Select(part => part.DisplayItemId));
        Assert.Equal([43, 14], parts.Select(part => (int)part.State));
        Assert.Equal([200, 99], parts.Select(part => (int)part.Flag20));
        Assert.All(parts, part =>
        {
            Assert.Equal(4, part.ExpansionLength);
            Assert.Equal(new byte[4], part.ExpansionData);
            Assert.Equal(0, part.LinkItemId);
            Assert.Equal(0u, part.EnchantValue);
        });
    }

    [Fact]
    public void Appearance_excludes_other_worn_slots_and_handles_an_empty_equipment_list()
    {
        var core = StackableItemCore.Create(1, 1000003, 1);
        var parts = DrawnAppearance.Of([new(null, 12, core), new(null, 13, core),
            new(null, 23, core), new(null, 24, core), new(null, 25, core)]);
        Assert.Equal([12, 23], parts.Select(part => (int)part.Slot));
        Assert.Empty(DrawnAppearance.Of([]));
    }
}
