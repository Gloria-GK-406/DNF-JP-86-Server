using Dfo.Server.Protocol.Tests.Support;
using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Application.Execution;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.CardReward;
using Dfo.Server.Protocol.Game.DungeonClear;
using Dfo.Server.Testing;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Tests.Game.CardReward;

/// <summary>
/// The layout and auto-flip delays are detached timers that, once due, enqueue a timer CMD and wait
/// for it (spec appendix A row 6); the coordinator drains them before the executor stops (RQ-009).
/// </summary>
public sealed partial class DungeonCardPresentationCoordinatorTests
{
    private const string LayoutCommand = "timer:DungeonCardPresentationCoordinator/layout";
    private const string AutoFlipCommand = "timer:DungeonCardPresentationCoordinator/auto-flip";

    [Fact]
    public async Task A_due_layout_timer_sends_its_frames_from_a_timer_cmd_on_the_worlds_executor()
    {
        using var world = TestGameplay.Run(() => Build(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4)));
        TestGameplay.Run(() => world.Coordinator.ScheduleAfterSettlement(world.Session, world.Run.RunId));
        await WaitUntilAsync(() => world.Delay.Pending(TimeSpan.FromSeconds(2)) == 1);
        Assert.Empty(world.Queue.Enqueued);

        await world.Delay.ReleaseAsync(TimeSpan.FromSeconds(2));
        await WaitUntilAsync(() => world.Sender.For(world.Session.ConnectionId)
            .Any(message => message is CardSelectRightStateResponse));

