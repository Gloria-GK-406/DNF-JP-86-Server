using Dfo.Pvf.Tests.Support;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Quests;
using Dfo.Server.Application.World;
using Dfo.Server.Data.World;
using Dfo.Server.Infrastructure.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

public sealed class PvfCycleDungeonTests
{
    [Fact]
    public async Task Circle_rewards_with_job_conditions_remain_loadable()
    {
        using var file = CircleQuest();
        var quest = await new PvfQuestDefinitionSource(file.Provider).GetAsync(7, default);

        Assert.NotNull(quest);
        Assert.Contains(quest.Rewards, x => x.Kind == QuestRewardKind.CircleDungeon);
        Assert.Equal([new QuestItemRewardRule(101040199, 1, new(0, -1)),
            new(102000219, 2, new(1, 2))], quest.ItemRewards.Fixed);
        var reward = new QuestRewardRuleService(new RegionalRewards()).Resolve(quest,
            new([100], new Dictionary<char, int>(), 80, 30, 120, 140),
            new(Job: 0, GrowType: 1, Level: 1), ushort.MaxValue, 1);
        Assert.True(reward.IsValid);
        Assert.Equal([new QuestRewardItemGrant(10149701, 1)], reward.Plan!.Items);
        Assert.Equal(0, reward.Plan.Gold);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public async Task Circle_numeric_reward_metadata_is_preserved(int value)
    {
        using var file = CircleQuest(scalar: value);
        var quest = await new PvfQuestDefinitionSource(file.Provider).GetAsync(7, default);

        Assert.NotNull(quest);
        Assert.Equal([value], Assert.Single(quest.Rewards).Values);
        Assert.Empty(quest.ItemRewards.Fixed);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("truncated")]
    [InlineData("job")]
    [InlineData("count")]
    public async Task Malformed_circle_reward_conditions_do_not_become_unrestricted_rewards(string defect)
    {
        using var file = CircleQuest(defect);
        Assert.Null(await new PvfQuestDefinitionSource(file.Provider).GetAsync(7, default));
    }

    [SkippableFact]
    public async Task A21_region15_cycle_candidates_all_resolve_to_circle_quests()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var region = new PvfCycleDungeonDefinitionSource(provider).Load().Single(x => x.WorldMapId == 15);
        var source = new PvfQuestDefinitionSource(provider);
        foreach (var candidate in region.Candidates)
        {
            var quest = await source.GetAsync(candidate.QuestId, default);
            Assert.True(quest is not null, $"Missing cycle quest {candidate.QuestId}");
            Assert.Equal(QuestGrade.Circle, quest.Grade);
            Assert.Contains(quest.Rewards, x => x.Kind == QuestRewardKind.CircleDungeon);
        }
    }

    [SkippableFact]
    public async Task A21_haze_cycle_maze_reaches_boss_despite_unused_map_bindings()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfDungeonDefinitionSource(provider);
        var rules = new DungeonStructureRuleService(source, new PvfDungeonMapDefinitionSource(provider));
        var facts = new DungeonEntryFacts(DungeonDifficulty.Normal, [14825], [], DungeonEntryIntent.Cycle(92, 14825));
        var route = new MazeCell[] { new(0, 4), new(1, 4), new(1, 3), new(2, 3),
            new(2, 2), new(2, 1), new(2, 0), new(3, 0) };
        var layout = await rules.ResolveAsync(92, facts, 1, default);

        Assert.NotNull(layout);
        Assert.Equal(13, layout.MazeIndex);
        Assert.Equal(57479, layout.StartRoomMapId);
        Assert.Equal(8, layout.Cells.Count);
        Assert.DoesNotContain(new MazeCell(3, 1), layout.Cells.Keys);
        Assert.DoesNotContain(new MazeCell(3, 2), layout.Cells.Keys);
        for (var index = 1; index < route.Length; index++)
            Assert.NotNull((await rules.SelectMapAsync(layout, route[index - 1], route[index], default)).MapId);
    }

    [SkippableFact]
    public async Task A21_cycle_catalog_resolves_the_observed_pair_quest_maze_and_regional_box()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var regions = new PvfCycleDungeonDefinitionSource(provider).Load();
        Assert.Equal(10, regions.Count);
        var region = Assert.Single(regions, x => x.WorldMapId == 12);
        Assert.Equal(2228, region.OpenQuestId);
        Assert.Equal(3, region.DeclaredDifficultyLevel);
        Assert.Contains(new CycleDungeonCandidate(84, 15203), region.Candidates);
        // Observed retry: 000F(previous 82), 0308(new 85/15217), 0020(old 15242).
        Assert.Contains(new CycleDungeonCandidate(82, 15242), region.Candidates);
        Assert.Contains(new CycleDungeonCandidate(85, 15217), region.Candidates);
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

    private static BuiltArchive CircleQuest(string? defect = null, int? scalar = null)
    {
        var builder = new PvfArchiveBuilder();
        builder.AddScript("n_quest", "quest.lst").Int(7).Quoted("circle.qst").Int(8).Quoted("other.qst").End();
        builder.AddScript("n_quest", "other.qst").Tag("[name]").Quoted("Valid control quest").End();
        var script = builder.AddScript("n_quest", "circle.qst").Tag("[grade]").Quoted("[circle]")
            .Tag("[reward type]").Quoted("[circle dungeon]").Tag("[reward int data]");
        if (scalar is { } value)
        {
            script.Int(value).End();
            return new BuiltArchive(builder);
        }
        script.Int(0).Int(0).Int(101040199).Quoted(defect == "unknown" ? "[other]" : "[job]")
            .Int(defect == "job" ? -2 : 0).Int(-1);
        if (defect != "truncated") script.Int(defect == "count" ? 0 : 1);
        if (defect is null) script.Int(102000219).Quoted("[job]").Int(1).Int(2).Int(2);
        script.End();
        return new BuiltArchive(builder);
    }

    private sealed class BuiltArchive : IDisposable
    {
        private readonly string path = Path.Combine(Path.GetTempPath(), $"pvf-circle-{Guid.NewGuid():N}.pvf");
        internal PvfArchiveProvider Provider { get; }
        internal BuiltArchive(PvfArchiveBuilder builder)
        {
            File.WriteAllBytes(path, builder.Build());
            Provider = new(new PvfContentOptions { ArchivePath = path });
        }
        public void Dispose() { Provider.Dispose(); File.Delete(path); }
    }

    private sealed class RegionalRewards : ICycleDungeonRuleService
    {
        public ValueTask<CycleDungeonRegion?> EligibleAsync(int dungeonId, int questId,
            WorldContentSubject subject, CancellationToken cancellationToken) => throw new NotSupportedException();
        public IReadOnlyList<QuestRewardItemGrant> RewardsForQuest(int questId) => [new(10149701, 1)];
    }
}
