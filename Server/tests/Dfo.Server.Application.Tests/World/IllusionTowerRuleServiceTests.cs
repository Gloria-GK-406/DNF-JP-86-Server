using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Application.World.IllusionTower;
using Dfo.Server.Infrastructure.Content;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.World;

public sealed class IllusionTowerRuleServiceTests
{
    [PvfFact]
    public async Task Current_archive_builds_every_floor_in_dgn_order_including_pure_apc_floors()
    {
        using var provider = new PvfArchiveProvider(new PvfContentOptions());
        var rules = new IllusionTowerRuleService(new PvfIllusionTowerContentSource(provider));
        var definition = await rules.GetDefinitionAsync(CancellationToken.None);
        Assert.Equal(11001, definition.DungeonId);
        Assert.Equal(40, definition.MinimumLevel);
        Assert.Equal(40, definition.BasisLevel);
        Assert.Equal(new TowerRequiredItem(4183, 1, true), Assert.Single(definition.RequiredItems));
        Assert.Equal(new[] { 31001, 31002, 31003, 31004, 31005, 31007, 31008, 31010, 31011, 31006,
            31019, 31015, 31020, 31024, 31025 }, definition.Rooms.Select(room => room.MapId));
        Assert.Equal(new[] { 1, 1, 1, 1, 1, 2, 2, 2, 2, 2, 3, 3, 3, 4, 4 }, definition.Rewards.CardCounts);
        Assert.Equal(15, definition.Rewards.ExperienceWeights.Count);
        Assert.Equal(4.752, definition.Rewards.ExperienceWeights[0], 4);
        Assert.Equal(0, definition.Parameters.ItemDrop);
        Assert.Equal(new[] { 0, 10, 100 }, definition.Parameters.RecoveryOperands[0]);
        Assert.NotEmpty(definition.RandomApcCandidates);
        int[] fixedCounts = [3, 7, 0, 4, 1, 5, 7, 1, 0, 6, 8, 2, 3, 3, 10];
        for (var level = 1; level <= 15; level++)
        {
            var room = await rules.CreateRoomAsync(level, 1234, CancellationToken.None);
            Assert.Equal(fixedCounts[level - 1], definition.Rooms[level - 1].Actors.Count);
            Assert.NotEmpty(room.Actors);
            Assert.All(room.Actors, actor => Assert.True(actor.BlocksClear));
            Assert.NotEmpty(room.Objects);
            if (level is not (3 or 9)) Assert.Equal(fixedCounts[level - 1], room.Actors.Count);
            Assert.All(room.Actors.Where(actor => actor.ActorType <= 3), actor => Assert.Equal(40, actor.Level));
        }
        var final = await rules.CreateRoomAsync(15, 0, CancellationToken.None);
        var apc = Assert.Single(final.Actors, actor => actor.ActorType >= 5);
        Assert.Equal(608, apc.TemplateId);
        Assert.Equal(40, apc.Level);
        Assert.Equal(0, apc.ListIndex);
    }

    [PvfTheory]
    [InlineData(3)]
    [InlineData(9)]
    public async Task Current_random_floors_are_repeatable_and_respect_budget_and_positions(int level)
    {
        using var provider = new PvfArchiveProvider(new PvfContentOptions());
        var rules = new IllusionTowerRuleService(new PvfIllusionTowerContentSource(provider));
        var definition = await rules.GetDefinitionAsync(CancellationToken.None);
        var declared = definition.Rooms[level - 1];
        for (uint seed = 0; seed < 32; seed++)
        {
            var first = await rules.CreateRoomAsync(level, seed, CancellationToken.None);
            var repeated = await rules.CreateRoomAsync(level, seed, CancellationToken.None);
            Assert.Equal(first.Actors, repeated.Actors);
            Assert.InRange(first.Actors.Count, 1, declared.RandomApcPositions.Count);
            Assert.Equal(first.Actors.Count, first.Actors.Select(actor => actor.TemplateId).Distinct().Count());
            Assert.InRange(first.Actors.Sum(actor => definition.RandomApcCandidates.Single(candidate => candidate.TemplateId == actor.TemplateId).AppearancePoint), 1, declared.RandomApcPoints);
            Assert.Equal(declared.RandomApcPositions.Take(first.Actors.Count), first.Actors.Select(actor => actor.Position));
            Assert.Equal(Enumerable.Range(64, first.Actors.Count), first.Actors.Select(actor => actor.ListIndex));
        }
    }

    [Fact]
    public async Task Unusable_random_configuration_never_becomes_an_empty_clearable_room()
    {
        var definition = Definition(new(1, 10, [], 50, [new(1, 2, 3)], []), [new(77, 40, 51)]);
        var rules = new IllusionTowerRuleService(new Source(definition));
        await Assert.ThrowsAsync<InvalidDataException>(() => rules.CreateRoomAsync(1, 1, CancellationToken.None).AsTask());
        rules = new(new Source(definition with { Rooms = [definition.Rooms[0] with { RandomApcPositions = [] }] }));
        await Assert.ThrowsAsync<InvalidDataException>(() => rules.CreateRoomAsync(1, 1, CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task Level_modes_preserve_absolute_apc_and_resolve_monster_offset()
    {
        TowerActorDeclaration[] actors = [new(1, 3, true, 0, true, new(0, 0, 0)), new(2, 28, false, 0, true, new(0, 0, 0)), new(3, 70, false, 5, true, new(0, 0, 0))];
        var rules = new IllusionTowerRuleService(new Source(Definition(new(1, 10, actors, 0, [], []), [])));
        var room = await rules.CreateRoomAsync(1, 10, CancellationToken.None);
        Assert.Equal(new byte[] { 43, 28, 70 }, room.Actors.Select(actor => actor.Level));
        Assert.Equal(new[] { 0, 1, 0 }, room.Actors.Select(actor => actor.ListIndex));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => rules.CreateRoomAsync(2, 0, CancellationToken.None).AsTask());
    }

    private static IllusionTowerDefinition Definition(TowerRoomDefinition room, IReadOnlyList<TowerApcCandidate> candidates) =>
        new(11001, 40, 40, [], [room], candidates, new([], [], [], 0, 0, 0), new(0, 0, 0, 0, [], [], []));
    private sealed class Source(IllusionTowerDefinition definition) : IIllusionTowerContentSource
    {
        public ValueTask<IllusionTowerDefinition> GetAsync(CancellationToken cancellationToken) => ValueTask.FromResult(definition);
    }
}
