using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Execution;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.CardReward;
using Dfo.Server.Protocol.Game.Dungeons;
using Dfo.Server.Protocol.Game.DungeonClear;
using Microsoft.Extensions.Logging.Abstractions;
using Dfo.Server.Testing;

namespace Dfo.Server.Protocol.Tests.Game.CardReward;

public sealed partial class DungeonCardPresentationCoordinatorTests
{
    [Fact]
    public async Task Settlement_opens_layout_then_auto_flips_one_free_card()
    {
        using var world = TestGameplay.Run(() => Build(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4)));

        await TestGameplay.RunAsync(async () =>
        {
            world.Coordinator.ScheduleAfterSettlement(world.Session, world.Run.RunId);
            await WaitUntilAsync(() => world.Delay.Pending(TimeSpan.FromSeconds(2)) == 1);
        });
        await world.Delay.ReleaseAsync(TimeSpan.FromSeconds(2));
        await TestGameplay.RunAsync(async () =>
        {
            await WaitUntilAsync(() => world.Sender.For(world.Session.ConnectionId)
                .Any(message => message is CardSelectRightStateResponse));
            Assert.Collection(
                world.Sender.For(world.Session.ConnectionId),
                first => Assert.IsType<ScoreScrollStateResponse>(first),
                second => Assert.IsType<CardSelectRightStateResponse>(second));
            await WaitUntilAsync(() => world.Delay.Pending(TimeSpan.FromSeconds(4)) == 1);
        });
        await world.Delay.ReleaseAsync(TimeSpan.FromSeconds(4));
        await TestGameplay.RunAsync(async () =>
        {
            await WaitUntilAsync(() => world.Sender.For(world.Session.ConnectionId)
                .Any(message => message is SelectCardResponse));

            Assert.Equal(1, world.Bus.Selections);
            Assert.Single(world.Runs.Find(world.Session.CharacterId)!.CardSelections);
        });
    }

    [Fact]
    public void Production_timing_matches_the_a21_two_then_four_second_cadence()
    {
        Assert.Equal(TimeSpan.FromSeconds(2),
            DungeonCardPresentationTiming.Production.LayoutDelay);
        Assert.Equal(TimeSpan.FromSeconds(4),
            DungeonCardPresentationTiming.Production.AutoFlipDelay);
    }

    [Fact]
    public async Task Manual_free_flip_cancels_the_pending_auto_flip()
    {
        using var world = TestGameplay.Run(() => Build(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4)));
        await TestGameplay.RunAsync(async () =>
        {
            world.Coordinator.ScheduleAfterSettlement(world.Session, world.Run.RunId);
            await WaitUntilAsync(() => world.Delay.Pending(TimeSpan.FromSeconds(2)) == 1);
        });
        await world.Delay.ReleaseAsync(TimeSpan.FromSeconds(2));
        await TestGameplay.RunAsync(async () =>
        {
            await WaitUntilAsync(() => world.Runs.Find(world.Session.CharacterId)!.CardLayoutOpen);
            await WaitUntilAsync(() => world.Delay.Pending(TimeSpan.FromSeconds(4)) == 1);

            var result = await world.Bus.SendAsync(
                new SelectCardCommand(
                    world.Session.AccountId,
                    world.Session.CharacterId,
                    ClearRewardCardKind.Free,
                    0,
                    world.Run.RunId));
            world.Coordinator.Cancel(world.Session.CharacterId, world.Run.RunId);
            await world.Projection.DeliverAsync(
                world.Session, result, DateTimeOffset.UnixEpoch, default);
            await WaitUntilAsync(() => world.Delay.Pending(TimeSpan.FromSeconds(4)) == 0);

            Assert.Equal(1, world.Bus.Selections);
            Assert.Equal(0, Assert.Single(
                world.Runs.Find(world.Session.CharacterId)!.CardSelections).Index);
        });
    }

    [Fact]
    public async Task Leaving_or_replacing_the_session_invalidates_old_layout_timer()
    {
        using var departed = TestGameplay.Run(() => Build(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4)));
        await TestGameplay.RunAsync(async () =>
        {
            departed.Coordinator.ScheduleAfterSettlement(departed.Session, departed.Run.RunId);
            await WaitUntilAsync(() => departed.Delay.Pending(TimeSpan.FromSeconds(2)) == 1);
            Assert.True(departed.Runs.End(departed.Session.CharacterId, departed.Run.RunId));
        });
        await departed.Delay.ReleaseAsync(TimeSpan.FromSeconds(2));

        using var replaced = TestGameplay.Run(() => Build(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4)));
        await TestGameplay.RunAsync(async () =>
        {
            replaced.Coordinator.ScheduleAfterSettlement(replaced.Session, replaced.Run.RunId);
            await WaitUntilAsync(() => replaced.Delay.Pending(TimeSpan.FromSeconds(2)) == 1);
            replaced.Sessions.Current = replaced.Session with
            {
                ConnectionId = new NetworkConnectionId("replacement"),
                ConnectionGeneration = 2,
            };
        });
        await replaced.Delay.ReleaseAsync(TimeSpan.FromSeconds(2));
