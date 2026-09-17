using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.CardReward;
using Dfo.Server.Protocol.Game.Dungeons;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dfo.Server.Protocol.Tests.Game.CardReward;

public sealed class DungeonCardPresentationCoordinatorTests
{
    [Fact]
    public async Task Settlement_opens_layout_then_auto_flips_one_free_card()
    {
        using var world = Build(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4));

        world.Coordinator.ScheduleAfterSettlement(world.Session, world.Run.RunId);
        await WaitUntilAsync(() => world.Delay.Pending(TimeSpan.FromSeconds(2)) == 1);
        world.Delay.Release(TimeSpan.FromSeconds(2));
        await WaitUntilAsync(() => world.Sender.For(world.Session.ConnectionId)
            .Any(message => message is CardSelectRightStateResponse));
        await WaitUntilAsync(() => world.Delay.Pending(TimeSpan.FromSeconds(4)) == 1);
        world.Delay.Release(TimeSpan.FromSeconds(4));
        await WaitUntilAsync(() => world.Sender.For(world.Session.ConnectionId)
            .Any(message => message is SelectCardResponse));

        Assert.Equal(1, world.Bus.Selections);
        Assert.Single(world.Runs.Find(world.Session.CharacterId)!.CardSelections);
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
        using var world = Build(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4));
        world.Coordinator.ScheduleAfterSettlement(world.Session, world.Run.RunId);
        await WaitUntilAsync(() => world.Delay.Pending(TimeSpan.FromSeconds(2)) == 1);
        world.Delay.Release(TimeSpan.FromSeconds(2));
        await WaitUntilAsync(() => world.Runs.Find(world.Session.CharacterId)!.CardLayoutOpen);
        await WaitUntilAsync(() => world.Delay.Pending(TimeSpan.FromSeconds(4)) == 1);

        var result = await world.Bus.SendAsync(
            new SelectCardCommand(
                world.Session.AccountId,
                world.Session.CharacterId,
                ClearRewardCardKind.Free,
                1,
                world.Run.RunId));
        world.Coordinator.Cancel(world.Session.CharacterId, world.Run.RunId);
        await world.Projection.DeliverAsync(
            world.Session, result, DateTimeOffset.UnixEpoch, default);
        await WaitUntilAsync(() => world.Delay.Pending(TimeSpan.FromSeconds(4)) == 0);

        Assert.Equal(1, world.Bus.Selections);
        Assert.Equal(1, Assert.Single(
            world.Runs.Find(world.Session.CharacterId)!.CardSelections).Index);
    }

    [Fact]
    public async Task Leaving_or_replacing_the_session_invalidates_old_layout_timer()
    {
        using var departed = Build(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4));
        departed.Coordinator.ScheduleAfterSettlement(departed.Session, departed.Run.RunId);
        await WaitUntilAsync(() => departed.Delay.Pending(TimeSpan.FromSeconds(2)) == 1);
        Assert.True(departed.Runs.End(departed.Session.CharacterId, departed.Run.RunId));
        departed.Delay.Release(TimeSpan.FromSeconds(2));

        using var replaced = Build(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4));
        replaced.Coordinator.ScheduleAfterSettlement(replaced.Session, replaced.Run.RunId);
        await WaitUntilAsync(() => replaced.Delay.Pending(TimeSpan.FromSeconds(2)) == 1);
        replaced.Sessions.Current = replaced.Session with
        {
            ConnectionId = new NetworkConnectionId("replacement"),
            ConnectionGeneration = 2,
        };
        replaced.Delay.Release(TimeSpan.FromSeconds(2));

        await WaitUntilAsync(() =>
            departed.Delay.Pending(TimeSpan.FromSeconds(2)) == 0
            && replaced.Delay.Pending(TimeSpan.FromSeconds(2)) == 0);
        Assert.Empty(departed.Sender.For(departed.Session.ConnectionId));
        Assert.Empty(replaced.Sender.For(replaced.Session.ConnectionId));
        Assert.Equal(0, departed.Bus.Selections);
        Assert.Equal(0, replaced.Bus.Selections);
    }

    [Fact]
    public async Task Simultaneous_party_auto_flips_retry_a_contended_column_and_award_each_member_once()
    {
        using var world = BuildParty();
        foreach (var session in world.Sessions.All)
            world.Coordinator.ScheduleAfterSettlement(session, world.Run.RunId);

        await WaitUntilAsync(() => world.Delay.Pending(TimeSpan.FromSeconds(2)) == 2);
        world.Delay.Release(TimeSpan.FromSeconds(2));
        await WaitUntilAsync(() => world.Delay.Pending(TimeSpan.FromSeconds(4)) == 2);
        world.Delay.Release(TimeSpan.FromSeconds(4));
        await WaitUntilAsync(() => world.Runs.Find(world.Sessions.All[0].CharacterId)!
            .CardSelections.Count == 2);
        await WaitUntilAsync(() => world.Bus.Selections == 2);

        var selections = world.Runs.Find(world.Sessions.All[0].CharacterId)!.CardSelections;
        Assert.Equal(2, world.Bus.Selections);
        Assert.Equal(2, selections.Select(selection => selection.CharacterId).Distinct().Count());
        Assert.Equal(2, selections.Select(selection => selection.Index).Distinct().Count());
    }

    [Fact]
    public async Task Auto_flip_skips_an_in_flight_reserved_column_and_stops_after_bounded_attempts()
    {
        using var world = Build(
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(4),
            transientlyContendedFirstColumn: true);
        world.Coordinator.ScheduleAfterSettlement(world.Session, world.Run.RunId);

        await WaitUntilAsync(() => world.Delay.Pending(TimeSpan.FromSeconds(2)) == 1);
        world.Delay.Release(TimeSpan.FromSeconds(2));
        await WaitUntilAsync(() => world.Delay.Pending(TimeSpan.FromSeconds(4)) == 1);
        world.Delay.Release(TimeSpan.FromSeconds(4));
        await WaitUntilAsync(() => world.Bus.Selections == 1);

        Assert.Equal([0, 1], world.Bus.Attempts);
        Assert.Equal(1, Assert.Single(
            world.Runs.Find(world.Session.CharacterId)!.CardSelections).Index);
    }

    private static World Build(
        TimeSpan layoutDelay,
        TimeSpan autoFlipDelay,
        bool transientlyContendedFirstColumn = false)
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
                new Dictionary<MazeCell, int> { [new MazeCell(0, 0)] = 8153 }),
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
        var coordinator = new DungeonCardPresentationCoordinator(
            sessions,
            runs,
            bus,
            sender,
            projection,
            TimeProvider.System,
            delay,
            NullLogger<DungeonCardPresentationCoordinator>.Instance,
            new DungeonCardPresentationTiming(layoutDelay, autoFlipDelay));
        return new World(
            session, sessions, runs, run, sender, projection, bus, delay, coordinator);
    }

    private static PartyWorld BuildParty()
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
                new Dictionary<MazeCell, int> { [new MazeCell(0, 0)] = 8153 }),
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
        var bus = new CardCommandBus(runs, new FirstAttemptGate(participants.Length));
        var delay = new ControlledDelay();
        var coordinator = new DungeonCardPresentationCoordinator(
            sessions,
            runs,
            bus,
            sender,
            projection,
            TimeProvider.System,
            delay,
            NullLogger<DungeonCardPresentationCoordinator>.Instance,
            DungeonCardPresentationTiming.Production);
        return new PartyWorld(
            sessions, runs, run, sender, projection, bus, delay, coordinator);
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
        DungeonCardPresentationCoordinator Coordinator) : IDisposable
    {
        public void Dispose() => this.Coordinator.Dispose();
    }

    private sealed record PartyWorld(
        PartySessions Sessions,
        DungeonRunDomainService Runs,
        DungeonRunState Run,
        RecordingSender Sender,
        DungeonCardSelectionProjectionDelivery Projection,
        CardCommandBus Bus,
        ControlledDelay Delay,
        DungeonCardPresentationCoordinator Coordinator) : IDisposable
    {
        public void Dispose() => this.Coordinator.Dispose();
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

        public async ValueTask<TResult> SendAsync<TResult>(
            ICommand<TResult> command,
            CancellationToken cancellationToken = default)
            where TResult : notnull
        {
            var select = Assert.IsType<SelectCardCommand>(command);
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
        private readonly TaskCompletionSource ready =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

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

        public void Release(TimeSpan delay)
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
        }

        private void Cancel(Waiter waiter, CancellationToken cancellationToken)
        {
            lock (this.gate)
                _ = this.waiters.Remove(waiter);
            waiter.Completion.TrySetCanceled(cancellationToken);
        }

        private sealed class Waiter(TimeSpan delay)
        {
            public TimeSpan Delay { get; } = delay;
            public TaskCompletionSource Completion { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            public CancellationTokenRegistration Cancellation { get; set; }
        }
    }

    private sealed class RecordingSender : IGameMessageSender
    {
        private readonly object gate = new();
        private readonly Dictionary<NetworkConnectionId, List<IGameServerMessage>> sent = [];

        public IReadOnlyList<IGameServerMessage> For(NetworkConnectionId connectionId)
        {
            lock (this.gate)
                return this.sent.TryGetValue(connectionId, out var frames) ? [.. frames] : [];
        }

        public ValueTask SendCmdAsync(
            NetworkConnectionId connectionId,
            IGameCmdMessage message,
            CancellationToken cancellationToken)
        {
            this.Add(connectionId, [message]);
            return ValueTask.CompletedTask;
        }

        public ValueTask SendNotiAsync(
            NetworkConnectionId connectionId,
            IGameNotiMessage message,
            CancellationToken cancellationToken)
        {
            this.Add(connectionId, [message]);
            return ValueTask.CompletedTask;
        }

        public ValueTask SendExplicitAsync(
            NetworkConnectionId requiredDirectConnectionId,
            GameExplicitMessageDelivery delivery,
            CancellationToken cancellationToken)
        {
            this.Add(requiredDirectConnectionId, delivery.RequiredDirectMessages);
            foreach (var target in delivery.BestEffortTargets)
                this.Add(target.ConnectionId, target.OrderedMessages);
            return ValueTask.CompletedTask;
        }

        private void Add(
            NetworkConnectionId connectionId,
            IReadOnlyList<IGameServerMessage> frames)
        {
            lock (this.gate)
            {
                if (!this.sent.TryGetValue(connectionId, out var received))
                    this.sent[connectionId] = received = [];
                received.AddRange(frames);
            }
        }
    }
}
