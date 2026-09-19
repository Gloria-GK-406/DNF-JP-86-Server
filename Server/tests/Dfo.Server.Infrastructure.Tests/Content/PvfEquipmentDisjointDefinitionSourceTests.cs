using Dfo.Server.Abstractions.Content;
using Dfo.Server.Infrastructure.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

public sealed class PvfEquipmentDisjointDefinitionSourceTests
{
    [SkippableFact]
    public async Task A21_system_disjoint_table_and_equipment_facts_are_read_from_the_archive()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfEquipmentDisjointDefinitionSource(provider);

        var table = await source.GetSystemTableAsync(default);
        Assert.NotNull(table);
        Assert.Equal((3037, 150d), (table.CubeItemId, table.CubeCreationBase));
        var expectedMultipliers = new[] { 1.12, 1.35, 0.07, 1.48, 1.8, 0.7, 1.53 };
        Assert.Equal(expectedMultipliers.Length, table.CubeCreationMultipliers.Count);
        for (var index = 0; index < expectedMultipliers.Length; index++)
            Assert.Equal(expectedMultipliers[index], table.CubeCreationMultipliers[index], 5);
        Assert.Equal(new[] { 3033, 3034, 3035, 3036 }, table.AdditionalResults[1]);
        Assert.Equal(10100115, table.ExpandRules[0].ItemId);
        Assert.False(table.ExpandRules[4].Enabled);
        Assert.Equal(10099775, table.ExpandRules[5].ItemId);
        var placements = new PvfItemPlacementDefinitionSource(provider);
        var materialIds = table.AdditionalResults.SelectMany(row => row)
            .Append(table.CubeItemId)
            .Concat(table.ExpandRules.Where(rule => rule.Enabled).Select(rule => rule.ItemId))
            .Distinct();
        foreach (var itemId in materialIds)
        {
            var placement = await placements.GetAsync(itemId, default);
            Assert.NotNull(placement);
            Assert.Equal(ItemWorldClass.Interchangeable, placement.WorldClass);
        }

        var sword = await source.GetEquipmentAsync(101010437, default);
        Assert.NotNull(sword);
        Assert.True(sword.CanDisjoint);
        Assert.True(sword.SellGold > 0);
        Assert.True(sword.MinimumLevel > 0);
        Assert.InRange(sword.Rarity, 0, 6);
        var beginner = Assert.Single(await new PvfInitialEquipmentDefinitionSource(provider)
            .GetAsync(0, default), grant => grant.SlotIndex == PvfEquipmentSlots.ByName["[weapon]"]);
        Assert.NotNull(await source.GetEquipmentAsync(beginner.ItemId, default));
        Assert.Null(await source.GetEquipmentAsync(int.MaxValue, default));
    }
}
