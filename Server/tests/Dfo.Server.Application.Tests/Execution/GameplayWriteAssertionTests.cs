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
using Dfo.Server.Application.Commands.Pipeline;
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
/// RQ-061, the half without the outbox (TASK-18): a write unit of work, the three bare write sites and every write
/// member of the appendix F "move onto the executor" types throw off the executor and report it; the same call in a
/// job runs; reads, and the appendix B exemptions, run anywhere.
/// </summary>
/// <remarks>
/// A counterexample class the zero-violation evidence subtracts: 24 write violations per run, one per off-executor
/// write below (Begin 1, ChangeAsync 2, AloneAsync 1, the creature fallback 2 (through AdvanceAsync and direct),
/// the fourteen types 14, a finished job's continuation 1, a cancelled caller 1, two tower members that open
/// their own unit of work 2: 1 + 2 + 1 + 2 + 14 + 1 + 1 + 2 = 24). Every case throws before it touches any state.
/// </remarks>
public sealed class GameplayWriteAssertionTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);
    private static readonly AccountId Account = new(7);
    private static readonly CharacterId Character = new(70);

    [Fact]
    public void A_write_unit_of_work_opened_off_the_executor_throws_reports_and_leaves_nothing_open()
    {
        var scope = new DomainTransactionScope();
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
        var scope = new DomainTransactionScope();
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
        var scope = new DomainTransactionScope();
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
    public async Task A_teardown_position_save_without_a_use_case_throws_off_the_executor()
    {
        var store = new RecordingPositions();
        var persistence = new SelectedSessionPositionPersistence(store, new NoTransactions(), new StubEntryPlacementRuleService());
        var position = new CharacterPositionState(1, 2, 3, 4, 5);
        var seen = ViolationCapture.Attach();

        await Assert.ThrowsAsync<GameplayOffExecutorException>(async () =>
            await persistence.PersistAsync(Account, Character, position, CancellationToken.None));

        AssertOneViolation(seen, "SelectedSessionPositionPersistence.AloneAsync");
        Assert.Equal(0, store.Sessions);
        await TestGameplay.RunAsync(async () =>
            await persistence.PersistAsync(Account, Character, position, CancellationToken.None));
        Assert.Equal(1, store.Sessions);
    }

    [Fact]
    public async Task The_creature_fallback_transaction_refuses_off_the_executor_through_its_member()
    {
        // :274 is reached only with an accessor that is not the configured scope: no test or production path
        // builds one, so this one is made for the purpose (entry reconciliation section 1.5).
        var service = Creatures(new NoTransactions());
        var session = Session();
        var seen = ViolationCapture.Attach();

        await Assert.ThrowsAsync<GameplayOffExecutorException>(async () =>
            await service.AdvanceAsync(session, CancellationToken.None));

        AssertOneViolation(seen, "CreatureRuntimeDomainService.AdvanceAsync");
        var changes = await TestGameplay.RunAsync(async () =>
            await service.AdvanceAsync(session, CancellationToken.None));
        Assert.Empty(changes);
    }

    [Fact]
    public async Task The_creature_fallback_transaction_itself_asserts_before_it_opens()
    {
        // The member asserts first, so the fallback's own assertion is reached directly here.
        var service = Creatures(new NoTransactions());
        var within = typeof(CreatureRuntimeDomainService)
            .GetMethod("WithinAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .MakeGenericMethod(typeof(bool));
        var ran = 0;
        Func<IDomainTransaction, ValueTask<bool>> action = transaction =>
        {
            ran++;
            return ValueTask.FromResult(transaction is not null);
        };
        ValueTask<bool> Within() =>
            (ValueTask<bool>)within.Invoke(service, [action, CancellationToken.None])!;
        var seen = ViolationCapture.Attach();

        await Assert.ThrowsAsync<GameplayOffExecutorException>(async () => await Within());

        AssertOneViolation(seen, "CreatureRuntimeDomainService.WithinAsync");
        Assert.Equal(0, ran);
        Assert.True(await TestGameplay.RunAsync(async () => await Within()));
        Assert.Equal(1, ran);
    }

    [Fact]
    public void OnlineStateRegistry_refuses_a_write_off_the_executor_and_reads_anywhere()
    {
        var registry = new OnlineStateRegistry(new DomainTransactionScope());
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
        var registry = new LoginSessionRegistry(new DomainTransactionScope(), TimeProvider.System, new LoginSessionOptions());
        var connection = new NetworkConnectionId("login");
        var seen = ViolationCapture.Attach();

        Assert.Throws<GameplayOffExecutorException>(() => registry.Disconnected(connection));

        AssertOneViolation(seen, "LoginSessionRegistry.Disconnected");
        Assert.False(registry.IsSuperseded(connection));
        Assert.Null(registry.CaptureAuthority(connection));
        TestGameplay.Run(() => registry.Disconnected(connection));
    }

    [Fact]
    public void DungeonRunDomainService_refuses_a_write_off_the_executor_and_reads_anywhere()
    {
        var service = new DungeonRunDomainService();
        var run = TestGameplay.Run(() => service.PreparePartyEntry(
            [new DungeonRunParticipant(Account, Character, 0, true)], Layout, DungeonDifficulty.Normal,
            DungeonCellSeed.CompleteEmpty(new MazeCell(0, 0), 10), DungeonRunId.New(), DateTimeOffset.UtcNow).Run);
        var seen = ViolationCapture.Attach();

        Assert.Throws<GameplayOffExecutorException>(() =>
            service.TryMove(Character, run.RunId, run.RoomVersion, new MazeCell(1, 0), DungeonCellSeed.CompleteEmpty(new MazeCell(1, 0), 11)));

        AssertOneViolation(seen, "DungeonRunDomainService.TryMove");
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
            TimeProvider.System, EmptyPartyMemberSessionEndpointSource.Instance);
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
    public void TownAreaDomainService_refuses_a_write_off_the_executor_and_reads_anywhere()
    {
        var towns = new TownAreaDomainService();
        var member = new TownAreaMember(Character, new TownAreaMembershipLease(1), new TownAreaKey(11, 1, 2),
            new CharacterPositionState(1, 2, 3, 4, 5), sharesTownPresence: true);
        var seen = ViolationCapture.Attach();

        Assert.Throws<GameplayOffExecutorException>(() => towns.Enter(member));

        AssertOneViolation(seen, "TownAreaDomainService.Enter");
        Assert.Null(towns.Find(Character));
        TestGameplay.Run(() => towns.Enter(member));
        Assert.Equal(member, towns.Find(Character));
    }

    [Fact]
    public async Task DungeonParticipationDomainService_refuses_a_write_off_the_executor_and_reads_anywhere()
    {
        var scope = new DomainTransactionScope();
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
        var coordinator = new DungeonCardPresentationCoordinator(
            null!, null!, null!, null!, null!, TimeProvider.System, null!,
            NullLogger<DungeonCardPresentationCoordinator>.Instance, DungeonCardPresentationTiming.Production, null!);
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
        var scope = new DomainTransactionScope();
        var towers = new BossTowerDomainService(null!, null!, null!, null!, null!, null!, null!, null!, null!, null!,
            null!, scope, TimeProvider.System);
        var context = new BossTowerActionContext(Session(), Guid.NewGuid(), 1);
        var seen = ViolationCapture.Attach();

        await Assert.ThrowsAsync<GameplayOffExecutorException>(async () =>
            await towers.LoadAsync(context, CancellationToken.None));

        AssertOneViolation(seen, "BossTowerDomainService.LoadAsync");
        Assert.False(towers.IsInTower(Character));
        Assert.Empty(towers.FindReturnsDue(DateTimeOffset.UtcNow));
        var result = await TestGameplay.RunAsync(async () => await towers.LoadAsync(context, CancellationToken.None));
        Assert.False(result.Changed);
    }

    [Fact]
    public async Task IllusionTowerDomainService_refuses_a_write_off_the_executor_and_reads_anywhere()
    {
        var scope = new DomainTransactionScope();
        var towers = new IllusionTowerDomainService(null!, null!, null!, null!, null!, null!, null!, scope,
            TimeProvider.System);
        var context = new TowerActionContext(Session(), Guid.NewGuid(), 1);
        var seen = ViolationCapture.Attach();

        await Assert.ThrowsAsync<GameplayOffExecutorException>(async () =>
            await towers.LoadAsync(context, CancellationToken.None));

        AssertOneViolation(seen, "IllusionTowerDomainService.LoadAsync");
        Assert.False(towers.IsInTower(Character));
        var result = await TestGameplay.RunAsync(async () => await towers.LoadAsync(context, CancellationToken.None));
        Assert.False(result.Changed);
    }

    [Fact]
    public void TournamentDungeonDomainService_refuses_a_write_off_the_executor_and_reads_anywhere()
    {
        var tournaments = new TournamentDungeonDomainService(null!, null!, null!, null!, null!, null!, null!, null!,
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
        var engine = new AltarDungeonEngine(null!, null!, null!, null!, new DungeonRunDomainService(), null!, null!,
            null!, null!, null!, null!, null!, new NoTransactions(), TimeProvider.System, null!);
        var store = new AltarStateStore();
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
        var store = new AltarStateStore();
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
        var runtime = new OnlineCharacterRuntimeDomainService(TimeProvider.System);
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
        var towns = new TownAreaDomainService();
        var member = new TownAreaMember(Character, new TownAreaMembershipLease(1), new TownAreaKey(11, 1, 2),
            new CharacterPositionState(1, 2, 3, 4, 5), sharesTownPresence: true);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var seen = new System.Collections.Concurrent.ConcurrentQueue<GameplayAssertionViolation>();
        // Not suppressed: the continuation carries the job's token, which is inactive once the job has ended.
        var leaked = await TestGameplay.RunAsync(() => Task.FromResult(Task.Run(async () =>
        {
            await release.Task;
            ViolationCapture.Attach(seen);
            towns.Enter(member);
        })));
        release.TrySetResult();

        await Assert.ThrowsAsync<GameplayOffExecutorException>(() => leaked.WaitAsync(Patience));

        AssertOneViolation(seen, "TownAreaDomainService.Enter");
        Assert.Null(towns.Find(Character));
    }

    [Fact]
    public void A_cancelled_caller_is_still_reported_as_writing_off_the_executor()
    {
        // Unlike the gates, whose old WaitAsync looked at the token first, a write member never checked it, so
        // the assertion does not either: the violation is reported whatever the caller's token says.
        var runtime = new OnlineCharacterRuntimeDomainService(TimeProvider.System);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var seen = ViolationCapture.Attach();

        Assert.Throws<GameplayOffExecutorException>(() => runtime.StartCooldown(Session(), new ItemCooldownKey(false, 1), TimeSpan.FromSeconds(1)));

        AssertOneViolation(seen, "OnlineCharacterRuntimeDomainService.StartCooldown");
        Assert.True(cancelled.IsCancellationRequested);
    }

    [Fact]
    public async Task A_member_that_opens_its_own_unit_of_work_reports_the_member_not_the_entry()
    {
        // The tower members open their unit of work through Execute; their own assertion comes first, so the
        // report names the member, and Begin is not reached.
        var scope = new DomainTransactionScope();
        var towers = new BossTowerDomainService(null!, null!, null!, null!, null!, null!, null!, null!, null!, null!,
            null!, scope, TimeProvider.System);
        var context = new BossTowerActionContext(Session(), Guid.NewGuid(), 1);
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
        new(null!, null!, transactions, TestSessions.Directory(), TimeProvider.System);

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
}
