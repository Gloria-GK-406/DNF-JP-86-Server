using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.World;
using Dfo.Server.Data.World;
using Dfo.Server.Infrastructure.Content;
using Dfo.Server.Testing;

namespace Dfo.Server.Infrastructure.Tests.Content;

/// <summary>
/// <c>[boss room entrance condition]</c> and the conditional monsters it places, read from the real
/// archive (#357, 2010 陷落的村庄 and 2016).
/// </summary>
public sealed class DungeonBossRoomEntranceContentTests
{
    [SkippableFact]
    public async Task Dungeon_2010_hunts_its_three_residents_and_summons_queen_dio()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var entrance = (await new PvfDungeonDefinitionSource(provider).GetAsync(2010, default))!.BossRoomEntrance;
        Assert.NotNull(entrance);
        Assert.True(entrance!.IsExecutable);
        Assert.True(entrance.SkipsBossRoomCheck);
        Assert.Equal((69264, 1), (entrance.SummonCode, entrance.SummonCount));
        Assert.Equal([56611, 56617, 56618], entrance.Hunts.Select(hunt => hunt.Code));
        Assert.All(entrance.Hunts, hunt => Assert.Equal((1, 1), (hunt.First, hunt.Second)));
    }

    [SkippableFact]
    public async Task Dungeon_2016_hunts_four_identical_targets_and_2005_keeps_its_time_action_unexecuted()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfDungeonDefinitionSource(provider);
        var station = (await source.GetAsync(2016, default))!.BossRoomEntrance!;
        Assert.Equal(56616, station.SummonCode);
        Assert.Equal(4, station.Hunts.Count);
        Assert.All(station.Hunts, hunt => Assert.Equal((61574, 1, 4), (hunt.Code, hunt.First, hunt.Second)));

        var gent = (await source.GetAsync(2005, default))!.BossRoomEntrance!;
        Assert.Equal("[time condition]", gent.UnsupportedAction);
        Assert.False(gent.IsExecutable);
    }

    [SkippableFact]
    public async Task A_room_map_declares_its_conditional_monsters()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var maps = new PvfDungeonMapDefinitionSource(provider);
        Assert.Equal([56611, 56617, 56618], (await maps.GetAsync(17110, default))!.ConditionalMonsterCodes.Order());
        Assert.Empty((await maps.GetAsync(17122, default))!.ConditionalMonsterCodes);
    }

    [SkippableFact]
    public async Task Dungeon_2010_places_each_resident_in_its_own_ordinary_room()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var rules = new DungeonStructureRuleService(new PvfDungeonDefinitionSource(provider),
            new PvfDungeonMapDefinitionSource(provider), TestRequired.NoQuests);
        var cells = new HashSet<MazeCell>();
        for (var seed = 0; seed < 24; seed++)
        {
            var layout = (await rules.ResolveAsync(2010,
                new DungeonEntryFacts(DungeonDifficulty.Adventure, [], [], DungeonEntryIntent.Interactive), seed, default))!;
            var plan = layout.EntrancePlan!;
            Assert.Equal(69264, plan.SummonCode);
            Assert.Equal([56611, 56617, 56618], plan.Targets.Select(target => target.Code));
            Assert.Equal(3, plan.Targets.Select(target => target.Cell).Distinct().Count());
            Assert.All(plan.Targets, target =>
            {
                Assert.NotEqual(layout.StartCell, target.Cell);
                Assert.NotEqual(layout.BossCell, target.Cell);
                Assert.Equal(target.MapId, Assert.Single(layout.Cells[target.Cell].MapCandidates));
            });
            cells.UnionWith(plan.Targets.Select(target => target.Cell));
        }
        Assert.True(cells.Count > 3, "Placement never varied across seeds.");
    }

    [SkippableFact]
    public async Task Dungeon_2016_fills_the_four_rooms_its_fixed_minimap_grid_marks()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var rules = new DungeonStructureRuleService(new PvfDungeonDefinitionSource(provider),
            new PvfDungeonMapDefinitionSource(provider), TestRequired.NoQuests);
        var layout = (await rules.ResolveAsync(2016,
            new DungeonEntryFacts(DungeonDifficulty.Normal, [], [], DungeonEntryIntent.Interactive), 3, default))!;
        Assert.Equal(new HashSet<MazeCell> { new(1, 0), new(0, 1), new(3, 1), new(1, 3) },
            layout.EntrancePlan!.Targets.Select(target => target.Cell).ToHashSet());
    }
}
