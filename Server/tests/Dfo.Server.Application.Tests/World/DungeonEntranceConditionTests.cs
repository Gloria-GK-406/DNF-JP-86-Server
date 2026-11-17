using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;
using Dfo.Server.Testing;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dfo.Server.Application.Tests.World;

/// <summary>Boss room entrance conditions: placed residents, their hunt, and the summon they unlock (#357).</summary>
[Trait("Category", "Unit")]
public sealed class DungeonEntranceConditionTests
{
    private static readonly AccountId Account = new(1);
    private static readonly CharacterId Leader = new(1);
    private static readonly MazeCell Room = new(0, 0);
    private static readonly MazeCell Elsewhere = new(1, 0);
    private const int Resident = 56611;
    private const int Queen = 69264;
    private const int MapId = 17110;

    [Fact]
    public void Each_target_must_die_in_the_room_it_was_placed_in()
    {
        var rules = new DungeonSettlementRuleService();
        var plan = new DungeonEntrancePlan(Queen, [new(Resident, Room, MapId), new(56617, Elsewhere, 17111)]);
        Assert.False(rules.IsEntranceConditionMet(plan, [new(Resident, Elsewhere), new(56617, Elsewhere)]));
        Assert.False(rules.CompletesEntranceCondition(plan, [new(56617, Elsewhere)], new(Resident, Elsewhere)));
        Assert.True(rules.CompletesEntranceCondition(plan, [new(56617, Elsewhere)], new(Resident, Room)));
        Assert.False(rules.CompletesEntranceCondition(plan, [new(56617, Elsewhere), new(Resident, Room)], new(Resident, Room)));
    }

    [Fact]
    public void Identical_targets_in_identical_rooms_each_need_their_own_kill()
    {
        var rules = new DungeonSettlementRuleService();
        var plan = new DungeonEntrancePlan(56616, [new(61574, Room, MapId), new(61574, Elsewhere, 17111)]);
        Assert.False(rules.IsEntranceConditionMet(plan, [new(61574, Room), new(61574, Room)]));
        Assert.True(rules.IsEntranceConditionMet(plan, [new(61574, Room), new(61574, Elsewhere)]));
    }

    [Fact]
    public async Task A_placed_resident_joins_only_its_own_room_before_the_AI_characters()
    {
        var source = new EntranceRoomSource();
        var rooms = Rooms(source);
        var layout = Layout();
        var target = await rooms.DescribeAsync(new(DungeonRunId.New(), layout, Room, MapId, DungeonDifficulty.Normal,
            DateTimeOffset.UnixEpoch, []), default);
        Assert.Equal([65028, Resident, 461], target.Actors.Select(actor => actor.Actor.Code));
        Assert.True(target.Actors[1].Actor.IsHostile);
        Assert.Equal((byte)29, target.Actors[1].Actor.Level);

        var other = await rooms.DescribeAsync(new(DungeonRunId.New(), layout, Elsewhere, MapId, DungeonDifficulty.Normal,
            DateTimeOffset.UnixEpoch, []), default);
        Assert.Equal([65028, 461], other.Actors.Select(actor => actor.Actor.Code));
    }

    [Fact]
    public async Task The_summon_comes_from_the_maps_conditional_summon_row_at_the_dungeon_level()
    {
        var summons = await Rooms(new EntranceRoomSource()).DescribeConditionalSummonsAsync(Layout(), MapId, Queen, default);
        var queen = Assert.Single(summons);
        Assert.Equal((Queen, (byte)3, (byte)29), (queen.Code, queen.ActorType, queen.Level));
    }

    [Fact]
    public async Task The_leader_summons_once_after_every_resident_is_hunted()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var scope = TestRequired.TransactionScope();
            var runs = TestParties.Runs(scope);
            var seed = new DungeonCellSeed(Room, MapId, 100,
                [new(100, new DungeonRoomActor(Resident, 29, 0), IsAiCharacter: false, IsDead: false, PendingLoot: [])], []);
            var run = runs.PreparePartyEntry([new(Account, Leader, 0, true)], Layout(), DungeonDifficulty.Normal, seed,
                DungeonRunId.New(), DateTimeOffset.UnixEpoch).Run;
            var service = new DungeonEntranceDomainService(runs, new DungeonSettlementRuleService(),
                Rooms(new EntranceRoomSource()), new NoLoot(), new FixedDrawMultipliers(), NullLogger<DungeonEntranceDomainService>.Instance);
            var leader = SelectedSessionIdentity.Create(Account, Leader, new Dfo.Server.Data.Networking.NetworkConnectionId("entrance"), 1, 10011);
            var request = new DungeonEntranceSummonRequest(0, Queen, 7, MapId, 0, 0, 1);

