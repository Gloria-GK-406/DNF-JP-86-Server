using System.Reflection;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.World.BossTower;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Application.Authentication;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Creatures;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Parties;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Application.Users;
using Dfo.Server.Application.World;
using Dfo.Server.Application.World.BossTower;
using Dfo.Server.Application.World.IllusionTower;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;
using Dfo.Server.Persistence.Tests.Support;
using Dfo.Server.Protocol.Game.CardReward;
using Dfo.Server.Testing;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dfo.Server.Application.Tests.Execution;

/// <summary>
/// RQ-061, the half without the outbox (TASK-18): a write unit of work, the two bare write sites and every write
/// member of the appendix F "move onto the executor" types throw off the executor and report it; the same call in a
/// job runs; reads, and the appendix B exemptions, run anywhere.
/// </summary>
/// <remarks>
/// A counterexample class the zero-violation evidence subtracts: 22 write violations per run, one per off-executor
/// write below (Begin 1, ChangeAsync 2, the creature advance 1, the fourteen types 14, a finished job's continuation
/// 1, a cancelled caller 1, two tower members that require the caller's unit of work 2: 1 + 2 + 1 + 14 + 1 + 1 + 2 = 22),
/// plus one per write entry the TASK-30 census added (Application 10 + Protocol 7 + the ten daily refreshes 10 = 27,
/// the weakness handle's case reporting three writes: 27 + 2 = 29): 22 + 29 = 51. Every case throws before it
/// touches any state.
/// </remarks>
public sealed class GameplayWriteAssertionTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);
    private static readonly AccountId Account = new(7);
    private static readonly CharacterId Character = new(70);

    [Fact]
    public void A_write_unit_of_work_opened_off_the_executor_throws_reports_and_leaves_nothing_open()
    {
        var scope = TestRequired.TransactionScope();
        var seen = ViolationCapture.Attach();

        var thrown = Assert.Throws<GameplayOffExecutorException>(() => scope.Begin());

        Assert.Contains("Writing DomainTransactionScope.Begin must happen inside a gameplay job", thrown.Message,
            StringComparison.Ordinal);
        AssertOneViolation(seen, "DomainTransactionScope.Begin");
        Assert.Null(scope.Current);
    }

    [Fact]
    public async Task A_read_only_unit_of_work_opens_anywhere_and_a_write_one_opens_in_a_job()
    {
        var scope = TestRequired.TransactionScope();
        var seen = ViolationCapture.Attach();

        await using (scope.Begin(readOnly: true))
            Assert.True(scope.Current!.IsReadOnly);
        await TestGameplay.RunAsync(async () =>
        {
            await using (var write = scope.Begin())
                await write.CommitAsync(CancellationToken.None);
            await using (scope.Begin(readOnly: true)) Assert.True(scope.Current!.IsReadOnly);
        });

        Assert.Empty(seen);
    }

    [Fact]
    public async Task The_write_mode_read_snapshot_is_not_a_write_and_opens_off_the_executor()
    {
        // TASK-18 ruling 3: ReadSnapshotAsync keeps its write-mode transaction but goes through BeginSnapshot.
        var scope = TestRequired.TransactionScope();
        var seen = ViolationCapture.Attach();

        var writable = await new AmbientStoreAccess(scope).ReadSnapshotAsync(() =>
            ValueTask.FromResult(scope.Current is { IsReadOnly: false }));

        Assert.True(writable);
        Assert.Empty(seen);
        Assert.Null(scope.Current);
    }

    [Fact]
    public async Task A_volatile_change_off_the_executor_throws_and_changes_nothing_but_runs_in_a_job()
    {
        var access = new AmbientStoreAccess(new NoTransactions());
        var store = new VolatileItemDomainStore();
        var container = new ContainerId("off-executor");
        var seen = ViolationCapture.Attach();

        await Assert.ThrowsAsync<GameplayOffExecutorException>(async () =>
            await access.ChangeAsync(store, held => PutPotionAsync(held, container), CancellationToken.None));

        AssertOneViolation(seen, "AmbientStoreAccess.ChangeAsync");
        Assert.Null(await access.ReadAsync(store, held => held.GetAsync(container, 0, CancellationToken.None)));
        await TestGameplay.RunAsync(async () =>
            await access.ChangeAsync(store, held => PutPotionAsync(held, container), CancellationToken.None));
        Assert.NotNull(await access.ReadAsync(store, held => held.GetAsync(container, 0, CancellationToken.None)));
    }

    [Fact]
    public async Task Off_the_executor_the_assertion_comes_before_the_durable_refusal()
    {
        // The assertion sits before the old "outside a use case" refusal, so an off-executor change of durable
        // state is reported as the program error it is rather than as the business refusal.
        var access = new AmbientStoreAccess(new NoTransactions());
        var seen = ViolationCapture.Attach();

        await Assert.ThrowsAsync<GameplayOffExecutorException>(async () =>
            await access.ChangeAsync(new DurableOnly(), _ => ValueTask.CompletedTask, CancellationToken.None));

        AssertOneViolation(seen, "AmbientStoreAccess.ChangeAsync");
    }

    [Fact]
    public async Task A_position_save_without_a_use_case_is_refused_and_writes_nothing()
    {
        // Teardown opens its own step for this write; the persistence itself never opens a boundary.
        var store = new RecordingPositions();
        var persistence = new SelectedSessionPositionPersistence(store, new NoTransactions(), new StubEntryPlacementRuleService());
        var position = new CharacterPositionState(1, 2, 3, 4, 5);

        await TestGameplay.RunAsync(async () => await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await persistence.PersistAsync(Account, Character, position, CancellationToken.None)));

        Assert.Equal(0, store.Sessions);
    }

    [Fact]
    public async Task A_creature_advance_refuses_off_the_executor_and_without_a_use_case_on_it()
    {
        var service = Creatures(new NoTransactions());
        var session = Session();
        var seen = ViolationCapture.Attach();

        await Assert.ThrowsAsync<GameplayOffExecutorException>(async () =>
            await service.AdvanceAsync(session, CancellationToken.None));

        AssertOneViolation(seen, "CreatureRuntimeDomainService.AdvanceAsync");
        // On the executor the member passes its assertion, but the service never opens a boundary of its own.
        await TestGameplay.RunAsync(async () => await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await service.AdvanceAsync(session, CancellationToken.None)));
    }

    [Fact]
    public void OnlineStateRegistry_refuses_a_write_off_the_executor_and_reads_anywhere()
    {
        var registry = new OnlineStateRegistry(TestRequired.TransactionScope());
        var connection = new NetworkConnectionContext(new NetworkConnectionId("registry"), NetworkEndpointKind.Game, 10011);
        var seen = ViolationCapture.Attach();

        Assert.Throws<GameplayOffExecutorException>(() => registry.RegisterConnection(connection));

        AssertOneViolation(seen, "OnlineStateRegistry.RegisterConnection");
        Assert.False(registry.TryGetByConnection(connection.ConnectionId, out _));
        TestGameplay.Run(() => registry.RegisterConnection(connection));
        Assert.True(registry.TryGetByConnection(connection.ConnectionId, out _));
    }

    [Fact]
    public void LoginSessionRegistry_refuses_a_write_off_the_executor_and_reads_anywhere()
    {
        var registry = new LoginSessionRegistry(TestRequired.TransactionScope(), TimeProvider.System, new LoginSessionOptions(), onlineUsers: TestRequired.Absent<Dfo.Server.Abstractions.OnlineUsers.IOnlineUserDomainService>(), closer: TestRequired.Absent<Dfo.Server.Abstractions.Networking.INetworkConnectionCloser>());
        var connection = new NetworkConnectionId("login");
        var seen = ViolationCapture.Attach();

        Assert.Throws<GameplayOffExecutorException>(() => registry.Disconnected(connection));

        AssertOneViolation(seen, "LoginSessionRegistry.Disconnected");
        Assert.False(registry.IsSuperseded(connection));
        Assert.Null(registry.CaptureAuthority(connection));
        TestGameplay.Run(() => registry.Disconnected(connection));
    }

    [Fact]
    public void Dungeon_run_entry_service_refuses_a_write_off_the_executor_and_reads_anywhere()
    {
        var service = TestParties.Runs();
        var run = TestGameplay.Run(() => service.PreparePartyEntry(
            [new DungeonRunParticipant(Account, Character, 0, true)], Layout, DungeonDifficulty.Normal,
            DungeonCellSeed.CompleteEmpty(new MazeCell(0, 0), 10), DungeonRunId.New(), DateTimeOffset.UtcNow).Run);
        var seen = ViolationCapture.Attach();

        Assert.Throws<GameplayOffExecutorException>(() =>
            service.TryMove(Character, run.RunId, run.RoomVersion, new MazeCell(1, 0), DungeonCellSeed.CompleteEmpty(new MazeCell(1, 0), 11)));

        AssertOneViolation(seen, "EntryService.TryMove");
        Assert.Equal(new MazeCell(0, 0), service.Find(Character)!.CurrentCell);
        Assert.NotNull(TestGameplay.Run(() =>
            service.TryMove(Character, run.RunId, run.RoomVersion, new MazeCell(1, 0), DungeonCellSeed.CompleteEmpty(new MazeCell(1, 0), 11))));
        Assert.Equal(new MazeCell(1, 0), service.Find(Character)!.CurrentCell);
    }

    [Fact]
    public void CreatureRuntimeDomainService_refuses_a_write_off_the_executor_and_reads_anywhere()
    {
        var service = Creatures(new NoTransactions());
        var session = Session();
        var seen = ViolationCapture.Attach();

        Assert.Throws<GameplayOffExecutorException>(() => service.Enter(session));

        AssertOneViolation(seen, "CreatureRuntimeDomainService.Enter");
        Assert.Empty(service.DueSessions());
        TestGameplay.Run(() => service.Enter(session));
        Assert.Equal(session, Assert.Single(service.DueSessions()));
        Assert.False(service.IsEquipped(session, 1));
    }

    [Fact]
    public void PartyService_refuses_a_write_off_the_executor_and_reads_anywhere()
    {
        var parties = new PartyService(new StubPartyMemberStatusDomainService(), new PartyModeRuleService(),
            TimeProvider.System, EmptyPartyMemberSessionEndpointSource.Instance, new Dfo.Server.Application.World.OnlineCharacterLocationDomainService());
        var scope = new PartyDirectoryScope(11, 1, 1);
        var leader = new PartyCharacterSnapshot(Account, Character, 1, scope);
        var member = new PartyCharacterSnapshot(new AccountId(8), new CharacterId(80), 2, scope);
        var display = new PartyInviteDisplayState(0, 0, 0, 0);
        var seen = ViolationCapture.Attach();

        Assert.Throws<GameplayOffExecutorException>(() => parties.RequestInvite(leader, member, display));

        AssertOneViolation(seen, "PartyService.RequestInvite");
        Assert.Single(parties.FindParty(leader).Members);
        Assert.True(TestGameplay.Run(() => parties.RequestInvite(leader, member, display)).Succeeded);
        Assert.True(TestGameplay.Run(() => parties.AcceptInvite(member, leader)).Succeeded);
        Assert.Equal(2, parties.FindParty(leader).Members.Length);
    }

    [Fact]
    public void OnlineCharacterLocationDomainService_refuses_a_write_off_the_executor_and_reads_anywhere()
    {
        var towns = new OnlineCharacterLocationDomainService();
        var member = new TownAreaMember(Session(), new TownAreaKey(11, 1, 2),
            new CharacterPositionState(1, 2, 3, 4, 5), sharesTownPresence: true);
        var seen = ViolationCapture.Attach();

        Assert.Throws<GameplayOffExecutorException>(() => towns.Begin(member.Session, member.Area, member.Position, member.SharesTownPresence));

        AssertOneViolation(seen, "OnlineCharacterLocationDomainService.Begin");
        Assert.Null(towns.Find(Character));
        TestGameplay.Run(() => towns.Begin(member.Session, member.Area, member.Position, member.SharesTownPresence));
        Assert.Equal(member, towns.Find(Character));
    }

    [Fact]
    public async Task DungeonParticipationDomainService_refuses_a_write_off_the_executor_and_reads_anywhere()
    {
        var scope = TestRequired.TransactionScope();
        var participation = new DungeonParticipationDomainService(scope);
        var owner = new DungeonParticipationOwner(DungeonParticipationMode.Ordinary, Guid.NewGuid());
        await TestGameplay.RunAsync(async () =>
        {
            await using var claim = scope.Begin();
            Assert.True(participation.TryClaim([Character], owner));
            await claim.CommitAsync(CancellationToken.None);
        });
        var seen = ViolationCapture.Attach();

        Assert.Throws<GameplayOffExecutorException>(() => participation.ReleaseCommitted(Character, owner));

        AssertOneViolation(seen, "DungeonParticipationDomainService.ReleaseCommitted");
        Assert.Equal(owner, participation.Find(Character));
        TestGameplay.Run(() => participation.ReleaseCommitted(Character, owner));
        Assert.Null(participation.Find(Character));
    }

    [Fact]
    public async Task DungeonCardPresentationCoordinator_refuses_a_write_off_the_executor_and_lets_the_drain_and_disposal_run()
    {
        var coordinator = new DungeonCardPresentationCoordinator(null!, null!,
            NullLogger<DungeonCardPresentationCoordinator>.Instance, DungeonCardPresentationTiming.Production);
        var seen = ViolationCapture.Attach();

        Assert.Throws<GameplayOffExecutorException>(() => coordinator.Cancel(Character, DungeonRunId.New()));

        AssertOneViolation(seen, "DungeonCardPresentationCoordinator.Cancel");
        TestGameplay.Run(() => coordinator.Cancel(Character, DungeonRunId.New()));
        // Appendix B "producer enqueue side": the executor lifetime drains this producer off the executor.
        await coordinator.DrainAsync(CancellationToken.None).AsTask().WaitAsync(Patience);
        coordinator.Dispose();
        Assert.Single(seen);
    }

    [Fact]
    public async Task BossTowerDomainService_refuses_a_write_off_the_executor_and_reads_anywhere()
    {
        var scope = TestRequired.TransactionScope();
        var towers = new BossTowerDomainService(null!, null!, null!, null!, null!, null!, null!, null!, null!, null!,
            null!, scope, TimeProvider.System, logger: Microsoft.Extensions.Logging.Abstractions.NullLogger<Dfo.Server.Application.World.BossTower.BossTowerDomainService>.Instance, rewards: TestRequired.Absent<Dfo.Server.Abstractions.World.BossTower.IBossTowerRewardDomainService>(), events: TestRequired.Absent<Dfo.Server.Abstractions.DomainEvents.IEventService>(), participation: TestRequired.Absent<Dfo.Server.Abstractions.World.IDungeonParticipationDomainService>());
        var context = new TowerActionContext(Session(), Guid.NewGuid(), 1);
        var seen = ViolationCapture.Attach();

        await Assert.ThrowsAsync<GameplayOffExecutorException>(async () =>
            await towers.LoadAsync(context, CancellationToken.None));

        AssertOneViolation(seen, "BossTowerDomainService.LoadAsync");
        Assert.False(towers.IsInTower(Character));
        Assert.Empty(towers.FindReturnsDue(DateTimeOffset.UtcNow));
        await TestGameplay.RunAsync(async () =>
        {
            // On the executor the only remaining requirement is the caller's transaction: no violation, a plain refusal.
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await towers.LoadAsync(context, CancellationToken.None));
            Assert.Null(scope.Current);
            await using var transaction = scope.Begin();
            Assert.False((await towers.LoadAsync(context, CancellationToken.None)).Changed);
            await transaction.CommitAsync(CancellationToken.None);
        });
        Assert.Single(seen);
    }

    [Fact]
    public async Task IllusionTowerDomainService_refuses_a_write_off_the_executor_and_reads_anywhere()
    {
        var scope = TestRequired.TransactionScope();
        var towers = new IllusionTowerDomainService(null!, null!, null!, null!, null!, null!, null!, scope,
            TimeProvider.System, rewards: TestRequired.Absent<Dfo.Server.Abstractions.World.IllusionTower.IIllusionTowerRewardDomainService>(), towerInventory: null!, events: TestRequired.Absent<Dfo.Server.Abstractions.DomainEvents.IEventService>(), participation: TestRequired.Absent<Dfo.Server.Abstractions.World.IDungeonParticipationDomainService>());
        var context = new TowerActionContext(Session(), Guid.NewGuid(), 1);
        var seen = ViolationCapture.Attach();

        await Assert.ThrowsAsync<GameplayOffExecutorException>(async () =>
            await towers.LoadAsync(context, CancellationToken.None));

        AssertOneViolation(seen, "IllusionTowerDomainService.LoadAsync");
        Assert.False(towers.IsInTower(Character));
        await TestGameplay.RunAsync(async () =>
        {
            // On the executor the only remaining requirement is the caller's transaction: no violation, a plain refusal.
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await towers.LoadAsync(context, CancellationToken.None));
            Assert.Null(scope.Current);
            await using var transaction = scope.Begin();
            Assert.False((await towers.LoadAsync(context, CancellationToken.None)).Changed);
            await transaction.CommitAsync(CancellationToken.None);
        });
        Assert.Single(seen);
    }

    [Fact]
    public void TournamentDungeonDomainService_refuses_a_write_off_the_executor_and_reads_anywhere()
    {
        var tournaments = new TournamentDungeonDomainService(null!, null!, null!, null!, null!, null!, null!, null!, null!,
            new NoTransactions(), TimeProvider.System, null!, null!, null!, null!);
        var runId = DungeonRunId.New();
        var seen = ViolationCapture.Attach();

        Assert.Throws<GameplayOffExecutorException>(() => tournaments.StageRevival(Character, runId));

        AssertOneViolation(seen, "TournamentDungeonDomainService.StageRevival");
        Assert.False(tournaments.CanRevive(Character, runId));
        Assert.Empty(tournaments.PendingRewards(DateTimeOffset.UtcNow));
        Assert.False(TestGameplay.Run(() => tournaments.StageRevival(Character, runId)));
    }

    [Fact]
    public void AltarDungeonEngine_refuses_a_write_off_the_executor_and_reads_anywhere()
    {
        var engine = new AltarDungeonEngine(null!, null!, null!, null!, TestParties.Runs(), null!, null!,
            null!, null!, null!, null!, null!, new NoTransactions(), TimeProvider.System, null!);
        var store = new AltarStateStore(new NoDeadlineWake());
        var runId = DungeonRunId.New();
        var seen = ViolationCapture.Attach();

        Assert.Throws<GameplayOffExecutorException>(() => engine.StageRevival(store, Character, runId));

        AssertOneViolation(seen, "AltarDungeonEngine.StageRevival");
        Assert.False(engine.CanRevive(store, Character, runId));
        Assert.False(TestGameplay.Run(() => engine.StageRevival(store, Character, runId)));
    }

    [Fact]
    public void AltarStateStore_refuses_a_write_off_the_executor_and_reads_anywhere()
    {
        // Not in appendix F by name: the altar state lives here, and AltarDungeonDomainService.HandleAsync removes
        // from it directly, outside every AltarDungeonEngine member (TASK-18 section 2.3 check).
        var store = new AltarStateStore(new NoDeadlineWake());
        var runId = DungeonRunId.New();
        var seen = ViolationCapture.Attach();

        Assert.Throws<GameplayOffExecutorException>(() => store.Remove(runId));

        AssertOneViolation(seen, "AltarStateStore.Remove");
        Assert.Null(store.Find(runId));
        Assert.Empty(store.All());
        TestGameplay.Run(() => store.Remove(runId));
    }

    [Fact]
    public void OnlineCharacterRuntimeDomainService_refuses_a_write_off_the_executor_and_reads_anywhere()
    {
        var runtime = new OnlineCharacterRuntimeDomainService(TimeProvider.System, new OnlineCharacterLocationDomainService());
        var session = Session();
        var position = new CharacterPositionState(1, 2, 3, 4, 5);
        var seen = ViolationCapture.Attach();

        Assert.Throws<GameplayOffExecutorException>(() => runtime.Begin(session, position, 100));

        AssertOneViolation(seen, "OnlineCharacterRuntimeDomainService.Begin");
        Assert.Null(runtime.PositionOf(session));
        TestGameplay.Run(() => runtime.Begin(session, position, 100));
        Assert.Equal(position, runtime.PositionOf(session));
        Assert.Equal(100, runtime.HpPercentOf(Character));
    }

    [Fact]
    public async Task A_continuation_that_outlived_its_job_writes_as_if_off_the_executor()
    {
        var towns = new OnlineCharacterLocationDomainService();
        var member = new TownAreaMember(Session(), new TownAreaKey(11, 1, 2),
            new CharacterPositionState(1, 2, 3, 4, 5), sharesTownPresence: true);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var seen = new System.Collections.Concurrent.ConcurrentQueue<GameplayAssertionViolation>();
        // Not suppressed: the continuation carries the job's token, which is inactive once the job has ended.
        var leaked = await TestGameplay.RunAsync(() => Task.FromResult(Task.Run(async () =>
        {
            await release.Task;
            ViolationCapture.Attach(seen);
            towns.Begin(member.Session, member.Area, member.Position, member.SharesTownPresence);
        })));
        release.TrySetResult();

        await Assert.ThrowsAsync<GameplayOffExecutorException>(() => leaked.WaitAsync(Patience));

        AssertOneViolation(seen, "OnlineCharacterLocationDomainService.Begin");
        Assert.Null(towns.Find(Character));
    }

    [Fact]
    public void A_cancelled_caller_is_still_reported_as_writing_off_the_executor()
    {
        // Unlike the gates, whose old WaitAsync looked at the token first, a write member never checked it, so
        // the assertion does not either: the violation is reported whatever the caller's token says.
        var runtime = new OnlineCharacterRuntimeDomainService(TimeProvider.System, new OnlineCharacterLocationDomainService());
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var seen = ViolationCapture.Attach();

        Assert.Throws<GameplayOffExecutorException>(() => runtime.StartCooldown(Session(), new ItemCooldownKey(false, 1), TimeSpan.FromSeconds(1)));

        AssertOneViolation(seen, "OnlineCharacterRuntimeDomainService.StartCooldown");
        Assert.True(cancelled.IsCancellationRequested);
    }

    [Fact]
    public async Task A_member_that_requires_a_unit_of_work_reports_the_member_off_the_executor()
    {
        // The tower members require the caller's unit of work in Execute; their own assertion comes first, so the
        // report names the member rather than the missing transaction.
        var scope = TestRequired.TransactionScope();
        var towers = new BossTowerDomainService(null!, null!, null!, null!, null!, null!, null!, null!, null!, null!,
            null!, scope, TimeProvider.System, logger: Microsoft.Extensions.Logging.Abstractions.NullLogger<Dfo.Server.Application.World.BossTower.BossTowerDomainService>.Instance, rewards: TestRequired.Absent<Dfo.Server.Abstractions.World.BossTower.IBossTowerRewardDomainService>(), events: TestRequired.Absent<Dfo.Server.Abstractions.DomainEvents.IEventService>(), participation: TestRequired.Absent<Dfo.Server.Abstractions.World.IDungeonParticipationDomainService>());
        var context = new TowerActionContext(Session(), Guid.NewGuid(), 1);
        var seen = ViolationCapture.Attach();

        await Assert.ThrowsAsync<GameplayOffExecutorException>(async () =>
            await towers.StartAsync(context, CancellationToken.None));
        await Assert.ThrowsAsync<GameplayOffExecutorException>(async () =>
            await towers.CanRetryAsync(context, CancellationToken.None));

        Assert.Equal(["Writing BossTowerDomainService.StartAsync", "Writing BossTowerDomainService.CanRetryAsync"],
            seen.Select(violation => violation.Operation).ToArray());
        Assert.All(seen, violation => Assert.Equal(GameplayWriteAssertion.OffExecutorKind, violation.Kind));
        Assert.Null(scope.Current);
    }

    // TASK-30: one counterexample per write entry the state census added (GameplayStateCensusTests).

    [Fact]
    public async Task ClientSettingsDomainService_refuses_a_write_off_the_executor_and_reads_anywhere()
    {
        var settings = Make<Dfo.Server.Application.Accounts.ClientSettingsDomainService>();
        var connection = new NetworkConnectionId("settings");
        var seen = ViolationCapture.Attach();

        Assert.Throws<GameplayOffExecutorException>(() => settings.ForgetConnection(connection));

        AssertOneViolation(seen, "ClientSettingsDomainService.ForgetConnection");
        Assert.Empty(await settings.ReadHotkeysAsync(Account, CancellationToken.None));
        TestGameplay.Run(() => settings.ForgetConnection(connection));
    }

    [Fact]
    public async Task WeaknessDomainService_refuses_a_write_through_its_handle_off_the_executor_and_reads_anywhere()
    {
        // The writes are on the private handle For returns; the classification table lists its members too.
        var weakness = new WeaknessDomainService(null!, null!, logger: Microsoft.Extensions.Logging.Abstractions.NullLogger<Dfo.Server.Application.Characters.WeaknessDomainService>.Instance, transactions: TestRequired.TransactionScope());
        var seen = ViolationCapture.Attach();

        Assert.Throws<GameplayOffExecutorException>(() => weakness.For(Character).Recover());
        await Assert.ThrowsAsync<GameplayOffExecutorException>(async () => await weakness.For(Character).SetWeakness());
        Assert.Throws<GameplayOffExecutorException>(() => weakness.For(Character).PauseRecovery());

        Assert.Equal(["Writing Weakness.Recover", "Writing Weakness.SetWeakness", "Writing Weakness.PauseRecovery"],
            seen.Select(violation => violation.Operation).ToArray());
        Assert.All(seen, violation => Assert.Equal(GameplayWriteAssertion.OffExecutorKind, violation.Kind));
        Assert.Equal(100, weakness.For(Character).Value);
        TestGameplay.Run(() => weakness.For(Character).Recover());
        Assert.Empty(TestGameplay.Run(() => weakness.AdvanceRecovery()));
    }

    [Fact]
    public void CharacterContainerDomainService_refuses_a_write_off_the_executor_and_reads_anywhere()
    {
        var containers = new CharacterContainerDomainService(TestRequired.TransactionScope());
        var seen = ViolationCapture.Attach();

        Assert.Throws<GameplayOffExecutorException>(() => containers.Clear(Character));

        AssertOneViolation(seen, "CharacterContainerDomainService.Clear");
        Assert.Null(containers.FindContainer(Character, InventoryContainerType.Main));
        TestGameplay.Run(() => containers.Clear(Character));
    }

    [Fact]
    public void HousingState_refuses_a_write_off_the_executor_and_reads_anywhere()
    {
        var housing = new Dfo.Server.Application.Marriage.HousingState(null!, null!, null!, null!);
        var session = Session();
        var position = new CharacterPositionState(1, 2, 3, 4, 5);
        var seen = ViolationCapture.Attach();

        Assert.Throws<GameplayOffExecutorException>(() => housing.RememberReturn(session, position));

        AssertOneViolation(seen, "HousingState.RememberReturn");
        Assert.Null(housing.ReturnFor(session));
        TestGameplay.Run(() => housing.RememberReturn(session, position));
        Assert.Equal(position, housing.ReturnFor(session));
    }

    [Fact]
    public void ExpertStoreDomainService_refuses_a_write_off_the_executor_and_reads_anywhere()
    {
        var stores = new Dfo.Server.Application.Sessions.ExpertStoreDomainService();
        var seen = ViolationCapture.Attach();

        Assert.Throws<GameplayOffExecutorException>(() => stores.Open(Character));

        AssertOneViolation(seen, "ExpertStoreDomainService.Open");
        Assert.False(stores.HasOpenStore(Character));
        TestGameplay.Run(() => stores.Open(Character));
        Assert.True(stores.HasOpenStore(Character));
    }

    [Fact]
    public void PvpLobbyDomainService_refuses_a_write_off_the_executor_and_reads_anywhere()
    {
        var lobby = new Dfo.Server.Application.Sessions.PvpLobbyDomainService();
        var seen = ViolationCapture.Attach();

        Assert.Throws<GameplayOffExecutorException>(() => lobby.Withdraw(1));

        AssertOneViolation(seen, "PvpLobbyDomainService.Withdraw");
        Assert.Empty(lobby.OfferedOn(10011));
        Assert.False(TestGameplay.Run(() => lobby.Withdraw(1)));
    }

    [Fact]
    public void DungeonEntryIntentDomainService_refuses_a_write_off_the_executor_and_reads_anywhere()
    {
        var intents = new DungeonEntryIntentDomainService();
        var session = Session();
        var seen = ViolationCapture.Attach();

        Assert.Throws<GameplayOffExecutorException>(() => intents.ClearExact(session));

        AssertOneViolation(seen, "DungeonEntryIntentDomainService.ClearExact");
        Assert.Null(intents.Find(session));
        Assert.False(TestGameplay.Run(() => intents.ClearExact(session)));
    }

    [Fact]
    public async Task JoustMaintenanceService_refuses_its_tick_body_off_the_executor_and_runs_it_in_a_job()
    {
        var maintenance = Make<Dfo.Server.Application.Events.JoustMaintenanceDomainService>();
        var state = new Dfo.Server.Data.Events.JoustState(false, DateTimeOffset.UnixEpoch, default, null, null, null, null, [], [], 0, 0, 0);
        ValueTask Body() => maintenance.ObserveAsync(state, true);
        var seen = ViolationCapture.Attach();
        await Assert.ThrowsAsync<GameplayOffExecutorException>(async () => await Body());
        AssertOneViolation(seen, "JoustMaintenanceDomainService.ObserveAsync");
        await TestGameplay.RunAsync(async () => await Body());
    }

    [Fact]
    public async Task SelectedSessionSafety_refuses_a_write_off_the_executor_and_reads_anywhere()
    {
        var safety = Dfo.Server.Testing.TestSessionSafety.Create(
            Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(new Microsoft.Extensions.DependencyInjection.ServiceCollection()));
        var connection = new NetworkConnectionContext(new NetworkConnectionId("write-assertion"), NetworkEndpointKind.Game, 10011);
        var recipient = new Dfo.Server.Application.Commands.SelectedSessionRecipient(Session(), connection);
        var seen = ViolationCapture.Attach();

        await Assert.ThrowsAsync<GameplayOffExecutorException>(async () =>
            await safety.BlockAsync(recipient, new InvalidOperationException("unknown"), "probe"));

        AssertOneViolation(seen, "SelectedSessionSafety.BlockAsync");
        Assert.False(safety.IsBlocked(connection));
        await TestGameplay.RunAsync(async () => await safety.BlockAsync(recipient, new InvalidOperationException("unknown"), "probe"));
        Assert.True(safety.IsBlocked(connection));
    }

    [Fact]
    public async Task EffectOperationLedger_refuses_a_request_off_the_executor_and_validates_it_in_a_job()
    {
        // The ledger has no read member; in a job the same call reaches its own request validation.
        var ledger = Make<Dfo.Server.Application.Effects.EffectOperationLedger>();
        var connection = new NetworkConnectionContext(new NetworkConnectionId("write-assertion"), NetworkEndpointKind.Game, 10011);
        var request = new Dfo.Server.Protocol.Game.Joust.JoustBetRequest(new GameMessageMetadata(1, 2, 3, 4), 0, 0, 0);
        ValueTask Dispatch() { ledger.Admit(connection,request); return ValueTask.CompletedTask; }
        var seen = ViolationCapture.Attach();

        await Assert.ThrowsAsync<GameplayOffExecutorException>(async () => await Dispatch());

        AssertOneViolation(seen, "EffectOperationLedger.Admit");
        await Assert.ThrowsAsync<InvalidDataException>(() => TestGameplay.RunAsync(async () => await Dispatch()));
    }

    [Fact]
    public void DungeonRunDeliveryRegistry_refuses_a_write_off_the_executor_and_reads_anywhere()
    {
        var registry = new Dfo.Server.Protocol.Game.Dungeons.DungeonRunDeliveryRegistry();
        var runId = DungeonRunId.New();
        var seen = ViolationCapture.Attach();

        Assert.Throws<GameplayOffExecutorException>(() => registry.Register(runId, [Session()]));

        AssertOneViolation(seen, "DungeonRunDeliveryRegistry.Register");
        Assert.Empty(registry.Find(runId));
        TestGameplay.Run(() => registry.Register(runId, [Session()]));
        Assert.Single(registry.Find(runId));
    }

    public static TheoryData<Dfo.Server.Abstractions.Sessions.SelectedSessionRefreshTopic> RefreshTopics =>
        new(Enum.GetValues<Dfo.Server.Abstractions.Sessions.SelectedSessionRefreshTopic>());

    [Theory]
    [MemberData(nameof(RefreshTopics))]
    public async Task A_daily_refresh_refuses_its_timer_body_off_the_executor_and_runs_it_in_a_job(
        Dfo.Server.Abstractions.Sessions.SelectedSessionRefreshTopic topic)
    {
        var refresh = Make<Dfo.Server.Application.Sessions.SelectedSessionRefreshDomainService>();
        ValueTask Body() { refresh.Capture(topic); return ValueTask.CompletedTask; }
        var seen = ViolationCapture.Attach();
        await Assert.ThrowsAsync<GameplayOffExecutorException>(async () => await Body());
        AssertOneViolation(seen, "SelectedSessionRefreshDomainService.Capture");
        await TestGameplay.RunAsync(async () => await Body());
    }

    [Fact]
    public void PartyProjectionStreamRegistry_refuses_to_hand_out_a_stream_off_the_executor()
    {
        // Its one member hands out (and may add) a stream; it has no read member.
        var registry = new Dfo.Server.Protocol.Game.Party.PartyProjectionStreamRegistry();
        var seen = ViolationCapture.Attach();

        Assert.Throws<GameplayOffExecutorException>(() => Invoke(registry, "GetOrAdd", 1L));

        AssertOneViolation(seen, "PartyProjectionStreamRegistry.GetOrAdd");
        var stream = TestGameplay.Run(() => Invoke(registry, "GetOrAdd", 1L));
        Assert.Same(stream, TestGameplay.Run(() => Invoke(registry, "GetOrAdd", 1L)));
    }

    [Fact]
    public void PartyDeliveryStream_refuses_a_write_off_the_executor_and_reads_anywhere()
    {
        var registry = new Dfo.Server.Protocol.Game.Party.PartyProjectionStreamRegistry();
        var stream = TestGameplay.Run(() => Invoke(registry, "GetOrAdd", 1L))!;
        var revision = stream.GetType().GetProperty("LatestRevision")!;
        var seen = ViolationCapture.Attach();

        Assert.Throws<GameplayOffExecutorException>(() => Unwrapped(() => revision.SetValue(stream, 5L)));

        AssertOneViolation(seen, "PartyDeliveryStream.set_LatestRevision");
        Assert.Equal(0L, revision.GetValue(stream));
        TestGameplay.Run(() => revision.SetValue(stream, 5L));
        Assert.Equal(5L, revision.GetValue(stream));
    }

    [Fact]
    public void PartyClientIdRegistry_refuses_to_allocate_off_the_executor()
    {
        // Every member may allocate a wire number; there is no read member.
        var ids = new Dfo.Server.Protocol.Game.Party.PartyClientIdRegistry();
        var key = new PartyKey(PartyKeyKind.Real, 1);
        var seen = ViolationCapture.Attach();

        Assert.Throws<GameplayOffExecutorException>(() => ids.For(key, 1));

        AssertOneViolation(seen, "PartyClientIdRegistry.For");
        var number = TestGameplay.Run(() => ids.For(key, 1));
        Assert.Equal(number, TestGameplay.Run(() => ids.For(key, 1)));
    }

    [Fact]
    public void BossTowerDepartureProjectionQueue_refuses_a_write_off_the_executor()
    {
        var departures = new Dfo.Server.Protocol.Game.BossTowerDepartureProjectionQueue();
        var seen = ViolationCapture.Attach();

        Assert.Throws<GameplayOffExecutorException>(() => departures.Enqueue(new BossTowerActionResult(false, null)));

        AssertOneViolation(seen, "TowerDepartureProjectionQueue.Enqueue");
        TestGameplay.Run(() => departures.Enqueue(new BossTowerActionResult(false, null)));
        Assert.True(TestGameplay.Run(() => departures.TryClaim(Assert.Single(departures.SnapshotPending()), out _)));
    }

    [Fact]
    public void IllusionTowerDepartureProjectionQueue_refuses_a_write_off_the_executor()
    {
        var departures = new Dfo.Server.Protocol.Game.IllusionTowerDepartureProjectionQueue();
        var seen = ViolationCapture.Attach();

        Assert.Throws<GameplayOffExecutorException>(() => departures.Enqueue(new TowerActionResult(false, null)));

        AssertOneViolation(seen, "TowerDepartureProjectionQueue.Enqueue");
        TestGameplay.Run(() => departures.Enqueue(new TowerActionResult(false, null)));
        Assert.True(TestGameplay.Run(() => departures.TryClaim(Assert.Single(departures.SnapshotPending()), out _)));
    }

    [Fact]
    public async Task JoustBetCommandHandler_refuses_a_bet_off_the_executor_and_handles_it_in_a_job()
    {
        // The handler's static per-connection identity is its state; the dispatcher only runs it in a job.
        var handler = Make<Dfo.Server.Protocol.Game.Joust.GameJoustBetCommandHandler>();
        var execution = DomainCommandFixture.Context(Guid.NewGuid(), typeof(Dfo.Server.Protocol.Game.Joust.GameJoustBetCommand), handler.GetType(), FixtureUnitKind.Direct);
        var context = new NetworkDispatchContext(new NetworkConnectionContext(new NetworkConnectionId("joust"), NetworkEndpointKind.Game, 10011));
        var bet = new Dfo.Server.Protocol.Game.Joust.JoustBetRequest(new GameMessageMetadata(1, 2, 3, 4), 0, 0, 0);
        var seen = ViolationCapture.Attach();

        await Assert.ThrowsAsync<GameplayOffExecutorException>(async () =>
            await handler.HandleAsync(execution, new Dfo.Server.Protocol.Game.Joust.GameJoustBetCommand(context, bet), CancellationToken.None));

        AssertOneViolation(seen, "GameJoustBetCommandHandler.HandleAsync");
        await TestGameplay.RunAsync(async () => await handler.HandleAsync(execution, new Dfo.Server.Protocol.Game.Joust.GameJoustBetCommand(context, bet), CancellationToken.None));
    }

    private static Type ProtocolType(string name) =>
        typeof(Dfo.Server.Protocol.ProtocolModule).Assembly.GetType(name, throwOnError: true)!;

    private static T Make<T>(params object[] given) => (T)Make(typeof(T), given);

    /// <summary>Builds a type through its widest constructor: given values first, then a null object for every interface.</summary>
    private static object Make(Type type, params object[] given)
    {
        var constructor = type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .OrderByDescending(candidate => candidate.GetParameters().Length).First();
        var arguments = constructor.GetParameters().Select(parameter =>
            given.FirstOrDefault(parameter.ParameterType.IsInstanceOfType) ?? Fill(parameter.ParameterType)).ToArray();
        return constructor.Invoke(arguments);
    }

    private static object? Fill(Type type)
    {
        if (type == typeof(TimeProvider)) return TimeProvider.System;
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Microsoft.Extensions.Logging.ILogger<>))
            return typeof(NullLogger<>).MakeGenericType(type.GetGenericArguments()).GetField("Instance")!.GetValue(null);
        return type.IsInterface ? NullObject.Of(type) : null;
    }

    private static object? Invoke(object target, string method, params object?[] arguments) =>
        Unwrapped(() => target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .Invoke(target, arguments));

    private static object? Unwrapped(Func<object?> call)
    {
        try
        {
            return call();
        }
        catch (TargetInvocationException invocation) when (invocation.InnerException is { } inner)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(inner).Throw();
            throw;
        }
    }

    private static void Unwrapped(Action call) => Unwrapped(() =>
    {
        call();
        return null;
    });

    /// <summary>An interface implementation that does nothing and returns empty values.</summary>
    public class NullObject : DispatchProxy
    {
        public static object Of(Type type) =>
            typeof(DispatchProxy).GetMethod(nameof(Create), 2, Type.EmptyTypes)!
                .MakeGenericMethod(type, typeof(NullObject)).Invoke(null, null)!;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Empty(targetMethod!.ReturnType);

        private static object? Empty(Type type)
        {
            if (type == typeof(void)) return null;
            if (type == typeof(Task)) return Task.CompletedTask;
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>))
                return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(type.GetGenericArguments())
                    .Invoke(null, [Empty(type.GetGenericArguments()[0])]);
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ValueTask<>))
                return Activator.CreateInstance(type, Empty(type.GetGenericArguments()[0]));
            if (type.IsGenericType && type.GetGenericTypeDefinition() is var definition
                && (definition == typeof(IReadOnlyList<>) || definition == typeof(IEnumerable<>) || definition == typeof(IReadOnlyCollection<>)))
                return Array.CreateInstance(type.GetGenericArguments()[0], 0);
            return type.IsValueType ? Activator.CreateInstance(type) : null;
        }
    }

    /// <summary>A joust with nothing to advance and an inactive round.</summary>
    private sealed class QuietJoust : Dfo.Server.Abstractions.Events.IJoustProjectionDomainService
    {
        public ValueTask<Dfo.Server.Data.Events.JoustState> ReadAsync(CharacterId character, DateTimeOffset now, CancellationToken ct) =>
            ValueTask.FromResult(new Dfo.Server.Data.Events.JoustState(false, now, default, null, null, null, null, [], [], 0, 0, 0));
    }

    private static readonly DungeonLayout Layout = new(84, 0, 3, 1, new MazeCell(0, 0), new MazeCell(2, 0), 10, 123,
        new Dictionary<MazeCell, DungeonCellDefinition>
        {
            [new MazeCell(0, 0)] = DungeonCellDefinition.Fixed(10),
            [new MazeCell(1, 0)] = DungeonCellDefinition.Fixed(11),
            [new MazeCell(2, 0)] = DungeonCellDefinition.Fixed(12),
        });

    private static SelectedSessionIdentity Session() =>
        SelectedSessionIdentity.Create(Account, Character, new NetworkConnectionId("write-assertion"), 1, 10011);

    private static CreatureRuntimeDomainService Creatures(IDomainTransactionAccessor transactions) =>
        new(null!, null!, transactions, TestSessions.Directory(), TimeProvider.System, inventories: TestRequired.Absent<Dfo.Server.Abstractions.Inventory.ICharacterBusinessInventoryDomainService>());

    private static void AssertOneViolation(
        System.Collections.Concurrent.ConcurrentQueue<GameplayAssertionViolation> seen, string entry)
    {
        var violation = Assert.Single(seen);
        Assert.Equal(GameplayWriteAssertion.OffExecutorKind, violation.Kind);
        Assert.Equal("Writing " + entry, violation.Operation);
    }

    private static async ValueTask PutPotionAsync(IItemDomainStoreSession held, ContainerId container)
    {
        var potion = await held.CreateAsync(ItemSnapshot.FromCore(new InventoryItemCoreState(6, 1000, 1, 0, 0, 0, 0, 0, 0, 0, -1,
            new ChronicleOptionState(0, 0, 0, 0, 0), new ChronicleOptionState(0, 0, 0, 0, 0),
            0, 0, 0, 0, 0,
            new RandomOptionState(0, 0, 0), new RandomOptionState(0, 0, 0),
            new RandomOptionState(0, 0, 0), 0, byte.MaxValue, 0, new RandomOptionState(0, 0, 0),
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0), true), default);
        await held.PutAsync(container, 0, potion, default);
    }

    /// <summary>A durable store that must never be reached: the assertion comes first.</summary>
    private sealed class DurableOnly : IDomainStore<object>
    {
        public object StoreIdentity => this;

        public bool IsDurable => true;

        public object SessionIn(IDomainTransaction transaction) => throw new InvalidOperationException("reached");
    }

    /// <summary>Counts the sessions the teardown save opens; each one writes the position once.</summary>
    private sealed class RecordingPositions : ICharacterDomainStore, ICharacterDomainStoreSession
    {
        public int Sessions { get; private set; }

        public object StoreIdentity => this;

        public bool IsDurable => true;

        public IDomainStoreSession<OwnedCharacterId, long, StoredCreature> Creatures => throw new NotSupportedException();

        public ICharacterDomainStoreSession SessionIn(IDomainTransaction transaction)
        {
            Sessions++;
            return this;
        }

        public ValueTask<bool> HoldsAsync(OwnedCharacterId characterId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask<CharacterPositionState?> ReadPositionAsync(
            OwnedCharacterId characterId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public void WritePosition(OwnedCharacterId characterId, CharacterPositionState position, byte? lastInnTownId = null)
        {
        }
    }

    /// <summary>An accessor that is never the configured scope and never has a transaction open.</summary>
    private sealed class NoTransactions : IDomainTransactionAccessor
    {
        public IDomainTransaction? Current => null;
    }
    private sealed class NoDeadlineWake : IDungeonDeadlineWake { public void Wake(DungeonDeadlineKind kind) { } }
}
