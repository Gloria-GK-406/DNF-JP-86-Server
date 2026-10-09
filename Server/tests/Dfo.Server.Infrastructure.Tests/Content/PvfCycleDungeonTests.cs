using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Infrastructure.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

public sealed class PvfCycleDungeonTests
{
    [SkippableFact]
    public async Task A21_cycle_catalog_resolves_the_observed_pair_quest_maze_and_regional_box()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var regions = new PvfCycleDungeonDefinitionSource(provider).Load();
        Assert.Equal(10, regions.Count);
        var region = Assert.Single(regions, x => x.WorldMapId == 12);
        Assert.Equal(2228, region.OpenQuestId);
        Assert.Equal(3, (int)region.Difficulty);
        Assert.Contains(new CycleDungeonCandidate(84, 15203), region.Candidates);
        Assert.Equal(10149700, Assert.Single(region.Rewards).ItemId);
        Assert.Equal(1, Assert.Single(region.Rewards).Count);
        var quest = await new PvfQuestDefinitionSource(provider).GetAsync(15203, default);
        Assert.NotNull(quest);
        Assert.Equal(QuestGrade.Circle, quest.Grade);
        Assert.True(quest.IsRepeatable);
        Assert.Contains(quest.Rewards, x => x.Kind == QuestRewardKind.CircleDungeon);
        Assert.Contains(quest.SeekingItems!, x => x.ItemId == 4396 && x.RequiredCount == 1);
        var definition = await new PvfDungeonDefinitionSource(provider).GetAsync(84, default);
        Assert.Contains(definition!.Mazes, x => x.QuestConnection?.QuestId == 15203);
    }
}
