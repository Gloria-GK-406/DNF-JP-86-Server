using Dfo.Pvf;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Infrastructure.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

/// <summary>Maze <c>[clear condition]</c> blocks and their companion flags, read from the real archive (#356).</summary>
public sealed class DungeonClearConditionContentTests
{
    [SkippableFact]
    public async Task The_1842_story_maze_of_dungeon_160_clears_on_destroying_object_10601()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var dungeon = await new PvfDungeonDefinitionSource(provider).GetAsync(160, default);
        var maze = Assert.Single(dungeon!.Mazes, maze => maze.QuestConnection?.QuestId == 1842);
        var condition = Assert.Single(maze.ClearConditions);
        Assert.Equal((DungeonClearConditionKind.DestroyObject, 10601, 1),
            (condition.Kind, Assert.Single(condition.Targets), condition.Count));
        Assert.False(dungeon.IgnoresDefaultClear);
        Assert.Empty(dungeon.Mazes.Single(maze => maze.Index == 0).ClearConditions);
    }

    [SkippableFact]
    public async Task Special_dungeons_read_list_maps_hunted_bosses_and_the_ignore_flag()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfDungeonDefinitionSource(provider);

        var infiltrate = (await source.GetAsync(2005, default))!;
        var clearMap = Assert.Single(infiltrate.Mazes[0].ClearConditions);
        Assert.Equal(DungeonClearConditionKind.ClearMap, clearMap.Kind);
        Assert.Equal([17068, 17069], clearMap.Targets);
        Assert.Equal(1, clearMap.Count);

        var escape = (await source.GetAsync(2008, default))!;
        Assert.True(escape.IgnoresDefaultClear);
        var hunt = Assert.Single(escape.Mazes[0].ClearConditions);
        Assert.Equal((DungeonClearConditionKind.HuntBoss, 69257, 1), (hunt.Kind, Assert.Single(hunt.Targets), hunt.Count));

        var test = (await source.GetAsync(4000, default))!;
        var limit = Assert.Single(test.Mazes[0].ClearConditions);
        Assert.Equal(DungeonClearConditionKind.LimitTime, limit.Kind);
        Assert.False(limit.IsCounted);
    }

    /// <remarks>
    /// Territory combat is out of scope (#356 user ruling): its team blocks are skipped whole, so
    /// they neither fail the dungeon nor leak their hunt boss entries into the maze's conditions.
    /// </remarks>
    [SkippableTheory]
    [InlineData(7054)]
    [InlineData(7055)]
    [InlineData(7056)]
    public async Task Territory_combat_team_conditions_are_skipped(int dungeonId)
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var dungeon = await new PvfDungeonDefinitionSource(provider).GetAsync(dungeonId, default);
        if (dungeon is null) return; // A territory map without a maze is not this reader's concern.
        Assert.True(dungeon.IgnoresDefaultClear);
        Assert.All(dungeon.Mazes, maze => Assert.Empty(maze.ClearConditions));
    }

    [SkippableFact]
    public async Task The_whole_archive_reads_every_clear_condition_block()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfDungeonDefinitionSource(provider);
        var failures = new List<string>();
        var entries = new List<DungeonClearCondition>();
        var ignoring = 0;
        foreach (var (id, _) in PvfListFile.Read(provider.Archive, "dungeon/dungeon.lst"))
        {
            try
            {
                if (await source.GetAsync(id, default) is not { } dungeon) continue;
                entries.AddRange(dungeon.Mazes.SelectMany(maze => maze.ClearConditions));
                if (dungeon.IgnoresDefaultClear) ignoring++;
            }
            catch (InvalidDataException error) when (error.Message.Contains("clear condition", StringComparison.Ordinal)
                || error.Message.Contains("ignore default", StringComparison.Ordinal))
            {
                failures.Add(error.Message);
            }
            catch (InvalidDataException)
            {
                // Other fields' failures are not this reader's concern.
            }
        }
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures.Take(20)));
        // content 0.3.3-dev.1: 79 destroy object, 10 hunt boss, 6 clear map, 1 limit time.
        var byKind = entries.GroupBy(entry => entry.Kind).ToDictionary(group => group.Key, group => group.Count());
        Assert.Equal(79, byKind.GetValueOrDefault(DungeonClearConditionKind.DestroyObject));
        Assert.Equal(10, byKind.GetValueOrDefault(DungeonClearConditionKind.HuntBoss));
        Assert.Equal(6, byKind.GetValueOrDefault(DungeonClearConditionKind.ClearMap));
        Assert.Equal(1, byKind.GetValueOrDefault(DungeonClearConditionKind.LimitTime));
        Assert.True(ignoring >= 6, $"Only {ignoring} readable dungeons ignore the default clear.");
    }

    [SkippableTheory]
    [InlineData(2356, 1)]
    [InlineData(12001, 1)]
    [InlineData(1842, null)]
    public async Task Quests_carry_their_cant_giveup_restriction(int questId, int? restriction)
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var quest = await new PvfQuestDefinitionSource(provider).GetAsync(questId, default);
        Assert.Equal(restriction, quest!.GiveUpRestriction);
    }
}
