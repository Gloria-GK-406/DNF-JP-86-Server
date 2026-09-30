using Dfo.Pvf.Tests.Support;
using Dfo.Server.Infrastructure.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

public sealed class PvfEquipmentMagicSealContentSourceTests
{
    [SkippableFact]
    public async Task A21_sealed_katana_has_real_option_pool_costs_and_low_level_scroll()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfEquipmentMagicSealContentSource(provider);

        var equipment = await source.GetEquipmentAsync(101010339, default);
        Assert.NotNull(equipment);
        Assert.Equal(75, equipment.MinimumLevel);
        Assert.Equal(2, equipment.Rarity);
        Assert.Equal(1720, equipment.UnsealGoldCost);
        Assert.Equal(3800, equipment.ChangeGoldCost);
        Assert.Contains(equipment.InitialGroups[0], candidate =>
            candidate.Option == new Dfo.Server.Data.Inventory.RandomOptionState(15, 13, 98));
        Assert.NotEmpty(equipment.Quantities);
        Assert.Null(await source.GetEquipmentAsync(101010437, default));

        var scroll = await source.GetScrollAsync(2600460, default);
        Assert.NotNull(scroll);
        Assert.Equal(30, scroll.MaximumEquipmentLevel);
        Assert.Null(await source.GetScrollAsync(2600294, default));
    }
}
