using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.World;

public sealed class DungeonRoomMechanismTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.UnixEpoch;
    private readonly DungeonRoomMechanismRuleService rules = new();

    [Theory]
    [InlineData(0, 0, DungeonRoomMechanismOutcome.Normal)]
    [InlineData(14999, 0, DungeonRoomMechanismOutcome.Normal)]
    [InlineData(15000, 1, DungeonRoomMechanismOutcome.Normal)]
    [InlineData(59999, 3, DungeonRoomMechanismOutcome.Normal)]
    [InlineData(60000, 4, DungeonRoomMechanismOutcome.Crash)]
    [InlineData(90000, 4, DungeonRoomMechanismOutcome.Crash)]
    public void Clear_time_selects_terminal_outcome_and_exit(int milliseconds, byte stage, DungeonRoomMechanismOutcome outcome)
    {
        var state = rules.Start(new(rules.Define(DungeonRoomMechanismKind.Elevator)), Start);
        Assert.False(rules.AllowsExit(state, new(1, 0), new(2, 0)));
        state = rules.Complete(state, Start.AddMilliseconds(milliseconds));
        Assert.Equal(stage, state.Stage);
        Assert.Equal(outcome, state.Outcome);
        Assert.Null(rules.NextDeadline(state));
        Assert.True(rules.AllowsExit(state, new(1, 0), new(outcome == DungeonRoomMechanismOutcome.Normal ? (byte)2 : (byte)0, 0)));
        Assert.False(rules.AllowsExit(state, new(1, 0), new(1, 1)));
        Assert.Equal(state, rules.Complete(state, Start.AddHours(1)));
        Assert.Equal(state, rules.Start(state, Start.AddHours(1)));
    }

    [Fact]
    public void Late_timer_coalesces_stages_without_ending_the_room()
    {
        var initial = new DungeonRoomMechanismState(rules.Define(DungeonRoomMechanismKind.Elevator));
        Assert.Null(rules.NextDeadline(initial));
        var state = rules.Start(initial, Start);
        state = rules.Advance(state, Start.AddSeconds(47));
        Assert.Equal(3, state.Stage);
        Assert.Equal(Start.AddSeconds(60), rules.NextDeadline(state));
        state = rules.Advance(state, Start.AddHours(1));
        Assert.Equal(4, state.Stage);
        Assert.Equal(DungeonRoomMechanismOutcome.Running, state.Outcome);
        Assert.Null(rules.NextDeadline(state));
        Assert.Equal(state, rules.Advance(state, Start));
    }

    [Fact]
    public void Loading_barrier_death_time_revisit_and_teardown_share_one_room_state()
    {
        TestGameplay.Run(() =>
        {
            var clock = new ManualClock();
            var signal = new WakeCounter();
            var service = TestParties.Runs(clock: clock, mechanismRules: rules, mechanismWake: signal);
            DungeonRunParticipant[] members = [new(new(1), new(1), 0, true), new(new(2), new(2), 1, false)];
            var start = new MazeCell(1, 0);
            var right = new MazeCell(2, 0);
            var layout = new DungeonLayout(98765, 0, 3, 1, start, new(0, 0), 12345, 9,
                new Dictionary<MazeCell, DungeonCellDefinition>
                {
                    [start] = DungeonCellDefinition.Fixed(12345), [right] = DungeonCellDefinition.Fixed(12346),
                    [new(0, 0)] = DungeonCellDefinition.Fixed(12347),
                });
            var actor = new DungeonCellActorState(7, new(56716, 67, 0)
            {
                Declaration = new(56716, DungeonActorSpawnSource.SpecialPassiveObject, 0) { IsSceneOwnedWave = true },
            }, false, false, []);
            var seed = new DungeonCellSeed(start, 12345, 7, [actor], [])
                { Mechanisms = [rules.Define(DungeonRoomMechanismKind.Elevator)] };
            TestParties.EnsureEntryParty(service, members);
            var run = service.PreparePartyEntry(members, layout, 0, seed, DungeonRunId.New(), Start).Run;
            Assert.False(run.IsCleared);
            Assert.Empty(service.AdvanceRoomMechanisms(Start));
            service.TryMarkParticipantReady(new(1), run.RunId, 0);
            Assert.Empty(service.AdvanceRoomMechanisms(Start));
            clock.Now = Start.AddSeconds(10);
            service.TryMarkParticipantReady(new(2), run.RunId, 0);
            var running = Assert.Single(service.AdvanceRoomMechanisms(clock.Now));
            Assert.Equal(clock.Now, Assert.Single(running.States).StartedAt);
            Assert.Null(service.TryMove(new(1), run.RunId, 0, right, DungeonCellSeed.CompleteEmpty(right, 12346)));
            clock.Now = Start.AddSeconds(69); // 59 seconds since the shared loading barrier.
            var claim = Assert.IsType<DungeonActorDeathClaim>(service.TryClaimActorDeath(new(1), 7));
            clock.Now = Start.AddSeconds(90); // Durable reward work must not turn this into a crash.
            var change = Assert.IsType<DungeonActorDeathChange>(service.CommitActorDeathChange(claim));
            Assert.True(change.IsCellCleared);
            var completed = Assert.Single(change.Run.CurrentCellState.Mechanisms);
            Assert.Equal(DungeonRoomMechanismOutcome.Normal, completed.Outcome);
            Assert.Equal(3, completed.Stage);
            Assert.Null(service.TryClaimActorDeath(new(1), 7));
            Assert.Null(service.TryMove(new(1), run.RunId, 0, new(0, 0), DungeonCellSeed.CompleteEmpty(new(0, 0), 12347)));
            var moved = Assert.IsType<DungeonRunState>(service.TryMove(new(1), run.RunId, 0, right, DungeonCellSeed.CompleteEmpty(right, 12346)));
            Assert.Empty(service.AdvanceRoomMechanisms(clock.Now));
            var revisit = Assert.IsType<DungeonRunState>(service.TryMove(new(1), run.RunId, moved.RoomVersion, start));
            service.TryMarkParticipantReady(new(1), run.RunId, revisit.RoomVersion);
            service.TryMarkParticipantReady(new(2), run.RunId, revisit.RoomVersion);
            Assert.Equal(completed, Assert.Single(Assert.Single(service.AdvanceRoomMechanisms(clock.Now)).States));
            TestParties.Leave(service, new(1));
            service.End(new(1)); service.End(new(2));
            Assert.Empty(service.AdvanceRoomMechanisms(clock.Now));
            Assert.True(signal.Count > 0);
        });
    }

    [Fact]
    public async Task Wake_signal_coalesces_and_cancellation_unblocks_an_idle_worker()
    {
        var signal = new DungeonRoomMechanismWakeSignal(TimeProvider.System);
        signal.Wake(); signal.Wake();
        await signal.WaitAsync(null, default);
        using var stop = new CancellationTokenSource();
        var pending = signal.WaitAsync(null, stop.Token).AsTask();
        Assert.False(pending.IsCompleted);
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        await signal.WaitAsync(DateTimeOffset.MinValue, default);
    }

    private sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = Start;
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class WakeCounter : IDungeonRoomMechanismWakeSignal
    {
        public int Count { get; private set; }
        public void Wake() => Count++;
        public ValueTask WaitAsync(DateTimeOffset? deadline, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }
}
