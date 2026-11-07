using Dfo.Pvf.Tests.Support;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Infrastructure.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

public sealed class PvfEquipmentGrowthDefinitionSourceTests
{
    [SkippableFact]
    public async Task A21_growth_group_one_has_seven_stars_and_kill_acquisition()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var group = await new PvfEquipmentGrowthDefinitionSource(provider).GetGroupAsync(1, default);
        Assert.NotNull(group);
        Assert.Equal(7, group.MaxLevel);
        var death = Assert.Single(group.Acquisitions);
        Assert.Equal("death", death.Event);
        Assert.Equal(5, death.Point);
        Assert.Equal(new Dictionary<int, int> { [1] = 100, [2] = 100, [3] = 300, [4] = 140 }, death.KindRates);
        Assert.Equal([3, 3, 10, 5, 90, 10], death.LevelCorrection);
        Assert.Equal([100, 100, 100, 100, 100], death.DifficultyCorrection);
        Assert.Equal([1794, 3174, 4830, 6141, 8694, 11868, 0], group.Stars.Select(x => x.MaxExperience));
        Assert.Equal([true, true, false, false, false, false, false], group.Stars.Select(x => x.GrowsByExperience));
        Assert.All(group.Stars.Take(6), star => Assert.Equal(new EquipmentGrowthPenalty("fatigue", 30, 20), Assert.Single(star.Penalties)));
        Assert.Empty(group.Stars[6].Penalties);
        Assert.Null(await new PvfEquipmentGrowthDefinitionSource(provider).GetGroupAsync(2, default));
    }

    [SkippableFact]
    public async Task A21_dragon_leap_short_sword_names_its_star_and_next_template()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfEquipmentEmancipateDefinitionSource(provider);
        var third = Assert.Single(await source.GetEquipmentAsync(101000044, default));
        Assert.Equal(new EquipmentGrowthPosition(1, 3), third.Growth);
        Assert.Equal([new EmancipateItemCount(10000224, 1)], third.Inputs);
        Assert.Equal([new EmancipateItemCount(101000045, 1)], third.Outputs);
        var last = Assert.Single(await source.GetEquipmentAsync(101000048, default));
        Assert.Equal(new EquipmentGrowthPosition(1, 7), last.Growth);
        Assert.Empty(await source.GetEquipmentAsync(101010339, default));
        Assert.Empty(await source.GetEquipmentAsync(-1, default));
    }

    [SkippableFact]
    public async Task Every_A21_emancipate_block_reads_and_every_growth_line_is_a_closed_chain()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfEquipmentEmancipateDefinitionSource(provider);
        var ids = PvfListFile.Paths(provider.Archive, "equipment/equipment.lst", prefix: "equipment/").Keys;
        var growth = new Dictionary<int, EquipmentEmancipateDefinition>();
        foreach (var id in ids)
            foreach (var definition in await source.GetEquipmentAsync(id, default))
                if (definition.Growth is not null) growth.Add(id, definition);
        // 172 named lines of seven stars; the six unnamed placeholder lines are not in equipment.lst.
        Assert.Equal(172 * 7, growth.Count);
        foreach (var (id, definition) in growth)
        {
            var star = definition.Growth!.Value.Star;
            if (star == 7) continue;
            var next = Assert.Single(definition.Outputs);
            Assert.Equal(star + 1, growth[next.ItemId].Growth!.Value.Star);
            Assert.Equal(definition.Growth.Value.Group, growth[next.ItemId].Growth!.Value.Group);
        }
    }
}