        var layout = world.Sender.Seen.Where(x => x.Message is ScoreScrollStateResponse or CardSelectRightStateResponse).ToArray();
        Assert.Equal(2, layout.Length);
        Assert.All(layout, seen =>
        {
            Assert.True(seen.OnExecutor);
            var chain = Assert.NotNull(seen.Chain);
            Assert.Equal(GameplayCallSource.Timer, chain.Source);
            Assert.Equal(LayoutCommand, chain.CommandType);
        });
        Assert.Equal([LayoutCommand], world.Queue.Enqueued);
    }

    [Fact]
    public async Task A_due_auto_flip_selects_the_card_from_a_timer_cmd()
    {
        using var world = TestGameplay.Run(() => Build(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4)));
        TestGameplay.Run(() => world.Coordinator.ScheduleAfterSettlement(world.Session, world.Run.RunId));
        await WaitUntilAsync(() => world.Delay.Pending(TimeSpan.FromSeconds(2)) == 1);
        await world.Delay.ReleaseAsync(TimeSpan.FromSeconds(2));
        await WaitUntilAsync(() => world.Delay.Pending(TimeSpan.FromSeconds(4)) == 1);

        await world.Delay.ReleaseAsync(TimeSpan.FromSeconds(4));
        await WaitUntilAsync(() => world.Bus.Selections == 1);

        var attempt = Assert.Single(world.Bus.Seen);
        Assert.True(attempt.OnExecutor);
        var chain = Assert.NotNull(attempt.Chain);
        Assert.Equal(GameplayCallSource.Timer, chain.Source);
        Assert.Equal(AutoFlipCommand, chain.CommandType);
        Assert.Equal([LayoutCommand, AutoFlipCommand], world.Queue.Enqueued);
    }

    /// <summary>
    /// A delay that completes at once must not enqueue from the scheduling job's own stack: that
    /// would be a self-wait. The layout opens after the scheduling job has ended.
    /// </summary>
    [Fact]
    public async Task A_zero_delay_scheduled_from_a_job_opens_the_layout_after_that_job()
    {
        using var world = TestGameplay.Run(() => Build(TimeSpan.Zero, TimeSpan.FromMinutes(5),
            realDelay: new SystemDungeonCardPresentationDelay(TimeProvider.System)));
        var sentDuringJob = -1;

        await world.Queue.RunAsync(new GameplayWork(GameplayShardKey.World,
            GameplayCallChain.NewRoot(GameplayCallSource.Connection), "test:settlement", async _ =>
            {
                world.Coordinator.ScheduleAfterSettlement(world.Session, world.Run.RunId);
                await Task.Delay(100);
                sentDuringJob = world.Sender.For(world.Session.ConnectionId).Count;
            }), CancellationToken.None);
        await WaitUntilAsync(() => world.Sender.For(world.Session.ConnectionId)
            .Any(message => message is CardSelectRightStateResponse));

        Assert.Equal(0, sentDuringJob);
        Assert.Equal(["test:settlement", LayoutCommand], world.Queue.Enqueued);
    }

    [Fact]
    public async Task A_timer_cancelled_or_replaced_during_its_delay_never_runs_its_cmd()
    {
        using var world = TestGameplay.Run(() => Build(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4)));
        TestGameplay.Run(() => world.Coordinator.ScheduleAfterSettlement(world.Session, world.Run.RunId));
        await WaitUntilAsync(() => world.Delay.Pending(TimeSpan.FromSeconds(2)) == 1);
        TestGameplay.Run(() => world.Coordinator.Cancel(world.Session.CharacterId, world.Run.RunId));
        await WaitUntilAsync(() => world.Delay.Pending(TimeSpan.FromSeconds(2)) == 0);
        Assert.Equal(0, PendingTimers(world.Coordinator));

        TestGameplay.Run(() =>
        {
            world.Coordinator.ScheduleAfterSettlement(world.Session, world.Run.RunId);
            world.Coordinator.ScheduleAfterSettlement(world.Session, world.Run.RunId);
        });
        await WaitUntilAsync(() => world.Delay.Pending(TimeSpan.FromSeconds(2)) == 1);
        Assert.Equal(1, PendingTimers(world.Coordinator));
        await world.Delay.ReleaseAsync(TimeSpan.FromSeconds(2));
        await WaitUntilAsync(() => world.Delay.Pending(TimeSpan.FromSeconds(4)) == 1);

        Assert.Equal([LayoutCommand], world.Queue.Enqueued);
        Assert.Single(world.Sender.For(world.Session.ConnectionId).OfType<CardSelectRightStateResponse>());
        // The layout timer completed and handed its slot to the auto-flip timer.
        Assert.Equal(1, PendingTimers(world.Coordinator));
    }

    /// <summary>
    /// The drain cancels the timers still in their delay and waits for the one whose CMD is already
    /// queued; once it returns, nothing the coordinator started will enqueue again.
    /// </summary>
    [Fact]
    public async Task The_drain_waits_for_a_timer_already_waiting_on_its_cmd_and_stops_the_rest()
    {
        using var world = TestGameplay.Run(() => Build(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4)));
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blocker = world.Queue.RunAsync(new GameplayWork(GameplayShardKey.World,
            GameplayCallChain.NewRoot(GameplayCallSource.Connection), "test:blocker", async _ =>
            {
                held.TrySetResult();
                await release.Task;
            }), CancellationToken.None).AsTask();
        await held.Task.WaitAsync(TimeSpan.FromSeconds(10));
        TestGameplay.Run(() => world.Coordinator.ScheduleAfterSettlement(world.Session, world.Run.RunId));
        await WaitUntilAsync(() => world.Delay.Pending(TimeSpan.FromSeconds(2)) == 1);
        await world.Delay.ReleaseAsync(TimeSpan.FromSeconds(2));
        await WaitUntilAsync(() => world.Queue.Enqueued.Contains(LayoutCommand));
        var later = new DungeonRunId(Guid.NewGuid());
        TestGameplay.Run(() => world.Coordinator.ScheduleAfterSettlement(world.Session, later));
        await WaitUntilAsync(() => world.Delay.Pending(TimeSpan.FromSeconds(2)) == 1);

        var drain = world.Coordinator.DrainAsync(CancellationToken.None).AsTask();
        await Task.Delay(100);
        Assert.False(drain.IsCompleted);
        Assert.Equal(0, world.Delay.Pending(TimeSpan.FromSeconds(2)));

        release.TrySetResult();
        await blocker;
        await drain.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(["test:blocker", LayoutCommand], world.Queue.Enqueued);
        Assert.Empty(world.Sender.For(world.Session.ConnectionId));
        // TASK-16: a request after the drain is ignored rather than thrown at; no timer starts. The
        // later run's cancelled source stays in pending (Dispose leaves it), so the count must not grow.
        var pendingAtDrain = PendingTimers(world.Coordinator);
        TestGameplay.Run(() => world.Coordinator.ScheduleAfterSettlement(world.Session, world.Run.RunId));
        await Task.Delay(100);
        Assert.Equal(["test:blocker", LayoutCommand], world.Queue.Enqueued);
        Assert.Equal(0, world.Delay.Pending(TimeSpan.FromSeconds(2)));
        Assert.Equal(pendingAtDrain, PendingTimers(world.Coordinator));
    }

    /// <summary>
    /// A settlement whose CMD runs after the coordinator drained, as when the drains run in another
    /// order, is the same as one cancelled by the drain: no timer, no failure.
    /// </summary>
    [Fact]
    public async Task Settlement_scheduled_after_the_coordinator_drained_is_ignored_without_throwing()
    {
        using var world = TestGameplay.Run(() => Build(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4)));
        var logs = new CoordinatorLogs();
        using var coordinator = WithLogs(world, logs);
        await coordinator.DrainAsync(CancellationToken.None);

        TestGameplay.Run(() => coordinator.ScheduleAfterSettlement(world.Session, world.Run.RunId));
        await Task.Delay(100);

        Assert.Empty(world.Queue.Enqueued);
        Assert.Equal(0, world.Delay.Pending(TimeSpan.FromSeconds(2)));
        Assert.Equal(0, PendingTimers(coordinator));
        Assert.Equal(0, Inflight(coordinator));
        Assert.True(coordinator.DrainAsync(CancellationToken.None).IsCompleted);
        Assert.Contains(logs.Messages, x => x == $"Information {LayoutCommand} was not scheduled: the card presentation coordinator has stopped");
    }

    [Fact]
    public async Task A_layout_opened_after_the_drain_still_answers_but_starts_no_auto_flip()
    {
        using var world = TestGameplay.Run(() => Build(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4)));
        var logs = new CoordinatorLogs();
        using var coordinator = WithLogs(world, logs);
        await coordinator.DrainAsync(CancellationToken.None);

        await TestGameplay.RunAsync(async () =>
            await coordinator.OpenOnRequestAsync(world.Session, world.Run.RunId, default));
        await Task.Delay(100);

        Assert.Single(world.Sender.For(world.Session.ConnectionId).OfType<ScoreScrollStateResponse>());
        Assert.Single(world.Sender.For(world.Session.ConnectionId).OfType<CardSelectRightStateResponse>());
        Assert.True(world.Runs.Find(world.Session.CharacterId)!.CardLayoutOpen);
        Assert.Empty(world.Queue.Enqueued);
        Assert.Equal(0, world.Delay.Pending(TimeSpan.FromSeconds(4)));
        Assert.Equal(0, PendingTimers(coordinator));
        Assert.Contains(logs.Messages, x => x == $"Information {AutoFlipCommand} was not scheduled: the card presentation coordinator has stopped");
    }

    /// <summary>A second coordinator over the same world whose log is kept.</summary>
    private static CardPresentationFixture WithLogs(World world, CoordinatorLogs logs) =>
        new(world.Sessions,
            world.Runs,
            world.Bus,
            world.Sender,
            world.Projection,
            TimeProvider.System,
            world.Delay,
            logs,
            new DungeonCardPresentationTiming(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4)),
            world.Queue);

    private static int Inflight(CardPresentationFixture coordinator) =>
        Assert.IsType<int>(typeof(DungeonCardPresentationCoordinator)
            .GetField("inflight", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(coordinator.Producer));

    private sealed class CoordinatorLogs : ILogger<DungeonCardPresentationCoordinator>
    {
        public ConcurrentQueue<string> Messages { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            this.Messages.Enqueue($"{logLevel} {formatter(state, exception)}");
    }

    private static int PendingTimers(CardPresentationFixture coordinator)
    {
        var field = typeof(DungeonCardPresentationCoordinator).GetField("pending", BindingFlags.Instance | BindingFlags.NonPublic);
        var gate = typeof(DungeonCardPresentationCoordinator).GetField("gate", BindingFlags.Instance | BindingFlags.NonPublic);
        lock (Assert.IsType<object>(gate!.GetValue(coordinator.Producer)))
            return Assert.IsAssignableFrom<ICollection>(field!.GetValue(coordinator.Producer)).Count;
    }

    /// <summary>The world's own queue; it records the CMD type of every job enqueued on its executor.</summary>
    private sealed class RecordingQueue(GameplayExecutor executor) : IGameplayQueue
    {
        private readonly GameplayQueue inner = new(new GameplayShardSelector(executor));
        private readonly ConcurrentQueue<string> enqueued = new();

        public IReadOnlyList<string> Enqueued => [.. enqueued];

        public ValueTask<T> RunAsync<T>(GameplayWork<T> work, CancellationToken cancellationToken)
        {
            enqueued.Enqueue(work.CommandType);
            return inner.RunAsync(work, cancellationToken);
        }

        public ValueTask RunAsync(GameplayWork work, CancellationToken cancellationToken)
        {
            enqueued.Enqueue(work.CommandType);
            return inner.RunAsync(work, cancellationToken);
        }
    }
}
