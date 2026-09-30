using Dfo.Pvf.Tests.Support;
using Dfo.Server.Infrastructure.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

public sealed class PvfEquipmentForgingContentSourceTests
{
    [SkippableFact]
    public async Task A21_separate_upgrade_table_defines_weapon_material_and_probability()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var table = await new PvfEquipmentForgingContentSource(provider).GetAsync(default);
        Assert.NotNull(table);
        Assert.Equal(8, table.MaxLevel);
        Assert.Equal(10000, table.Levels[0].SuccessWeight);
        Assert.Equal(5000, table.Levels[1].SuccessWeight);
        Assert.Equal(1.11, table.Levels[1].MaterialWeight, 2);
        Assert.Equal(3326, table.MaterialsByGrade[77].ItemId);
        Assert.Equal(39, table.MaterialsByGrade[77].BaseCount);
        Assert.Equal(1, table.ItemWeightsByRarity[2]);
        var weapon = await new PvfEquipmentReinforcementDefinitionSource(provider)
            .GetEquipmentAsync(101010339, default);
        Assert.NotNull(weapon);
        Assert.Equal(0, weapon.EquipmentTypeWeightIndex);
        Assert.Equal(77, weapon.Grade);
        Assert.Equal(2, weapon.Rarity);
        Assert.Equal(45, weapon.MaxDurability);
    }
}
