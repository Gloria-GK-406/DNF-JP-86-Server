using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Tests.World;

public sealed class DungeonClearDomainServiceTests
{
    private static readonly AccountId LeaderAccount = new(10);
    private static readonly CharacterId Leader = new(100);
    private static readonly AccountId HelperAccount = new(20);
    private static readonly CharacterId Helper = new(200);

    [Fact]
    public async Task Enemy_APC_death_does_not_clear_a_room_while_its_APC_boss_is_alive()
    {
        var runs = new DungeonRunDomainService();
        var layout = BossLayout();
        var seed = new DungeonCellSeed(layout.StartCell, layout.StartRoomMapId, 100,
        [
            Apc(100, 14003, 8, hostile: true),
            Apc(101, 10610, 5, hostile: true),
            Apc(102, 10610, 5, hostile: true),
            Apc(103, 461, 5, hostile: false),
            Apc(104, 20444, 8, hostile: true) with { IsHellTemplate = true, HellGroupIndex = 1 },
        ], []) { HellGroups = [new(1, [104], [])] };
        var run = runs.PreparePartyEntry([new(LeaderAccount, Leader, 0, true)], layout, 0,
            seed, DungeonRunId.New(), DateTimeOffset.UnixEpoch).Run;
        var service = new DungeonClearDomainService(runs, new MonsterKillDomainService());
        Assert.False(run.IsCleared);
        var first = runs.CommitActorDeathChange(runs.TryClaimActorDeath(Leader, 101)!);
        Assert.NotNull(first);
        Assert.Empty(first.AutoKilledActorSequences);
        Assert.False(first.IsCellCleared);
        Assert.Null(await service.EvaluateAsync(LeaderAccount, Leader, run.RunId, run.RoomVersion, default));
        var boss = runs.CommitActorDeathChange(runs.TryClaimActorDeath(Leader, 100)!);
        Assert.NotNull(boss);
        Assert.Equal(new ushort[] { 102 }, boss.AutoKilledActorSequences);
        Assert.True(boss.IsCellCleared);
        Assert.False(boss.Run.CurrentCellState.FindActor(103)!.IsDead);
        Assert.False(boss.Run.CurrentCellState.FindActor(104)!.IsDead);
        Assert.Equal(0, boss.Run.KillSummary.Boss); // APCs do not enter the ordinary XP ledger.
        Assert.NotNull(await service.EvaluateAsync(LeaderAccount, Leader, run.RunId, run.RoomVersion, default));
        Assert.Null(await service.EvaluateAsync(LeaderAccount, Leader, run.RunId, run.RoomVersion, default));
    }

    [Fact]
    public async Task Every_hostile_boss_must_die_before_remaining_enemies_are_suppressed()
    {
        var runs = new DungeonRunDomainService();
        var layout = BossLayout();
        var run = runs.PreparePartyEntry([new(LeaderAccount, Leader, 0, true)], layout, 0,
            new DungeonCellSeed(layout.StartCell, layout.StartRoomMapId, 100,
            [Apc(100, 14003, 8, true), Apc(101, 14003, 8, true), Apc(102, 10610, 5, true)], []),
            DungeonRunId.New(), DateTimeOffset.UnixEpoch).Run;
        var service = new DungeonClearDomainService(runs, new MonsterKillDomainService());
        var first = runs.CommitActorDeathChange(runs.TryClaimActorDeath(Leader, 100)!);
        Assert.NotNull(first);
        Assert.Empty(first.AutoKilledActorSequences);
        Assert.False(first.Run.CurrentCellState.FindActor(101)!.IsDead);
        Assert.False(first.Run.CurrentCellState.FindActor(102)!.IsDead);
        Assert.Null(await service.EvaluateAsync(LeaderAccount, Leader, run.RunId, run.RoomVersion, default));
        var last = runs.CommitActorDeathChange(runs.TryClaimActorDeath(Leader, 101)!);
        Assert.NotNull(last);
        Assert.Equal(new ushort[] { 102 }, last.AutoKilledActorSequences);
        Assert.NotNull(await service.EvaluateAsync(LeaderAccount, Leader, run.RunId, run.RoomVersion, default));
    }

    [Fact]
    public void Ordinary_APC_rooms_wait_for_enemies_and_ignore_friendly_and_hell_actors()
    {
        Assert.False((Apc(99, 461, 5, false) with { IsMonster = true }).BlocksClear);
        var cell = new DungeonCellState(new(0, 0), 57323, 0, 100,
            [Apc(100, 10610, 5, true), Apc(101, 461, 5, false),
                Apc(102, 20444, 5, true) with { IsHellTemplate = true }], []);
        Assert.False(cell.IsCleared);
        Assert.True((cell with { Actors = cell.Actors.Select(actor => actor.Sequence == 100
            ? actor with { IsDead = true } : actor).ToArray() }).IsCleared);
    }

