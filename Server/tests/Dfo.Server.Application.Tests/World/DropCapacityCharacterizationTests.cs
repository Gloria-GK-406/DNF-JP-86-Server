using Dfo.Server.Application.World;
using Dfo.Server.Protocol.Game.DropMaterialization;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Protocol.Game.MonsterDeath;
using Dfo.Server.Testing;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.Tests.World;

public sealed partial class DungeonRunDomainServiceTests
{
    [Theory]
    [Trait("Category", "Unit")]
    [InlineData(0)]
    [InlineData(254)]
    [InlineData(255)]
    [InlineData(256)]
    [InlineData(300)]
    public void Death_capacity_preserves_order_state_and_notification_identity(int count)
    {
        TestGameplay.Run(() =>
        {
            var runs = TestParties.Runs();
            var pending = Enumerable.Range(0, count)
                .Select(i => new RolledDrop(1000 + i, i + 1, DropRarity.Common, DropItemKind.Stackable)).ToArray();
            BeginWithCell(runs, actors: [CellActor(FirstSequence, new(61670, 1, 0), pendingLoot: pending)]);
            var claim = runs.TryClaimActorDeath(Character, FirstSequence)!;
            var change = runs.CommitActorDeathChange(claim)!;
            var expected = pending.Take(255).ToArray();
            Assert.Equal(expected.Select(x => x.ItemId), change.AddedGroundItems.Select(x => x.ItemId));
            Assert.Equal(expected.Select(x => x.Count), change.AddedGroundItems.Select(x => x.Count));
            Assert.Equal(Enumerable.Range(1, expected.Length).Select(x => (ushort)x), change.AddedGroundItems.Select(x => x.Number));
            Assert.Equal(change.AddedGroundItems, change.Run.CurrentCellState.GroundItems);
            Assert.Empty(change.Run.CurrentCellState.FindActor(FirstSequence)!.PendingLoot);
            var notification = new MonsterDeathNotification(FirstSequence,
                change.AddedGroundItems.Select(x => x.ToMonsterDrop()).ToArray(), 1, 77);
            var encoded = MonsterDeathPayloadCodec.EncodeMonsterDeath(notification);
            Assert.Equal(expected.Length, encoded[2]);
            Assert.Null(runs.CommitActorDeathChange(claim));
            Assert.Null(runs.TryClaimActorDeath(Character, FirstSequence));
            Assert.Equal(change.AddedGroundItems, runs.Find(Character)!.CurrentCellState.GroundItems);
        });
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Death_filters_delivery_and_membership_before_limiting_combined_group_rewards()
    {
        TestGameplay.Run(() =>
        {
            var runs = TestParties.Runs();
            var excluded = Enumerable.Range(0, 30).Select(i => new RolledDrop(8000 + i, 1,
                DropRarity.Common, DropItemKind.Stackable, OwnerCharacterId: new CharacterId(999)))
                .Concat(Enumerable.Range(0, 30).Select(i => new RolledDrop(9000 + i, 1,
                    DropRarity.Common, DropItemKind.Stackable, OwnerCharacterId: Character,
                    Delivery: RolledDropDelivery.DirectQuestItem)));
            var pending = Enumerable.Range(0, 250).Select(i => new RolledDrop(1000 + i, 1,
                DropRarity.Common, DropItemKind.Stackable)).ToArray();
            var actor = CellActor(FirstSequence, new(20444, 1, 5), pendingLoot: excluded.Concat(pending).ToArray())
                with { HellGroupIndex = 136, IsHellTemplate = true, EligibleForHellGroupReward = true };
            var groupLoot = Enumerable.Range(0, 20).Select(i => new RolledDrop(2000 + i, 1,
                DropRarity.Rare, DropItemKind.Stackable)).ToArray();
            var seed = CellSeed(Tutorial.StartCell, Tutorial.StartRoomMapId, [actor]) with
            { HellGroups = [new(136, [FirstSequence], groupLoot)] };
            runs.PreparePartyEntry([new(new(1), Character, 0, true)], Tutorial, 0, seed,
                DungeonRunId.New(), DateTimeOffset.UnixEpoch);
            var change = runs.CommitActorDeathChange(runs.TryClaimActorDeath(Character, FirstSequence)!)!;
            Assert.Equal(pending.Concat(groupLoot).Take(255).Select(x => x.ItemId),
                change.AddedGroundItems.Select(x => x.ItemId));
            Assert.True(Assert.Single(change.Run.CurrentCellState.HellGroups).IsSettled);
            Assert.Empty(Assert.Single(change.Run.CurrentCellState.HellGroups).PendingLoot);
        });
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData(65534, 1)]
    [InlineData(65535, 0)]
    public void Death_keeps_only_available_run_numbers_without_wrapping(int occupied, int expected)
    {
        TestGameplay.Run(() =>
        {
            var runs = TestParties.Runs();
            BeginWithCell(runs,
                actors: [CellActor(FirstSequence, new(61670, 1, 0), pendingLoot:
                    [new(1234, 1, DropRarity.Common, DropItemKind.Stackable), new(1235, 1, DropRarity.Common, DropItemKind.Stackable)])],
                groundItems: Enumerable.Range(0, occupied).Select(_ => DeclaredItem(1)).ToArray());
            var change = runs.CommitActorDeathChange(runs.TryClaimActorDeath(Character, FirstSequence)!)!;
            Assert.Equal(expected, change.AddedGroundItems.Count);
            if (expected > 0) Assert.Equal(ushort.MaxValue, Assert.Single(change.AddedGroundItems).Number);
            Assert.True(change.Run.CurrentCellState.FindActor(FirstSequence)!.IsDead);
            Assert.Equal(65535, change.Run.CurrentCellState.GroundItems.Count);
        });
    }
}

public sealed partial class DungeonCellFactoryTests
{
    [Theory]
    [Trait("Category", "Unit")]
    [InlineData(254)]
    [InlineData(255)]
    [InlineData(256)]
    public async Task Initial_capacity_drops_only_the_tail_and_keeps_actor_loot_untrimmed(int count)
    {
        var room = new CapacityRoom(count);
        var logger = new CapacityLogger<Dfo.Server.Application.World.DungeonCellFactory>();
        var factory = new Dfo.Server.Application.World.DungeonCellFactory(room, new NoPlacement(), new CapacityLoot(),
            new MonsterBoundary(), new RecordingQuestLoot(), new NoQuestBonus(), new NoHellRewards(),
            new NoLostLandDrops(), new FixedDrawMultipliers(), new DropMaterializationRuleService(DropMaterializationProtocolCapabilities.Limits), logger,
            TestGameplayBenefits.Query<DungeonCellCreationContext>(new LuckySharedDropBenefits(TestGameplayBenefits.LuckyRules)));
        var cell = new MazeCell(0, 0);
        var layout = new DungeonLayout(77, 0, 1, 1, cell, cell, 123, 456,
            new Dictionary<MazeCell, DungeonCellDefinition> { [cell] = DungeonCellDefinition.Fixed(123) });
        var seed = await factory.CreateAsync(new(DungeonRunId.New(), layout, cell, 123, 0,
            DateTimeOffset.UnixEpoch, [new(new(1), new(10), 0, true)]), default);
        Assert.Equal(Enumerable.Range(0, Math.Min(count, 255)).Select(i => 1000 + i), seed.GroundItems.Select(x => x.ItemId));
        Assert.Equal(Enumerable.Range(1, Math.Min(count, 255)).Select(i => (ushort)i), seed.GroundItems.Select(x => x.Number));
        Assert.Equal(300, Assert.Single(seed.Actors).PendingLoot.Count);
        Assert.Equal(count > 255 ? 2 : 1, logger.Warnings.Count);
    }

