using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Guilds;
using Dfo.Server.Data.Guilds;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.World;

public sealed class DungeonClearDomainServiceTests
{
    private static readonly AccountId LeaderAccount = new(10);
    private static readonly CharacterId Leader = new(100);
    private static readonly AccountId HelperAccount = new(20);
    private static readonly CharacterId Helper = new(200);

    [Fact]
    public async Task Tournament_room_clear_never_opens_the_ordinary_result_screen()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var definition = new TournamentDefinition(120, 17100, 69, 72, 2, 2, [], [], [], [], [], 16);
            var layout = BossLayout() with { Mode = new DungeonRunMode.Tournament(new(definition, [], [], 1)) };
            var runs = TestParties.Runs();
            var run = runs.Begin(Leader, layout, difficulty: 0);
            var service = new DungeonClearDomainService(runs, new MutableRoomCleared { Cleared = true }, new DungeonSettlementRuleService(), TestRequired.Absent<IGuildMembershipQueryDomainService>());
            Assert.Null(await service.TryOpenClearAsync(LeaderAccount, Leader, run.RunId, run.RoomVersion, default));
            Assert.False(runs.Find(Leader)!.ClearOpen);
        });
    }

    [Fact]
    public async Task Enemy_APC_death_does_not_clear_a_room_while_its_APC_boss_is_alive()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var runs = TestParties.Runs();
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
            var service = new DungeonClearDomainService(runs, new MonsterKillDomainService(), new DungeonSettlementRuleService(), TestRequired.Absent<IGuildMembershipQueryDomainService>());
            Assert.False(run.IsCleared);
            var first = runs.CommitActorDeathChange(runs.TryClaimActorDeath(Leader, 101)!);
            Assert.NotNull(first);
            Assert.Empty(first.AutoKilledActorSequences);
            Assert.False(first.IsCellCleared);
            Assert.Null(await service.TryOpenClearAsync(LeaderAccount, Leader, run.RunId, run.RoomVersion, default));
            var boss = runs.CommitActorDeathChange(runs.TryClaimActorDeath(Leader, 100)!);
            Assert.NotNull(boss);
            Assert.Equal(new ushort[] { 102 }, boss.AutoKilledActorSequences);
            Assert.True(boss.IsCellCleared);
            Assert.False(boss.Run.CurrentCellState.FindActor(103)!.IsDead);
            Assert.False(boss.Run.CurrentCellState.FindActor(104)!.IsDead);
            Assert.Equal(0, boss.Run.KillSummary.Boss); // APCs do not enter the ordinary XP ledger.
            Assert.NotNull(await service.TryOpenClearAsync(LeaderAccount, Leader, run.RunId, run.RoomVersion, default));
            Assert.Null(await service.TryOpenClearAsync(LeaderAccount, Leader, run.RunId, run.RoomVersion, default));
        });
    }

    [Fact]
    public async Task Every_hostile_boss_must_die_before_remaining_enemies_are_suppressed()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var runs = TestParties.Runs();
            var layout = BossLayout();
            var run = runs.PreparePartyEntry([new(LeaderAccount, Leader, 0, true)], layout, 0,
                new DungeonCellSeed(layout.StartCell, layout.StartRoomMapId, 100,
                [Apc(100, 14003, 8, true), Apc(101, 14003, 8, true), Apc(102, 10610, 5, true)], []),
                DungeonRunId.New(), DateTimeOffset.UnixEpoch).Run;
            var service = new DungeonClearDomainService(runs, new MonsterKillDomainService(), new DungeonSettlementRuleService(), TestRequired.Absent<IGuildMembershipQueryDomainService>());
            var first = runs.CommitActorDeathChange(runs.TryClaimActorDeath(Leader, 100)!);
            Assert.NotNull(first);
            Assert.Empty(first.AutoKilledActorSequences);
            Assert.False(first.Run.CurrentCellState.FindActor(101)!.IsDead);
            Assert.False(first.Run.CurrentCellState.FindActor(102)!.IsDead);
            Assert.Null(await service.TryOpenClearAsync(LeaderAccount, Leader, run.RunId, run.RoomVersion, default));
            var last = runs.CommitActorDeathChange(runs.TryClaimActorDeath(Leader, 101)!);
            Assert.NotNull(last);
            Assert.Equal(new ushort[] { 102 }, last.AutoKilledActorSequences);
            Assert.NotNull(await service.TryOpenClearAsync(LeaderAccount, Leader, run.RunId, run.RoomVersion, default));
        });
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
        await TestGameplay.RunAsync(async () =>
        {
            var runs = TestParties.Runs();
            var run = runs.Begin(
                Leader,
                BossLayout() with
                {
                    Purpose = DungeonRunPurpose.QuestLinked(
                        1781, DungeonQuestConnectionState.Active),
                },
                difficulty: 0);
            var service = new DungeonClearDomainService(
                runs, new MutableRoomCleared { Cleared = true }, new DungeonSettlementRuleService(), TestRequired.Absent<IGuildMembershipQueryDomainService>());

            var opened = await service.TryOpenClearAsync(
                LeaderAccount, Leader, run.RunId, run.RoomVersion, default);

            Assert.NotNull(opened);
            Assert.True(runs.Find(Leader)!.ClearOpen);
        });
    }

    [Fact]
    public async Task Boss_clear_opens_only_after_the_room_is_cleared()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var runs = TestParties.Runs();
            var run = runs.Begin(Leader, BossLayout(), difficulty: 0);
            var room = new MutableRoomCleared();
            var service = new DungeonClearDomainService(runs, room, new DungeonSettlementRuleService(), TestRequired.Absent<IGuildMembershipQueryDomainService>());

            Assert.Null(await service.TryOpenClearAsync(
                LeaderAccount, Leader, run.RunId, run.RoomVersion, default));
            room.Cleared = true;

            var opened = await service.TryOpenClearAsync(
                LeaderAccount, Leader, run.RunId, run.RoomVersion, default);

            Assert.NotNull(opened);
            Assert.True(runs.Find(Leader)!.ClearOpen);
            Assert.Null(await service.TryOpenClearAsync(
                LeaderAccount, Leader, run.RunId, run.RoomVersion, default));
        });
    }

    [Fact]
    public async Task Party_helper_can_open_the_shared_clear()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var runs = TestParties.Runs();
            var run = runs.BeginParty(
                [
                    new DungeonRunParticipant(LeaderAccount, Leader, 0, IsLeader: true),
                    new DungeonRunParticipant(HelperAccount, Helper, 1, IsLeader: false),
                ],
                BossLayout(),
                difficulty: 0);
            var service = new DungeonClearDomainService(
                runs, new MutableRoomCleared { Cleared = true }, new DungeonSettlementRuleService(), TestRequired.Absent<IGuildMembershipQueryDomainService>());

            var opened = await service.TryOpenClearAsync(
                HelperAccount, Helper, run.RunId, run.RoomVersion, default);

            Assert.NotNull(opened);
            Assert.Equal([Leader, Helper], opened.Recipients);
        });
    }

    [Fact]
    public async Task Ordinary_room_and_stale_room_version_cannot_open_the_clear()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var runs = TestParties.Runs();
            var ordinary = runs.Begin(Leader, OrdinaryLayout(), difficulty: 0);
            var service = new DungeonClearDomainService(
                runs, new MutableRoomCleared { Cleared = true }, new DungeonSettlementRuleService(), TestRequired.Absent<IGuildMembershipQueryDomainService>());

            Assert.Null(await service.TryOpenClearAsync(
                LeaderAccount, Leader, ordinary.RunId, ordinary.RoomVersion, default));
            Assert.Null(await service.TryOpenClearAsync(
                LeaderAccount, Leader, ordinary.RunId, ordinary.RoomVersion + 1, default));
        });
    }

    [Theory]
    [InlineData(false, false, false, true, true)]
    [InlineData(true, false, false, false, false)]
    [InlineData(true, false, true, false, false)]
    [InlineData(false, true, false, true, false)]
    [InlineData(false, true, true, true, false)]
    public async Task Ordinary_clear_freezes_each_membership_for_monthly_and_party_contributions(
        bool leaderRejoined, bool helperRejoined, bool switchedGuild, bool earnsMonthly, bool earnsParty)
    {
        await TestGameplay.RunAsync(async () =>
        {
            var settledAt = new DateTimeOffset(2027, 1, 1, 6, 0, 0, TimeSpan.FromHours(8));
            var joinedAt = settledAt.AddSeconds(-1);
            var runs = TestParties.Runs(clock: new SettlementClock(joinedAt));
            var run = runs.BeginParty([new(LeaderAccount, Leader, 0, true), new(HelperAccount, Helper, 1, false)],
                BossLayout(), difficulty: 0);
            var guild = new Guild(new(1), "guild", "", 1);
            var members = new Dictionary<CharacterId, GuildMembership>
            {
                [Leader] = new(guild, new(guild.Id, Leader, true, joinedAt) { MembershipId = 31 }),
                [Helper] = new(guild, new(guild.Id, Helper, false, joinedAt) { MembershipId = 32 }),
            };
            object? Lookup(System.Reflection.MethodInfo method, object?[]? args) =>
                method.Name == nameof(IGuildMembershipQueryDomainService.FindMembershipAsync)
                    ? ValueTask.FromResult<GuildMembership?>(members[(CharacterId)args![0]!]) : null;
            var clear = new DungeonClearDomainService(runs, new MutableRoomCleared { Cleared = true },
                new DungeonSettlementRuleService(), TestRequired.Absent<IGuildMembershipQueryDomainService>(Lookup));
            Assert.NotNull(await clear.TryOpenClearAsync(LeaderAccount, Leader, run.RunId, run.RoomVersion, default));
            var completed = runs.Find(Leader)!;
            Assert.Equal([31L, 32L], completed.ClearParticipants.Select(p => p.MembershipId!.Value));
            Assert.Equal(joinedAt, completed.ClearedAt);
            var nextGuild = switchedGuild ? guild with { Id = new(2) } : guild;
            if (leaderRejoined)
                members[Leader] = new(nextGuild, members[Leader].Member with { Guild = nextGuild.Id, MembershipId = 33 });
            if (helperRejoined)
                members[Helper] = new(nextGuild, members[Helper].Member with { Guild = nextGuild.Id, MembershipId = 34 });

            var contributions = new List<DateTimeOffset>();
            var store = TestRequired.Absent<IGuildDomainStore>(Lookup);
            var delivery = TestRequired.Absent<IItemDeliveryDomainService>((method, _) =>
                method.Name == nameof(IItemDeliveryDomainService.DeliverAsync)
                    ? ValueTask.FromResult(new ItemDeliveryReceipt([], [], 1)) : null);
            var monthly = new GuildMonthlyContributionDomainService(store,
                TestRequired.Absent<IGuildMonthlyContributionStore>((method, args) =>
                {
                    if (method.Name != nameof(IGuildMonthlyContributionStore.RecordAsync)) return null;
                    contributions.Add((DateTimeOffset)args![5]!);
                    return ValueTask.FromResult(true);
                }), new(TestRequired.Absent<IGuildEconomySource>((_, _) => new GuildMonthlyContributionRule(10, 1100, 1, 1)),
                TestRequired.Absent<IDungeonDefinitionSource>((_, _) =>
                    ValueTask.FromResult<DungeonDefinition?>(new(144, "test", 1, true, []) { RecommendedLevels = [1, 10] }))),
                delivery, new SettlementClock(settledAt));
            await monthly.RecordClearAsync(Leader, completed, 5, default);
            Assert.Equal(earnsMonthly ? new[] { settledAt } : [], contributions);

            var party = new GuildContributionDomainService(store,
                TestRequired.Absent<IGuildContributionStore>((method, _) =>
                    method.Name == nameof(IGuildContributionStore.TryRecordClearAsync) ? ValueTask.FromResult(true) : null),
                new(TestRequired.Absent<IGuildRedPacketRuleSource>((_, _) => new GuildRedPacketRules(
                    new Dictionary<int, GuildRedPacketProduct>(), 1, 1, 1) { PartyDungeonContribution = new(2, 1) })),
                delivery, TimeProvider.System);
            var award = await party.AwardClearAsync(LeaderAccount, Leader, completed, settledAt, default);
            Assert.Equal(earnsParty, award is not null);
        });
    }

    private sealed class SettlementClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
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