            await using (var tx = scope.Begin())
            {
                Assert.False((await service.SummonAsync(leader, request, default)).Accepted);
                await tx.CommitAsync(default);
            }
            Assert.NotNull(runs.CommitActorDeathChange(runs.TryClaimActorDeath(Leader, 100)!));

            DungeonEntranceSummonResult summoned;
            await using (var tx = scope.Begin())
            {
                Assert.False((await service.SummonAsync(leader, request with { MapId = 1 }, default)).Accepted);
                summoned = await service.SummonAsync(leader, request, default);
                await tx.CommitAsync(default);
            }
            var actor = Assert.Single(summoned.Actors);
            Assert.Equal(7, summoned.RequestId);
            Assert.Equal((Queen, (byte)3, (ushort)29), (actor.MonsterCode, actor.Grade, actor.Level));
            Assert.NotEqual((ushort)100, actor.Sequence);
            var standing = runs.Find(Leader)!.CurrentCellState.FindActor(actor.Sequence);
            Assert.NotNull(standing);
            Assert.True(standing!.BlocksClear);

            await using (var tx = scope.Begin())
            {
                Assert.False((await service.SummonAsync(leader, request, default)).Accepted);
                await tx.CommitAsync(default);
            }
        });
    }

    private static DungeonLayout Layout() =>
        new(2010, 0, 2, 1, Room, Elsewhere, MapId, 7,
            new Dictionary<MazeCell, DungeonCellDefinition>
            {
                [Room] = DungeonCellDefinition.Fixed(MapId),
                [Elsewhere] = DungeonCellDefinition.Fixed(MapId),
            }, BasisLevel: 29)
        { EntrancePlan = new(Queen, [new(Resident, Room, MapId)]) };

    private static DungeonRoomRuleService Rooms(EntranceRoomSource source) =>
        new(source, source, source, source, mechanisms: new DungeonRoomMechanismRuleService());

    private sealed class EntranceRoomSource : IDungeonRoomActorSource, IDungeonRoomDropSource, IObjectDropRuleService,
        IDungeonRoomObjectSource
    {
        private static readonly DungeonActorSpawnDeclaration Ordinary = new(65028, DungeonActorSpawnSource.Monster, 0)
        { RelativeLevel = true, LevelValue = 0 };
        private static readonly DungeonActorSpawnDeclaration Apc = new(461, DungeonActorSpawnSource.AiCharacter, 0)
        { LevelValue = 30 };

        public ValueTask<IReadOnlyList<DungeonRoomActor>> GetAsync(int id, CancellationToken ct) =>
            ValueTask.FromResult<IReadOnlyList<DungeonRoomActor>>(
            [
                new(65028, 0, 0, 0) { Declaration = Ordinary },
                new(461, 30, 5) { Declaration = Apc, IsHostile = false },
            ]);

        public ValueTask<DungeonMapActorDeclarations> GetDeclarationsAsync(int mapId, CancellationToken ct) =>
            ValueTask.FromResult(new DungeonMapActorDeclarations(
            [
                Ordinary,
                new(Resident, DungeonActorSpawnSource.ConditionalMonster, 0) { RelativeLevel = true, LevelValue = 0 },
                new(56617, DungeonActorSpawnSource.ConditionalMonster, 1) { RelativeLevel = true, LevelValue = 0 },
                new(Queen, DungeonActorSpawnSource.ConditionalSummon, 0) { RelativeLevel = true, LevelValue = 0, Rank = 3 },
                Apc,
            ], []));

        public ValueTask<IReadOnlyList<ObjectDropDefinition>> GetAsync(int dungeon, int map, CancellationToken ct) =>
            ValueTask.FromResult<IReadOnlyList<ObjectDropDefinition>>([]);
        public IReadOnlyList<DungeonRoomDrop> Draw(IReadOnlyList<ObjectDropDefinition> definitions, DungeonCellCreationContext context) => [];
        public ValueTask<IReadOnlyList<DungeonRoomObject>> GetObjectsAsync(int map, CancellationToken ct) =>
            ValueTask.FromResult<IReadOnlyList<DungeonRoomObject>>([]);
    }

    private sealed class NoLoot : IMonsterDropDomainService
    {
        public ValueTask<IReadOnlyList<RolledDrop>> GenerateIndependentAsync(int monsterId, MonsterDropContext context,
            int growthBonusPercent, CancellationToken ct) => ValueTask.FromResult<IReadOnlyList<RolledDrop>>([]);
        public ValueTask<IReadOnlyList<RolledDrop>> GeneratePersonalAsync(int monsterId, CharacterId characterId,
            MonsterDropContext context, CancellationToken ct) => ValueTask.FromResult<IReadOnlyList<RolledDrop>>([]);
        public ValueTask<IReadOnlyList<RolledDrop>> GenerateGroundAsync(int monsterId, MonsterDropContext context,
            CancellationToken ct) => ValueTask.FromResult<IReadOnlyList<RolledDrop>>([]);
    }
}