    private static DungeonCellActorState Apc(ushort sequence, int code, byte type, bool hostile) =>
        new(sequence, new DungeonRoomActor(code, 60, type) { IsHostile = hostile },
            IsAiCharacter: true, IsDead: false, PendingLoot: [], IsMonster: false);

    [Fact]
    public async Task Verified_boss_room_clear_does_not_wait_for_its_own_quest_trigger()
    {
        var runs = new DungeonRunDomainService();
        var run = runs.Begin(
            Leader,
            BossLayout() with
            {
                Purpose = DungeonRunPurpose.QuestLinked(
                    1781, DungeonQuestConnectionState.Active),
            },
            difficulty: 0);
        var service = new DungeonClearDomainService(
            runs, new MutableRoomCleared { Cleared = true });

        var opened = await service.EvaluateAsync(
            LeaderAccount, Leader, run.RunId, run.RoomVersion, default);

        Assert.NotNull(opened);
        Assert.True(runs.Find(Leader)!.ClearOpen);
    }

    [Fact]
    public async Task Boss_clear_opens_only_after_the_room_is_cleared()
    {
        var runs = new DungeonRunDomainService();
        var run = runs.Begin(Leader, BossLayout(), difficulty: 0);
        var room = new MutableRoomCleared();
        var service = new DungeonClearDomainService(runs, room);

        Assert.Null(await service.EvaluateAsync(
            LeaderAccount, Leader, run.RunId, run.RoomVersion, default));
        room.Cleared = true;

        var opened = await service.EvaluateAsync(
            LeaderAccount, Leader, run.RunId, run.RoomVersion, default);

        Assert.NotNull(opened);
        Assert.True(runs.Find(Leader)!.ClearOpen);
        Assert.Null(await service.EvaluateAsync(
            LeaderAccount, Leader, run.RunId, run.RoomVersion, default));
    }

    [Fact]
    public async Task Party_helper_can_open_the_shared_clear()
    {
        var runs = new DungeonRunDomainService();
        var run = runs.BeginParty(
            [
                new DungeonRunParticipant(LeaderAccount, Leader, 0, IsLeader: true),
                new DungeonRunParticipant(HelperAccount, Helper, 1, IsLeader: false),
            ],
            BossLayout(),
            difficulty: 0);
        var service = new DungeonClearDomainService(
            runs, new MutableRoomCleared { Cleared = true });

        var opened = await service.EvaluateAsync(
            HelperAccount, Helper, run.RunId, run.RoomVersion, default);

        Assert.NotNull(opened);
        Assert.Equal([Leader, Helper], opened.Recipients);
    }

    [Fact]
    public async Task Ordinary_room_and_stale_room_version_cannot_open_the_clear()
    {
        var runs = new DungeonRunDomainService();
        var ordinary = runs.Begin(Leader, OrdinaryLayout(), difficulty: 0);
        var service = new DungeonClearDomainService(
            runs, new MutableRoomCleared { Cleared = true });

        Assert.Null(await service.EvaluateAsync(
            LeaderAccount, Leader, ordinary.RunId, ordinary.RoomVersion, default));
        Assert.Null(await service.EvaluateAsync(
            LeaderAccount, Leader, ordinary.RunId, ordinary.RoomVersion + 1, default));
    }

    private static DungeonLayout BossLayout() => new(
        DungeonId: 144,
        MazeIndex: 0,
        Width: 1,
        Height: 1,
        StartCell: new MazeCell(0, 0),
        BossCell: new MazeCell(0, 0),
        StartRoomMapId: 8153,
        Seed: 7,
        Cells: new Dictionary<MazeCell, DungeonCellDefinition> { [new MazeCell(0, 0)] = DungeonCellDefinition.Fixed(8153)});

    private static DungeonLayout OrdinaryLayout() => new(
        DungeonId: 144,
        MazeIndex: 0,
        Width: 2,
        Height: 1,
        StartCell: new MazeCell(0, 0),
        BossCell: new MazeCell(1, 0),
        StartRoomMapId: 8153,
        Seed: 7,
        Cells: new Dictionary<MazeCell, DungeonCellDefinition> {
            [new MazeCell(0, 0)] = DungeonCellDefinition.Fixed(8153),
            [new MazeCell(1, 0)] = DungeonCellDefinition.Fixed(8154),
        });

    private sealed class MutableRoomCleared : IMonsterKillDomainService
    {
        public bool Cleared { get; set; }

        public ValueTask<KillIdentification> IdentifyAsync(
            DungeonRunState run, ushort actorSequence, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask<bool> IsCurrentRoomClearedAsync(
            DungeonRunState run, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Cleared);
    }

}
