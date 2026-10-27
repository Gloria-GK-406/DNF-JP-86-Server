using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Application.World;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Tests.World;

[Trait("Category", "Unit")]
public sealed class AltarDungeonRuleServiceTests
{
    private static AltarDungeonDefinition Definition() => new(11006, AltarDungeonKind.Endless, 60, 60, 15, 5, [],
        new(100, 0, 100, 7, Enumerable.Repeat(2.0, 16).ToArray(), [1, 2, 4, 6], [300, 200], [], ""), 3)
    { AdmissionPricing = new([new(1, 50, 8000), new(51, 55, 10000), new(56, 99, 12000)],
        new Dictionary<int, int> { [1] = 0, [2] = 60, [3] = 200 }, 10000) };
    private static AltarDungeonRuleService Rules(AltarDungeonPolicy? policy = null) =>
        new(policy ?? new(), new Source(), new TestExperienceRules(), new Rewards());

    [Fact]
    public void Admission_prices_use_first_entry_key_above_the_pre_entry_count()
    {
        var rules = Rules();
        Assert.Equal(22000, rules.AdmissionGold(Definition(), 86, 0));
        Assert.Equal(29200, rules.AdmissionGold(Definition(), 86, 1));
        Assert.Equal(46000, rules.AdmissionGold(Definition(), 86, 2));
        Assert.Equal(18000, rules.AdmissionGold(Definition(), 50, 0));
        Assert.Equal(0, rules.AdmissionGold(Definition() with { Kind = AltarDungeonKind.Ultimate }, 86, 2));
        Assert.Throws<InvalidDataException>(() => rules.AdmissionGold(Definition(), 100, 0));
    }
    [Fact]
    public void Empty_windows_are_all_day_and_configured_windows_are_half_open_in_server_time()
    {
        var utc = new DateTimeOffset(2026, 10, 13, 2, 0, 0, TimeSpan.Zero);
        Assert.True(Rules().IsOpen(11006, utc));
        var rules = Rules(new() { OpeningWindows = [new(11006, new(10, 0), new(10, 10)), new(11007, new(23, 0), new(1, 0))] });
        Assert.True(rules.IsOpen(11006, utc));
        Assert.False(rules.IsOpen(11006, utc.AddMinutes(10)));
        Assert.False(rules.IsOpen(11006, utc.AddTicks(-1)));
        Assert.True(rules.IsOpen(11007, utc.AddHours(14)));
        Assert.False(rules.IsOpen(11007, utc));
    }
    [Fact]
    public void Concurrent_phases_and_batch_growth_preserve_delays_unique_sequences_and_clear_gate()
    {
        var map = new AltarMapDefinition(16348,
            [new(100, 0, 200, 300, 0, 3000, 500, 1, 1, 2), new(101, 0, 400, 500, 0, 1000, 0, 2, 0, 1)],
            [new(0, [new(0, 0, 100, 1.5, 0, 2, 0), new(0, 1, 200, 1, 0, 0, 0)])]);
        var waves = Rules().PlanRound(map, 0, 0, 0, 60, 42);
        Assert.Equal(new[] { 100, 200, 600 }, waves.Select(x => x.OffsetMilliseconds));
        Assert.Equal(new[] { 1, 2, 2 }, waves.Select(x => x.Actors.Count));
        Assert.Equal(new ushort[] { 42, 43, 44, 45, 46 }, waves.SelectMany(x => x.Actors).Select(x => x.Sequence));
        Assert.All(waves.Take(2), x => Assert.Equal((ushort)1, x.SpawnTail));
        Assert.Equal((ushort)0, waves[^1].SpawnTail);
        Assert.All(waves.SelectMany(x => x.Actors), x => { Assert.Equal((byte)19, x.Variant); Assert.Equal((byte)0, x.ActorType); });
        Assert.Equal((ushort)150, waves[0].Actors[0].Scale);
        Assert.Throws<InvalidDataException>(() => Rules().PlanRound(map, 0, 0, 0, 60, 65534));
    }
    [Fact]
    public void Difficulty_selects_only_its_templates_and_only_final_challenge_batch_gets_boss_rank()
    {
        var map = new AltarMapDefinition(17030, [new(100, 0, 10, 20, 0, 0, 100, 1, 0, 2), new(200, 0, 10, 20, 0, 0, 0, 1, 0, 1)],
            [new(0, [new(0, 0, 0, 1, 0, 0, 1), new(0, 1, 0, 1, 0, 0, 2)])]);
        var hard = Rules().PlanRound(map, 0, 1, 6, 70, 1);
        Assert.All(hard.SelectMany(x => x.Actors), x => Assert.Equal(100, x.MonsterCode));
        Assert.Equal((byte)0, hard[0].Actors[0].ActorType);
        Assert.Equal((byte)3, hard[^1].Actors[0].ActorType);
        var easy = Rules().PlanRound(map, 0, 2, 3, 70, 20);
        Assert.Equal(200, Assert.Single(Assert.Single(easy).Actors).MonsterCode);
        Assert.Equal((byte)0, easy[0].Actors[0].ActorType);
    }
    [Fact]
    public void Reward_cards_remain_distinct_while_payment_aggregates_and_zero_rounds_grant_nothing()
    {
        var state = new AltarDungeonState(DungeonRunId.New(), Definition(), [new(new(new(1), new(2), 0, true), 86, 2, 29200)],
            DateTimeOffset.UnixEpoch, 1, 16348) { CompletedRounds = 15 };
        var reward = Assert.Single(Rules().Rewards(state, 7));
        Assert.Equal(10, reward.Cards.Count);
        Assert.All(reward.Cards, x => Assert.Equal(new AltarReward(0, 700), x));
        Assert.Equal(7000, reward.Gold);
        Assert.Empty(reward.Items);
        IExperienceRuleService experience = new TestExperienceRules();
        Assert.Equal((uint)(15 * experience.MobReward(86) * 2 * 4), reward.Experience);
        var none = Assert.Single(Rules().Rewards(state with { CompletedRounds = 0 }, 7));
        Assert.Empty(none.Cards);
        Assert.Equal(0u, none.Experience);
        Assert.Equal(0, none.Gold);
    }
    private sealed class Source : IAltarDungeonContentSource
    {
        public ValueTask<AltarDungeonDefinition> GetAsync(int id, CancellationToken ct) => ValueTask.FromResult(Definition());
    }
    private sealed class Rewards : IClearRewardRuleService
    {
        public ClearRewardChance ChanceOf(ClearRewardOccasion occasion) => default;
        public ClearRewardCardFace FaceFor(ClearRewardOccasion occasion, ulong seed) => new(occasion.Kind, [new(0, null)], 100);
        public int? PaidFlipCost(int level) => 1;
        public ValueTask<bool> OffersPaidFlipAsync(int id, CancellationToken ct) => ValueTask.FromResult(true);
    }
}