await TestGameplay.RunAsync(async () =>
{

            await WaitUntilAsync(() =>
                departed.Delay.Pending(TimeSpan.FromSeconds(2)) == 0
                && replaced.Delay.Pending(TimeSpan.FromSeconds(2)) == 0);
            Assert.Empty(departed.Sender.For(departed.Session.ConnectionId));
            Assert.Empty(replaced.Sender.For(replaced.Session.ConnectionId));
            Assert.Equal(0, departed.Bus.Selections);
            Assert.Equal(0, replaced.Bus.Selections);
});
    }

    /// <summary>
    /// The two members' auto-flips are timer CMDs that run one after the other (RQ-001), so they no
    /// longer contend; each member still gets exactly one card, on distinct columns. Retrying the
    /// same personal confirmation after a conflict is kept by the transient-conflict case below
    /// (formerly <c>Simultaneous_party_auto_flips_retry_a_contended_column_and_award_each_member_once</c>).
    /// </summary>
    [Fact]
    public async Task Party_auto_flips_run_one_after_the_other_and_award_each_member_once()
    {
        using var world = TestGameplay.Run(() => BuildParty());
        TestGameplay.Run(() =>
        {
            foreach (var session in world.Sessions.All)
                world.Coordinator.ScheduleAfterSettlement(session, world.Run.RunId);
        });

        await WaitUntilAsync(() => world.Delay.Pending(TimeSpan.FromSeconds(2)) == 2);
        await world.Delay.ReleaseAsync(TimeSpan.FromSeconds(2));
        await WaitUntilAsync(() => world.Delay.Pending(TimeSpan.FromSeconds(4)) == 2);
        await world.Delay.ReleaseAsync(TimeSpan.FromSeconds(4));
        await WaitUntilAsync(() => world.Bus.Selections == 2);

        TestGameplay.Run(() =>
        {
            var selections = world.Runs.Find(world.Sessions.All[0].CharacterId)!.CardSelections;
            Assert.Equal(2, world.Bus.Selections);
            Assert.Equal(2, selections.Count);
            Assert.Equal(2, selections.Select(selection => selection.CharacterId).Distinct().Count());
            Assert.Equal(2, selections.Select(selection => selection.Index).Distinct().Count());
            foreach (var session in world.Sessions.All)
                Assert.Single(world.Sender.For(session.ConnectionId).OfType<SelectCardResponse>());
        });
    }

    [Fact]
    public async Task Late_party_layout_requests_do_not_repeat_the_panel_or_cancel_auto_flip()
    {
        using var world = TestGameplay.Run(() => BuildParty());
        await TestGameplay.RunAsync(async () =>
        {
            foreach (var session in world.Sessions.All)
                world.Coordinator.ScheduleAfterSettlement(session, world.Run.RunId);

            await WaitUntilAsync(() => world.Delay.Pending(TimeSpan.FromSeconds(2)) == 2);
        });
        await world.Delay.ReleaseAsync(TimeSpan.FromSeconds(2));
        await TestGameplay.RunAsync(async () =>
        {
            await WaitUntilAsync(() => world.Delay.Pending(TimeSpan.FromSeconds(4)) == 2);

            foreach (var session in world.Sessions.All)
            {
                await world.Coordinator.OpenOnRequestAsync(session, world.Run.RunId, default);
                await world.Coordinator.OpenOnRequestAsync(session, world.Run.RunId, default);
            }

            foreach (var session in world.Sessions.All)
            {
                var layout = Assert.Single(world.Sender.For(session.ConnectionId)
                    .OfType<CardSelectRightStateResponse>());
                Assert.Equal([0, 1], layout.EligibleSeats);
            }
            Assert.Equal(2, world.Delay.Pending(TimeSpan.FromSeconds(4)));

        });
        await world.Delay.ReleaseAsync(TimeSpan.FromSeconds(4));
        await TestGameplay.RunAsync(async () =>
        {
            await WaitUntilAsync(() => world.Bus.Selections == 2);
        });
    }

    [Fact]
    public async Task Late_scroll_request_does_not_send_a_second_acknowledgement()
    {
        using var world = TestGameplay.Run(() => Build(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4)));
        await TestGameplay.RunAsync(async () =>
        {
            world.Coordinator.ScheduleAfterSettlement(world.Session, world.Run.RunId);
            await WaitUntilAsync(() => world.Delay.Pending(TimeSpan.FromSeconds(2)) == 1);
        });
        await world.Delay.ReleaseAsync(TimeSpan.FromSeconds(2));
        await TestGameplay.RunAsync(async () =>
        {
            await WaitUntilAsync(() => world.Sender.For(world.Session.ConnectionId)
                .Any(message => message is CardSelectRightStateResponse));

            var handler = new ScoreScrollStateHandler(
                world.Sessions,
                world.Sender,
                NullLogger<ScoreScrollStateHandler>.Instance,
                world.Coordinator,
                world.Runs);
            await handler.HandleAsync(
                new NetworkDispatchContext(new NetworkConnectionContext(
                    world.Session.ConnectionId, NetworkEndpointKind.Game, 10010)),
                new ScoreScrollStateRequest(GameMessageMetadata.Empty),
                CancellationToken.None);

            Assert.Collection(
                world.Sender.For(world.Session.ConnectionId),
                first => Assert.IsType<ScoreScrollStateResponse>(first),
                second => Assert.IsType<CardSelectRightStateResponse>(second));
        });
    }

    [Fact]
    public async Task Early_scroll_request_sends_the_ordered_pair_once_and_retires_layout_timer()
    {
        using var world = TestGameplay.Run(() => Build(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4)));
        await TestGameplay.RunAsync(async () =>
        {
            world.Coordinator.ScheduleAfterSettlement(world.Session, world.Run.RunId);
            await WaitUntilAsync(() => world.Delay.Pending(TimeSpan.FromSeconds(2)) == 1);

            var handler = new ScoreScrollStateHandler(
                world.Sessions,
                world.Sender,
                NullLogger<ScoreScrollStateHandler>.Instance,
                world.Coordinator,
                world.Runs);
            await handler.HandleAsync(
                new NetworkDispatchContext(new NetworkConnectionContext(
                    world.Session.ConnectionId, NetworkEndpointKind.Game, 10010)),
                new ScoreScrollStateRequest(GameMessageMetadata.Empty),
                CancellationToken.None);

            await WaitUntilAsync(() => world.Delay.Pending(TimeSpan.FromSeconds(2)) == 0);
            // The auto-flip timer starts after a yield, so its delay is registered asynchronously.
            await WaitUntilAsync(() => world.Delay.Pending(TimeSpan.FromSeconds(4)) == 1);
            Assert.Equal(1, world.Delay.Pending(TimeSpan.FromSeconds(4)));
            Assert.Collection(
                world.Sender.For(world.Session.ConnectionId),
                first => Assert.IsType<ScoreScrollStateResponse>(first),
                second => Assert.IsType<CardSelectRightStateResponse>(second));
        });
    }

    [Fact]
    public async Task Early_party_layout_requests_open_once_and_retire_the_layout_timers()
    {
        using var world = TestGameplay.Run(() => BuildParty());
        await TestGameplay.RunAsync(async () =>
        {
            foreach (var session in world.Sessions.All)
                world.Coordinator.ScheduleAfterSettlement(session, world.Run.RunId);

            await WaitUntilAsync(() => world.Delay.Pending(TimeSpan.FromSeconds(2)) == 2);
            foreach (var session in world.Sessions.All)
                await world.Coordinator.OpenOnRequestAsync(session, world.Run.RunId, default);

            await WaitUntilAsync(() => world.Delay.Pending(TimeSpan.FromSeconds(2)) == 0);
            // The auto-flip timers start after a yield, so their delays are registered asynchronously.
            await WaitUntilAsync(() => world.Delay.Pending(TimeSpan.FromSeconds(4)) == 2);
            Assert.Equal(2, world.Delay.Pending(TimeSpan.FromSeconds(4)));
            foreach (var session in world.Sessions.All)
                Assert.Single(world.Sender.For(session.ConnectionId)
                    .OfType<CardSelectRightStateResponse>());

        });
        await world.Delay.ReleaseAsync(TimeSpan.FromSeconds(4));
        await TestGameplay.RunAsync(async () =>
        {
            await WaitUntilAsync(() => world.Bus.Selections == 2);
        });
    }

    [Fact]
    public async Task Auto_flip_retries_the_same_personal_confirmation_after_a_transient_conflict()
    {
        using var world = TestGameplay.Run(() => Build(
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(4),
            transientlyContendedFirstColumn: true));
        TestGameplay.Run(() => world.Coordinator.ScheduleAfterSettlement(world.Session, world.Run.RunId));

        await WaitUntilAsync(() => world.Delay.Pending(TimeSpan.FromSeconds(2)) == 1);
        await world.Delay.ReleaseAsync(TimeSpan.FromSeconds(2));
        await WaitUntilAsync(() => world.Delay.Pending(TimeSpan.FromSeconds(4)) == 1);
        await world.Delay.ReleaseAsync(TimeSpan.FromSeconds(4));
        await WaitUntilAsync(() => world.Bus.Selections == 1);
        Assert.Equal(1, world.Bus.Selections);

        Assert.Equal([0, 0], world.Bus.Attempts);
        Assert.Equal(0, Assert.Single(
            world.Runs.Find(world.Session.CharacterId)!.CardSelections).Index);
    }

    private static World Build(
        TimeSpan layoutDelay,
        TimeSpan autoFlipDelay,
        bool transientlyContendedFirstColumn = false,
        IDungeonCardPresentationDelay? realDelay = null)
    {
        var session = SelectedSessionIdentity.Create(
            new AccountId(1),
            new CharacterId(1),
            new NetworkConnectionId("one"),
            connectionGeneration: 1,
            listenerPort: 10010);
        var sessions = new MutableSessions(session);
        var runs = new DungeonRunDomainService();
        var run = runs.BeginParty(
            [new DungeonRunParticipant(
                session.AccountId, session.CharacterId, Slot: 0, IsLeader: true)],
            new DungeonLayout(
                144, 0, 1, 1, new MazeCell(0, 0), new MazeCell(0, 0), 8153, 7,
                new Dictionary<MazeCell, DungeonCellDefinition> { [new MazeCell(0, 0)] = DungeonCellDefinition.Fixed(8153)}),
            difficulty: 0);
        var face = new ClearRewardCardFace(
            ClearRewardCardKind.Free,
            [
                new ClearRewardCardSlot(0, null),
                new ClearRewardCardSlot(1, null),
                new ClearRewardCardSlot(2, null),
                new ClearRewardCardSlot(3, null),
            ],
            Gold: 0);
        Assert.NotNull(runs.TryOpenClear(session.CharacterId, run.RunId, run.RoomVersion));
        Assert.NotNull(runs.FixClearRewardCards(
            session.CharacterId,
            run.RunId,
            face,
            face with { Kind = ClearRewardCardKind.Paid },
            paidFlipCost: 1));
        var settlement = Assert.IsType<DungeonSettlementClaim>(
            runs.TryClaimSettlement(session.CharacterId, run.RunId));
        Assert.NotNull(runs.CommitSettlement(settlement));

        var registry = new DungeonRunDeliveryRegistry();
        registry.Register(run.RunId, [session]);
        var sender = new RecordingSender();
        var projection = new DungeonCardSelectionProjectionDelivery(
            sessions, runs, registry, sender);
        var bus = new CardCommandBus(
            runs,
            transientConflict: transientlyContendedFirstColumn
                ? session.CharacterId
                : null);
        var delay = new ControlledDelay();
        var executor = new GameplayExecutor(NullLogger<GameplayExecutor>.Instance);
        var queue = new RecordingQueue(executor);
        var coordinator = new DungeonCardPresentationCoordinator(
            sessions,
            runs,
            bus,
            sender,
            projection,
            TimeProvider.System,
            realDelay ?? delay,
            NullLogger<DungeonCardPresentationCoordinator>.Instance,
            new DungeonCardPresentationTiming(layoutDelay, autoFlipDelay),
            queue);
        return new World(
            session, sessions, runs, run, sender, projection, bus, delay, coordinator, executor, queue);
    }

    private static PartyWorld BuildParty(bool contendFirstAttempts = false)
    {
        SelectedSessionIdentity[] participants =
        [
            SelectedSessionIdentity.Create(
                new AccountId(1), new CharacterId(1), new NetworkConnectionId("one"),
                connectionGeneration: 1, listenerPort: 10010),
            SelectedSessionIdentity.Create(
                new AccountId(2), new CharacterId(2), new NetworkConnectionId("two"),
                connectionGeneration: 1, listenerPort: 10011),
        ];
        var sessions = new PartySessions(participants);
        var runs = new DungeonRunDomainService();
        var run = runs.BeginParty(
            [
                new DungeonRunParticipant(
                    participants[0].AccountId, participants[0].CharacterId, 0, IsLeader: true),
                new DungeonRunParticipant(
                    participants[1].AccountId, participants[1].CharacterId, 1, IsLeader: false),
            ],
            new DungeonLayout(
                144, 0, 1, 1, new MazeCell(0, 0), new MazeCell(0, 0), 8153, 7,
                new Dictionary<MazeCell, DungeonCellDefinition> { [new MazeCell(0, 0)] = DungeonCellDefinition.Fixed(8153)}),
            difficulty: 0);
        var face = new ClearRewardCardFace(
            ClearRewardCardKind.Free,
            [
                new ClearRewardCardSlot(0, null),
                new ClearRewardCardSlot(1, null),
                new ClearRewardCardSlot(2, null),
                new ClearRewardCardSlot(3, null),
            ],
            Gold: 0);
        Assert.NotNull(runs.TryOpenClear(
            participants[0].CharacterId, run.RunId, run.RoomVersion));
        foreach (var session in participants)
        {
            Assert.NotNull(runs.FixClearRewardCards(
                session.CharacterId,
                run.RunId,
                face,
                face with { Kind = ClearRewardCardKind.Paid },
                paidFlipCost: 1));
            var settlement = Assert.IsType<DungeonSettlementClaim>(
                runs.TryClaimSettlement(session.CharacterId, run.RunId));
            Assert.NotNull(runs.CommitSettlement(settlement));
        }

        var registry = new DungeonRunDeliveryRegistry();
        registry.Register(run.RunId, participants);
        var sender = new RecordingSender();
        var projection = new DungeonCardSelectionProjectionDelivery(
            sessions, runs, registry, sender);
        // Timer CMDs run one at a time, so a gate that waits for both first attempts would hold the
        // first one forever; it is only for a caller that drives the attempts concurrently itself.
        var bus = new CardCommandBus(runs, contendFirstAttempts ? new FirstAttemptGate(participants.Length) : null);
        var delay = new ControlledDelay();
        var executor = new GameplayExecutor(NullLogger<GameplayExecutor>.Instance);
        var queue = new RecordingQueue(executor);
        var coordinator = new DungeonCardPresentationCoordinator(
            sessions,
            runs,
            bus,
            sender,
            projection,
            TimeProvider.System,
            delay,
            NullLogger<DungeonCardPresentationCoordinator>.Instance,
            DungeonCardPresentationTiming.Production,
            queue);
        return new PartyWorld(
            sessions, runs, run, sender, projection, bus, delay, coordinator, executor, queue);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException("The card presentation transition did not arrive.");
            await Task.Delay(10);
        }
    }

    private sealed record World(
        SelectedSessionIdentity Session,
        MutableSessions Sessions,
        DungeonRunDomainService Runs,
        DungeonRunState Run,
        RecordingSender Sender,
        DungeonCardSelectionProjectionDelivery Projection,
        CardCommandBus Bus,
        ControlledDelay Delay,
        DungeonCardPresentationCoordinator Coordinator,
        // The timer CMDs run here. Never hand it to TestGameplay: a job polling for a timer effect
        // would then wait on the timer CMD queued behind it.
        GameplayExecutor Executor,
        RecordingQueue Queue) : IDisposable
    {
        public void Dispose()
        {
            this.Coordinator.Dispose();
            this.Executor.Dispose();
        }
    }

    private sealed record PartyWorld(
        PartySessions Sessions,
        DungeonRunDomainService Runs,
        DungeonRunState Run,
        RecordingSender Sender,
        DungeonCardSelectionProjectionDelivery Projection,
        CardCommandBus Bus,
        ControlledDelay Delay,
        DungeonCardPresentationCoordinator Coordinator,
        GameplayExecutor Executor,
        RecordingQueue Queue) : IDisposable
    {
        public void Dispose()
        {
            this.Coordinator.Dispose();
            this.Executor.Dispose();
        }
    }

    private sealed class CardCommandBus(
        DungeonRunDomainService runs,
        FirstAttemptGate? firstAttemptGate = null,
        CharacterId? transientConflict = null) : ICommandBus
    {
        private bool conflictReturned;

        private int selections;

        public int Selections => Volatile.Read(ref selections);
        public List<int> Attempts { get; } = [];
        public System.Collections.Concurrent.ConcurrentQueue<(bool OnExecutor, GameplayCallChain? Chain)> Seen { get; } = new();

        public async ValueTask<TResult> SendAsync<TResult>(
            ICommand<TResult> command,
            CancellationToken cancellationToken = default)
            where TResult : notnull
        {
            var select = Assert.IsType<SelectCardCommand>(command);
            this.Seen.Enqueue((GameplayJob.IsOnExecutor, GameplayJob.Current));
            this.Attempts.Add(select.Index);
            if (firstAttemptGate is not null)
                await firstAttemptGate.ArriveAsync(select.CharacterId, cancellationToken);
            var run = runs.Find(select.CharacterId)!;
            if (!this.conflictReturned
                && transientConflict == select.CharacterId
                && select.Kind is ClearRewardCardKind.Free
                && select.Index == 0)
            {
                this.conflictReturned = true;
                return (TResult)(object)SelectCardResult.Refused(
                    SelectCardStatus.AlreadyFlipped, select.Kind, select.Index);
            }
            var claim = runs.TryReserveClearRewardCard(
                select.CharacterId, select.ExpectedRunId, select.Kind, select.Index);
            SelectCardResult result;
            if (claim is null)
            {
                result = SelectCardResult.Refused(
                    SelectCardStatus.AlreadyFlipped, select.Kind, select.Index);
            }
            else
            {
                var change = runs.CommitClearRewardCard(claim).Change;
                Assert.NotNull(change);
                Interlocked.Increment(ref this.selections);
                result = new SelectCardResult(
                    SelectCardStatus.Flipped,
                    select.Kind,
                    select.Index,
                    run.ClearRewardCards!.FaceOf(select.Kind).Slots,
                    StaticChange: change);
            }
            return (TResult)(object)result;
        }
    }

    private sealed class FirstAttemptGate(int target)
    {
        private readonly object gate = new();
        private readonly HashSet<CharacterId> arrived = [];
        private readonly TaskCompletionSource ready = new();

        public Task ArriveAsync(CharacterId characterId, CancellationToken cancellationToken)
        {
            lock (this.gate)
            {
                if (!this.arrived.Add(characterId))
                    return Task.CompletedTask;
                if (this.arrived.Count == target)
                    this.ready.TrySetResult();
                return this.ready.Task.WaitAsync(cancellationToken);
            }
        }
    }

    private sealed class MutableSessions(SelectedSessionIdentity current)
        : ISelectedSessionDomainService
    {
        // Test double: the current selection counts as being in the world, and
        // remembers which sessions were fenced.
        private readonly System.Collections.Concurrent.ConcurrentDictionary<
            Dfo.Server.Data.World.SelectedSessionIdentity, bool> testDoubleFences = new();
        void Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.EnterWorld(
            Dfo.Server.Data.World.SelectedSessionIdentity session) { }
        bool Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.Fence(
            Dfo.Server.Data.World.SelectedSessionIdentity session) =>
            ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).Find(session.AccountId) == session
            && testDoubleFences.TryAdd(session, true) | true;
        Dfo.Server.Data.World.SelectedSessionStanding? Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.StandingOf(
            Dfo.Server.Data.World.SelectedSessionIdentity session) =>
            ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).Find(session.AccountId) == session
                ? new(Dfo.Server.Data.World.SelectedSessionPhase.InWorld, testDoubleFences.ContainsKey(session), false)
                : null;
        bool Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.IsInWorld(
            Dfo.Server.Data.World.SelectedSessionIdentity session) =>
            ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).StandingOf(session)
                is { Phase: Dfo.Server.Data.World.SelectedSessionPhase.InWorld };
        bool Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.IsSelected(
            Dfo.Server.Data.World.SelectedSessionIdentity session) =>
            ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).StandingOf(session) is { ConnectionRevoked: false };
        bool Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.TryGetInWorld(
            Dfo.Server.Data.Characters.CharacterId characterId,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Dfo.Server.Data.World.SelectedSessionIdentity? session)
        {
            session = ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).FindByCharacter(characterId);
            if (session is not null && !((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).IsInWorld(session)) session = null;
            return session is not null;
        }
        bool Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.TryGetInWorld(
            ushort wireUserId,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Dfo.Server.Data.World.SelectedSessionIdentity? session)
        {
            session = ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).FindByWireUserId(wireUserId);
            if (session is not null && !((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).IsInWorld(session)) session = null;
            return session is not null;
        }
        bool Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.TryGetInWorld(
            Dfo.Server.Data.Networking.NetworkConnectionId connectionId,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Dfo.Server.Data.World.SelectedSessionIdentity? session)
        {
            session = ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).FindByConnection(connectionId);
            if (session is not null && !((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).IsInWorld(session)) session = null;
            return session is not null;
        }

        public SelectedSessionIdentity Current { get; set; } = current;
        public SelectedSessionIdentity? FindByConnection(NetworkConnectionId connectionId) =>
            this.Current.ConnectionId == connectionId ? this.Current : null;
        public SelectedSessionIdentity? FindByCharacter(CharacterId characterId) =>
            this.Current.CharacterId == characterId ? this.Current : null;
        public SelectedSessionIdentity? Find(AccountId accountId) =>
            this.Current.AccountId == accountId ? this.Current : null;
        public SelectedSessionIdentity? FindByWireUserId(ushort wireUserId) =>
            this.Current.WireUserId == wireUserId ? this.Current : null;
        public SelectedSessionIdentity? Select(SelectedSessionIdentity session) =>
            throw new NotSupportedException();
        public bool ReleaseExact(SelectedSessionIdentity session) =>
            throw new NotSupportedException();
    }

    private sealed class PartySessions(IReadOnlyList<SelectedSessionIdentity> all)
        : ISelectedSessionDomainService
    {
        // Test double: the current selection counts as being in the world, and
        // remembers which sessions were fenced.
        private readonly System.Collections.Concurrent.ConcurrentDictionary<
            Dfo.Server.Data.World.SelectedSessionIdentity, bool> testDoubleFences = new();
        void Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.EnterWorld(
            Dfo.Server.Data.World.SelectedSessionIdentity session) { }
        bool Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.Fence(
            Dfo.Server.Data.World.SelectedSessionIdentity session) =>
            ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).Find(session.AccountId) == session
            && testDoubleFences.TryAdd(session, true) | true;
        Dfo.Server.Data.World.SelectedSessionStanding? Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.StandingOf(
            Dfo.Server.Data.World.SelectedSessionIdentity session) =>
            ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).Find(session.AccountId) == session
                ? new(Dfo.Server.Data.World.SelectedSessionPhase.InWorld, testDoubleFences.ContainsKey(session), false)
                : null;
        bool Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.IsInWorld(
            Dfo.Server.Data.World.SelectedSessionIdentity session) =>
            ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).StandingOf(session)
                is { Phase: Dfo.Server.Data.World.SelectedSessionPhase.InWorld };
        bool Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.IsSelected(
            Dfo.Server.Data.World.SelectedSessionIdentity session) =>
            ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).StandingOf(session) is { ConnectionRevoked: false };
        bool Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.TryGetInWorld(
            Dfo.Server.Data.Characters.CharacterId characterId,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Dfo.Server.Data.World.SelectedSessionIdentity? session)
        {
            session = ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).FindByCharacter(characterId);
            if (session is not null && !((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).IsInWorld(session)) session = null;
            return session is not null;
        }
        bool Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.TryGetInWorld(
            ushort wireUserId,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Dfo.Server.Data.World.SelectedSessionIdentity? session)
        {
            session = ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).FindByWireUserId(wireUserId);
            if (session is not null && !((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).IsInWorld(session)) session = null;
            return session is not null;
        }
        bool Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.TryGetInWorld(
            Dfo.Server.Data.Networking.NetworkConnectionId connectionId,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Dfo.Server.Data.World.SelectedSessionIdentity? session)
        {
            session = ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).FindByConnection(connectionId);
            if (session is not null && !((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).IsInWorld(session)) session = null;
            return session is not null;
        }

        public IReadOnlyList<SelectedSessionIdentity> All { get; } = all;

        public SelectedSessionIdentity? FindByConnection(NetworkConnectionId connectionId) =>
            this.All.SingleOrDefault(session => session.ConnectionId == connectionId);

        public SelectedSessionIdentity? FindByCharacter(CharacterId characterId) =>
            this.All.SingleOrDefault(session => session.CharacterId == characterId);

        public SelectedSessionIdentity? Find(AccountId accountId) =>
            this.All.SingleOrDefault(session => session.AccountId == accountId);

        public SelectedSessionIdentity? FindByWireUserId(ushort wireUserId) =>
            this.All.SingleOrDefault(session => session.WireUserId == wireUserId);

        public SelectedSessionIdentity? Select(SelectedSessionIdentity session) =>
            throw new NotSupportedException();

        public bool ReleaseExact(SelectedSessionIdentity session) =>
            throw new NotSupportedException();
    }

    private sealed class ControlledDelay : IDungeonCardPresentationDelay
    {
        private readonly object gate = new();
        private readonly List<Waiter> waiters = [];

        public ValueTask WaitAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            var waiter = new Waiter(delay);
            lock (this.gate)
                this.waiters.Add(waiter);
            waiter.Cancellation = cancellationToken.Register(
                () => this.Cancel(waiter, cancellationToken));
            return new ValueTask(waiter.Completion.Task);
        }

        public int Pending(TimeSpan delay)
        {
            lock (this.gate)
                return this.waiters.Count(waiter => waiter.Delay == delay);
        }

        public Task ReleaseAsync(TimeSpan delay) => Task.Run(() =>
        {
            Waiter[] released;
            lock (this.gate)
            {
                released = [.. this.waiters.Where(waiter => waiter.Delay == delay)];
                this.waiters.RemoveAll(waiter => waiter.Delay == delay);
            }
            foreach (var waiter in released)
            {
                waiter.Cancellation.Dispose();
                waiter.Completion.TrySetResult();
            }
        });

        private void Cancel(Waiter waiter, CancellationToken cancellationToken)
        {
            lock (this.gate)
                _ = this.waiters.Remove(waiter);
            waiter.Completion.TrySetCanceled(cancellationToken);
        }

        private sealed class Waiter(TimeSpan delay)
        {
            public TimeSpan Delay { get; } = delay;
            // ReleaseAsync completes continuations on a worker before returning, rather than polling its scheduling delay.
            public TaskCompletionSource Completion { get; } = new();
            public CancellationTokenRegistration Cancellation { get; set; }
        }
    }

    /// <summary>
    /// What was registered for each connection, read from the outbox of a real registration adapter; every call goes
    /// to the real protocol sender over it (TASK-25). <see cref="Seen"/> notes each message at its registration point,
    /// in the job that registers it.
    /// </summary>
    private sealed class RecordingSender : IGameMessageSender
    {
        private readonly RecordedOutbound outbound = new();

        public System.Collections.Concurrent.ConcurrentQueue<(IGameServerMessage Message, bool OnExecutor, GameplayCallChain? Chain)> Seen { get; } = new();

        public IReadOnlyList<IGameServerMessage> For(NetworkConnectionId connectionId) =>
            [.. this.outbound.MessagesFor(connectionId).Cast<IGameServerMessage>()];

        public ValueTask SendCmdAsync(
            NetworkConnectionId connectionId,
            IGameCmdMessage message,
            CancellationToken cancellationToken)
        {
            this.Note([message]);
            return this.outbound.Game.SendCmdAsync(connectionId, message, cancellationToken);
        }

        public ValueTask SendNotiAsync(
            NetworkConnectionId connectionId,
            IGameNotiMessage message,
            CancellationToken cancellationToken)
        {
            this.Note([message]);
            return this.outbound.Game.SendNotiAsync(connectionId, message, cancellationToken);
        }

        public ValueTask SendExplicitAsync(
            NetworkConnectionId requiredDirectConnectionId,
            GameExplicitMessageDelivery delivery,
            CancellationToken cancellationToken)
        {
            this.Note(delivery.RequiredDirectMessages);
            foreach (var target in delivery.BestEffortTargets)
                this.Note(target.OrderedMessages);
            return this.outbound.Game.SendExplicitAsync(requiredDirectConnectionId, delivery, cancellationToken);
        }

        private void Note(IReadOnlyList<IGameServerMessage> frames)
        {
            foreach (var frame in frames)
                this.Seen.Enqueue((frame, GameplayJob.IsOnExecutor, GameplayJob.Current));
        }
    }
}
