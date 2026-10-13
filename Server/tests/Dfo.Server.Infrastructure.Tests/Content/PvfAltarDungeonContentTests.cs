using Dfo.Server.Abstractions.World;
using Dfo.Server.Infrastructure.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

public sealed class PvfAltarDungeonContentTests
{
    [SkippableFact]
    public async Task Endless_loads_every_start_middle_and_boss_candidate_with_local_rounds()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfAltarDungeonContentSource(provider, new PvfDungeonMapDefinitionSource(provider));
        var definition = await source.GetAsync(11006, default);
        Assert.Equal(AltarDungeonKind.Endless, definition.Kind);
        Assert.Equal(60, definition.MinimumLevel);
        Assert.Equal(60, definition.BasisLevel);
        Assert.Equal(15, definition.MaxRounds);
        Assert.Equal(5, definition.CoinLimit);
        Assert.Equal(3, definition.DailyEntries);
        Assert.Equal(Enumerable.Range(16348, 7), definition.Maps.Select(value => value.MapId));
        Assert.All(definition.Maps, map =>
        {
            Assert.Equal(Enumerable.Range(0, 5), map.Rounds.Select(value => value.Number));
            Assert.NotEmpty(map.Monsters);
            Assert.All(map.Rounds.SelectMany(value => value.Phases), phase =>
            {
                Assert.Equal(0, phase.Difficulty);
                Assert.InRange(phase.MonsterTemplateIndex, 0, map.Monsters.Count - 1);
            });
        });
        var middle = Assert.Single(definition.Maps, value => value.MapId == 16351);
        Assert.Equal(new AltarMonsterDefinition(62001, 0, 792, 258, 0, 10000, 3000, 1, 0, 3), middle.Monsters[0]);
        Assert.Equal(new AltarPhaseDefinition(0, 0, 3000, (double)1.1f, 0, 0, 0), middle.Rounds[0].Phases[0]);
        Assert.Same(definition, await source.GetAsync(11006, default));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => source.GetAsync(11008, default).AsTask());
    }

    [SkippableFact]
    public async Task Ultimate_loads_both_difficulty_branches_and_complete_reward_tables()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfAltarDungeonContentSource(provider, new PvfDungeonMapDefinitionSource(provider));
        var definition = await source.GetAsync(11007, default);
        Assert.Equal(AltarDungeonKind.Ultimate, definition.Kind);
        Assert.Equal(65, definition.MinimumLevel);
        Assert.Equal(70, definition.BasisLevel);
        Assert.Equal(7, definition.MaxRounds);
        Assert.Equal(7, definition.CoinLimit);
        Assert.Equal(3, definition.DailyEntries);
        var map = Assert.Single(definition.Maps);
        Assert.Equal(17030, map.MapId);
        Assert.Equal(20, map.Monsters.Count);
        Assert.Equal(Enumerable.Range(0, 7), map.Rounds.Select(value => value.Number));
        Assert.Equal(new byte[] { 1, 2 }, map.Rounds[1].Phases.Select(value => value.Difficulty).Distinct().Order());
        Assert.All(new[] { map.Rounds[0], map.Rounds[^1] }, round => Assert.All(round.Phases, phase => Assert.Equal(1, phase.Difficulty)));
        Assert.Equal(new AltarMonsterDefinition(56017, 0, 440, 222, 0, 5000, 4000, 1, 0, 1), map.Monsters[0]);
        Assert.Equal(new AltarPhaseDefinition(0, 0, 5000, 1.5, 0, 0, 1), map.Rounds[0].Phases[0]);
        var rewards = definition.Rewards;
        Assert.Equal(50, rewards.GoldCandidateWeight);
        Assert.Equal(47, rewards.ItemCandidateWeight);
        Assert.Equal(100, rewards.RewardWeightPerProgress);
        Assert.Equal(7, rewards.GoldAmountWeight);
        Assert.Equal(new double[] { 0, 1, 1, 1, 1, 1, 2, 2, 2, 2, 2, 3, 3, 3, 3, 4 }, rewards.RewardExperienceWeights);
        Assert.Equal(13, rewards.EntryCountExperienceWeights.Count);
        Assert.Equal((double)2.9f, rewards.EntryCountExperienceWeights[10]);
        Assert.Equal(new[] { 300, 200 }, rewards.UltimateDifficultyPoints);
        Assert.Equal(11, rewards.UltimateRewards.Count);
        Assert.Equal(new AltarUltimateRewardProbability(2100, 0, 0), rewards.UltimateRewards[0]);
        Assert.Equal(new AltarUltimateRewardProbability(1100, 0, 0), rewards.UltimateRewards[^1]);
        Assert.Equal("etc/itemdropinfo_clearreward.etc", rewards.ItemRewardTablePath);
        Assert.Same(definition, await source.GetAsync(11007, default));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.GetAsync(11007, cancelled.Token).AsTask());
    }
}
