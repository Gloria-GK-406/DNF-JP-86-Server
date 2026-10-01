using Dfo.Pvf.Tests.Support;
using Dfo.Server.Infrastructure.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

public sealed class PvfEquipmentOutworldContentSourceTests
{
    [SkippableFact]
    public async Task A21_amplify_table_defines_real_materials_attributes_and_admission()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfEquipmentOutworldContentSource(provider);
        var table = await source.GetAsync(default);
        Assert.NotNull(table);
        Assert.Equal(55, table.MinimumEquipmentLevel);
        Assert.Equal(1, table.PurifyMaterials[1183]);
        Assert.Equal(1, table.ClearMaterials[10000408]);
        Assert.Equal((byte)3, table.InvestMaterials[1184].Attribute);
        Assert.Equal((byte)3, table.TwistMaterials[1188].Attribute);
        Assert.Equal((byte)0, table.TwistMaterials[1284].Attribute);
        Assert.Equal(5, table.InitialValues[2][1]);
        Assert.Equal(5, table.InitialValues[2][4]);
        Assert.Equal(4, table.RollWeights.Count);
        var equipment = await new PvfEquipmentDefinitionSource(provider)
            .GetEquipmentAsync(101010339, default);
        Assert.NotNull(equipment);
        Assert.True(equipment.MinimumLevel >= table.MinimumEquipmentLevel);
        Assert.True(equipment.Rarity >= 2);
    }
}
