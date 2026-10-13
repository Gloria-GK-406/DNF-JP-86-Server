using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.World;

namespace Dfo.Server.Application.Tests.World;

public sealed class TournamentDungeonRuleServiceTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(65534)]
    public async Task Path_actor_ids_are_unique_and_never_shadow_reserved_player_ids(ushort playerId)
    {
        var rules = new TournamentDungeonRuleService(new Source(Definition()), new GoldRules());
        var plan = await rules.PrepareAsync(120, 7, new HashSet<ushort> { playerId }, default);
        Assert.Equal(4, plan.PathActors.Select(actor => actor.Sequence).Distinct().Count());
        Assert.DoesNotContain(plan.PathActors, actor => actor.Sequence == playerId || actor.Sequence == 0);
        if (playerId == 2) Assert.Equal(new ushort[] { 1, 3, 4, 5 }, plan.PathActors.Select(actor => actor.Sequence));
        var multiple = await rules.PrepareAsync(120, 7, new HashSet<ushort> { 1, 2, 3, 4 }, default);
        Assert.Equal(new ushort[] { 5, 6, 7, 8 }, multiple.PathActors.Select(actor => actor.Sequence));
    }

    [Fact]
    public async Task Every_draw_has_one_player_fifteen_unique_opponents_and_a_four_round_legal_path()
    {
        var rules = new TournamentDungeonRuleService(new Source(Definition()), new GoldRules());
        var firstOpponents = new HashSet<int>();
        for (uint seed = 0; seed < 64; seed++)
        {
            var plan = await rules.PrepareAsync(120, seed, new HashSet<ushort>(), default);
            var repeat = await rules.PrepareAsync(120, seed, new HashSet<ushort>(), default);
            Assert.Equal(new[] { 16, 8, 4, 2 }, plan.Rounds.Select(round => round.Teams.Count));
            Assert.Equal(plan.PathActors, repeat.PathActors);
            Assert.Equal(16, plan.Rounds[0].Teams.Select(team => team.Code).Distinct().Count());
            Assert.Equal(new ushort[] { 1, 2, 3, 4 }, plan.PathActors.Select(actor => actor.Sequence));
            for (var index = 0; index < 4; index++)
            {
                var round = plan.Rounds[index];
                Assert.Single(round.Teams, team => team.Code == 0);
                Assert.Equal(Enumerable.Range(0, round.Teams.Count), round.Teams.Select(team => (int)team.Position));
                var player = round.Teams.Single(team => team.Code == 0);
                Assert.Equal(round.Teams[player.Position ^ 1].Code, plan.PathActors[index].Code);
                if (index > 0)
                    Assert.All(round.Teams, team => Assert.Contains(plan.Rounds[index - 1].Teams, previous => previous.Code == team.Code));
            }
            Assert.Equal(3, plan.PathActors[3].Type);
            Assert.All(plan.PathActors.Take(3), actor => Assert.Equal(0, actor.Type));
            firstOpponents.Add(plan.PathActors[0].Code);
        }
        Assert.True(firstOpponents.Count > 1);
        Assert.True(rules.IsSupported(121));
        Assert.True(rules.IsSupported(122));
        Assert.False(rules.IsSupported(123));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => rules.PrepareAsync(123, 0, new HashSet<ushort>(), default).AsTask());
    }

    [Fact]
    public async Task Rewards_use_completed_round_rows_frozen_gold_multiplier_and_champion_only_second_row()
    {
        var gold = new GoldRules();
        var rules = new TournamentDungeonRuleService(new Source(Definition()), gold);
        var plan = await rules.PrepareAsync(120, 42, new HashSet<ushort>(), default);
        var failure = rules.Rewards(plan, 0, 86);
        Assert.All(failure[0], card => Assert.Equal(new TournamentRewardCard(-1, 0), card));
        Assert.Empty(failure[1]);
        var champion = rules.Rewards(plan, 4, 86);
        Assert.All(champion[0], card => Assert.Equal(new TournamentRewardCard(0, 1600), card));
        Assert.All(champion[1], card => { Assert.Equal(3329, card.ItemId); Assert.InRange(card.Count, 1, 3); });
        var repeat = rules.Rewards(plan, 4, 86);
        Assert.Equal(champion[0], repeat[0]);
        Assert.Equal(champion[1], repeat[1]);
        Assert.Equal(72, gold.Occasion!.DungeonBasisLevel);
        Assert.Equal(86, gold.Occasion.CharacterLevel);
        Assert.Throws<ArgumentOutOfRangeException>(() => rules.Rewards(plan, 5, 86));
    }

    [Fact]
    public async Task Malformed_candidate_pools_fail_before_a_partial_bracket_exists()
    {
        var definition = Definition();
        var shortPool = new TournamentDungeonRuleService(new Source(definition with { Candidates = definition.Candidates.Take(14).ToArray() }), new GoldRules());
        await Assert.ThrowsAsync<InvalidDataException>(() => shortPool.PrepareAsync(120, 1, new HashSet<ushort>(), default).AsTask());
        var duplicatePool = new TournamentDungeonRuleService(new Source(definition with { Candidates = Enumerable.Repeat(definition.Candidates[0], 15).ToArray() }), new GoldRules());
        await Assert.ThrowsAsync<InvalidDataException>(() => duplicatePool.PrepareAsync(120, 1, new HashSet<ushort>(), default).AsTask());
    }

    private static TournamentDefinition Definition() => new(120, 17100, 69, 72, 2, 2, [],
        Enumerable.Range(1, 15).Select(index => new TournamentCandidate(index, index * 100, 72, false)).ToArray(),
        [0, 30000, 50000, 100000, 200000],
        [new(0, 0, 0, 100), new(1, 70, 0, 30), new(2, 80, 0, 20), new(3, 90, 0, 10), new(4, 100, 0, 0), new(5, 0, 100, 0)],
        [new(3329, 60, 1), new(3329, 35, 2), new(3329, 5, 3)], 16);
    private sealed class Source(TournamentDefinition definition) : ITournamentDungeonContentSource
    {
        public ValueTask<TournamentDefinition> GetAsync(int dungeonId, CancellationToken cancellationToken) => ValueTask.FromResult(definition);
    }
    private sealed class GoldRules : IClearRewardRuleService
    {
        public ClearRewardOccasion? Occasion { get; private set; }
        public ClearRewardChance ChanceOf(ClearRewardOccasion occasion) => new(1, 1);
        public ClearRewardCardFace FaceFor(ClearRewardOccasion occasion, ulong seed)
        {
            Occasion = occasion;
            return new(occasion.Kind, [], 100);
        }
        public int? PaidFlipCost(int dungeonBasisLevel) => null;
        public ValueTask<bool> OffersPaidFlipAsync(int dungeonId, CancellationToken cancellationToken) => ValueTask.FromResult(false);
    }
}