    private sealed class CapacityRoom(int count) : IDungeonRoomRuleService
    {
        public ValueTask<DungeonRoomContents> DescribeAsync(DungeonCellCreationContext context, CancellationToken ct) =>
            ValueTask.FromResult(new DungeonRoomContents([new(new(61670, 1, 0), false)],
                Enumerable.Range(0, count).Select(i => new GroundDrop((ushort)(i + 1), new(0, 1000 + i, 25, i + 1))).ToArray(), 100));
        public IReadOnlyList<RoomActorDescription> Describe(IReadOnlyList<DungeonRoomActor> actors) => throw new NotSupportedException();
        public ushort FirstActorSequenceFor(DungeonCellCreationContext context) => 100;
    }

    private sealed class CapacityLoot : IMonsterDropDomainService
    {
        public ValueTask<IReadOnlyList<RolledDrop>> GenerateGroundAsync(int id, MonsterDropContext context, CancellationToken ct) =>
            ValueTask.FromResult<IReadOnlyList<RolledDrop>>(Enumerable.Range(0, 300).Select(i => new RolledDrop(
                2000 + i, 1, DropRarity.Common, DropItemKind.Stackable)).ToArray());
        public ValueTask<IReadOnlyList<RolledDrop>> GeneratePersonalAsync(int id, CharacterId character, MonsterDropContext context, CancellationToken ct) =>
            ValueTask.FromResult<IReadOnlyList<RolledDrop>>([]);
        public ValueTask<IReadOnlyList<RolledDrop>> GenerateIndependentAsync(int id, MonsterDropContext context, CancellationToken ct) => throw new NotSupportedException();
    }
}

public sealed partial class MonsterDropDomainServiceTests
{
    [Theory]
    [Trait("Category", "Unit")]
    [InlineData(254, 1)]
    [InlineData(255, 1)]
    [InlineData(256, 1)]
    [InlineData(100, 3)]
    public async Task Independent_capacity_retains_round_order_and_deterministic_prefix(int perRound, int multiplier)
    {
        var independent = new CapacityIndependent(perRound);
        var service = Build(GoldAndEquipment(), independent: independent,
            multipliers: new FixedDrawMultipliers((DropDrawEntry.IndependentMonster, multiplier)));
        var context = Kill(Run());
        var result = await service.GenerateIndependentAsync(17, context, default);
        Assert.Equal(Enumerable.Range(0, multiplier).SelectMany(round => Enumerable.Range(0, perRound)
            .Select(i => 1000 + round * 1000 + i)).Take(255), result.Select(x => x.ItemId));
        Assert.Equal(result, await service.GenerateIndependentAsync(17, context, default));
    }

    private sealed class CapacityIndependent(int count) : IIndependentDropRuleService
    {
        public int Initialize() => 1;
        public bool Covers(MonsterDropRequest request) => true;
        public IReadOnlyList<RolledDrop> Draw(MonsterDropRequest request, int level, int bonus) =>
            Enumerable.Range(0, count).Select(i => new RolledDrop(1000 + request.Draw.Round * 1000 + i,
                1, DropRarity.Common, DropItemKind.Stackable)).ToArray();
    }
}

internal sealed class CapacityLogger<T> : ILogger<T>
{
    public List<string> Warnings { get; } = [];
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel level) => true;
    public void Log<TState>(LogLevel level, EventId id, TState state, Exception? error, Func<TState, Exception?, string> formatter)
    {
        if (level == LogLevel.Warning) Warnings.Add(formatter(state, error));
    }
}
