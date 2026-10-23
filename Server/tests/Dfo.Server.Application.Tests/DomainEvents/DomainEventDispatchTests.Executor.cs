using System.Collections.Concurrent;
using System.Diagnostics;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Application.Execution;
using Dfo.Server.Application.Tests.Execution;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.Tests.DomainEvents;

/// <summary>
/// Each due delivery is consumed as one event CMD on the executor (RQ-041): the producer reads due
/// rows outside it, enqueues one CMD per delivery and waits for it before the next (RQ-043).
/// </summary>
public sealed partial class DomainEventDispatchTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);
    private const string CounterPending = "SELECT COUNT(*) FROM domain_event_deliveries WHERE subscriber_id='counter' AND state=0";

    [Fact]
    public async Task A_reliable_delivery_is_one_event_cmd_and_its_business_write_commits_with_its_completion()
    {
        await using var f = new Fixture(record: true);
        var beforeCommit = new ConcurrentQueue<(long Count, long Pending)>();
        f.Probe.Observe = at =>
        {
            if (at == "counter") beforeCommit.Enqueue((f.Count("counter"), f.Scalar(CounterPending)));
        };
        await f.SendAsync(new Awarded("a", 4));

        Assert.Equal(1, await f.Dispatcher.DrainOnceAsync("counter"));

        var cmd = Assert.Single(f.Queue.Ran, x => x.CommandType == "event:counter");
        Assert.Equal(GameplayCallSource.Event, cmd.Source);
        var handled = Assert.Single(f.Probe.CounterSeen);
        Assert.True(handled.OnExecutor);
        Assert.Equal(cmd.Current!.Value.Id, handled.Current!.Value.Id);
        Assert.Equal("event:counter", handled.Current!.Value.CommandType);
        var persisted = f.Probe.Persisted.Where(x => x.Subscriber == "counter").ToArray();
        Assert.Equal(["TryBegin", "Complete"], persisted.Select(x => x.Method));
        Assert.All(persisted, x => Assert.True(x.OnExecutor));
        Assert.All(persisted, x => Assert.Equal(handled.Current!.Value.Id, x.Current!.Value.Id));
        Assert.Equal((0L, 1L), Assert.Single(beforeCommit));
        Assert.Equal(4, f.Count("counter"));
        Assert.Equal(0, f.Scalar(CounterPending));
    }

    [Fact]
    public async Task A_live_hint_commits_its_consumption_first_and_projects_later_in_the_same_event_cmd()
    {
        await using var f = new Fixture(record: true);
        var stateWhenProjected = new ConcurrentQueue<long>();
        f.Probe.Observe = at =>
        {
            if (at == "live")
                stateWhenProjected.Enqueue(f.Scalar("SELECT state FROM domain_event_deliveries WHERE subscriber_id='live'"));
        };
        await f.SendAsync(new Awarded("a", 1));

        Assert.Equal(1, await f.Dispatcher.DrainOnceAsync("live"));

        var projected = Assert.Single(f.Probe.LiveSeen);
        Assert.True(projected.OnExecutor);
        Assert.False(projected.InTransaction);
        Assert.Equal("event:live", projected.Current!.Value.CommandType);
        Assert.Equal(1, Assert.Single(stateWhenProjected));
        var completed = Assert.Single(f.Probe.Persisted, x => x.Subscriber == "live" && x.Method == "Complete");
        Assert.Equal(completed.Current!.Value.Id, projected.Current!.Value.Id);
        Assert.Single(f.Probe.Live);
    }

    [Fact]
    public async Task A_failed_attempt_records_its_retry_in_its_own_event_cmd_and_the_retry_is_another_cmd()
    {
        await using (var f = new Fixture(record: true))
        {
            f.Clock.Frozen = DateTimeOffset.UtcNow;
            f.Probe.FailingAttempts = 1;
            await f.SendAsync(new Awarded("a", 4));

            Assert.Equal(1, await f.Dispatcher.DrainOnceAsync("counter"));
            Assert.Equal(0, f.Count("counter"));
            var failed = f.Probe.Persisted.Where(x => x.Subscriber == "counter").ToArray();
            Assert.Equal(["TryBegin", "Fail"], failed.Select(x => x.Method));
            Assert.All(failed, x => Assert.True(x.OnExecutor));
            Assert.Equal(failed[0].Current!.Value.Id, failed[1].Current!.Value.Id);

            f.Clock.Frozen += TimeSpan.FromMinutes(1);
            Assert.Equal(1, await f.Dispatcher.DrainOnceAsync("counter"));
            Assert.Equal(4, f.Count("counter"));
            var retried = f.Probe.Persisted.Where(x => x.Subscriber == "counter").Skip(2).ToArray();
            Assert.Equal(["TryBegin", "Complete"], retried.Select(x => x.Method));
            Assert.Equal(retried[0].Current!.Value.Id, retried[1].Current!.Value.Id);
            Assert.NotEqual(failed[0].Current!.Value.Id, retried[0].Current!.Value.Id);
            Assert.Equal(GameplayCallSource.Event, retried[0].Current!.Value.Source);
        }

        await using (var f = new Fixture(record: true))
        {
            f.Probe.FailingAttempts = 1;
            f.Probe.FailRetryRecordingOnce = 1;
            await f.SendAsync(new Awarded("a", 8));

            await Assert.ThrowsAsync<IOException>(() => f.Dispatcher.DrainOnceAsync("counter").AsTask());
            Assert.Single(f.Logs.Entries, x => x.Level == LogLevel.Error
                && x.Message.StartsWith("Gameplay event:counter failed", StringComparison.Ordinal));
            Assert.Equal(1, f.Scalar(CounterPending));
        }
    }

    [Fact]
    public async Task Deliveries_left_by_an_earlier_process_run_as_startup_recovery_and_new_facts_as_event_cmds()
    {
        await using var earlier = new Fixture();
        await earlier.SendAsync(new Awarded("a", 5));
        await using var f = new Fixture(path: earlier.Path, record: true);
        using var stop = new CancellationTokenSource();
        var worker = f.Dispatcher.RunAsync(stop.Token);
        await f.Probe.CounterCommitted.Task.WaitAsync(Patience);
        Assert.True(await f.Queue.WaitForAsync(x => x.CommandType == "startup:event/audit" && x.Completed, Patience));
        Assert.True(await f.Queue.WaitForAsync(x => x.CommandType == "startup:event/live" && x.Completed, Patience));

        await f.SendAsync(new Awarded("b", 2));
        Assert.True(await f.Queue.WaitForAsync(x => x.CommandType == "event:counter" && x.Completed, Patience));
        Assert.True(await f.Queue.WaitForAsync(x => x.CommandType == "event:live" && x.Completed, Patience));
        await stop.CancelAsync();
        await worker.WaitAsync(Patience);

        foreach (var group in new[] { "counter", "audit", "live" })
        {
            var recovered = Assert.Single(f.Queue.Ran, x => x.CommandType == "startup:event/" + group);
            Assert.Equal(GameplayCallSource.StartupRecovery, recovered.Source);
            Assert.True(recovered.OnExecutor);
        }
        Assert.Equal(GameplayCallSource.Event, Assert.Single(f.Queue.Ran, x => x.CommandType == "event:counter").Source);
        Assert.Equal("b", Assert.Single(f.Probe.Live).Owner);
        Assert.Equal(1, f.Scalar("SELECT COUNT(*) FROM domain_event_deliveries WHERE subscriber_id='live' " +
            "AND state=1 AND last_error='Previous process instance; live hint skipped.'"));
        Assert.Equal(7, f.Count("counter"));
    }

    [Fact]
    public async Task A_drain_of_several_due_deliveries_runs_one_event_cmd_per_delivery()
    {
        await using var f = new Fixture(record: true);
        foreach (var owner in new[] { "a", "b", "c" }) await f.SendAsync(new Awarded(owner, 1));

        var processed = await f.Dispatcher.DrainOnceAsync();

        Assert.Equal(9, processed);
        Assert.Equal(processed, f.Queue.Ran.Count);
        foreach (var group in new[] { "counter", "audit", "live" })
            Assert.Equal(3, f.Queue.Ran.Count(x => x.CommandType == "event:" + group));
        var begun = f.Probe.Persisted.Where(x => x.Method == "TryBegin").ToArray();
        Assert.Equal(processed, begun.Length);
        Assert.Equal(processed, begun.Select(x => x.Current!.Value.Id).Distinct().Count());
        Assert.Equal(3, f.Count("counter"));
        Assert.Equal(3, f.Probe.Live.Count);
    }

    [Fact]
    public async Task A_group_stays_serial_and_consumes_later_facts_while_a_failed_one_backs_off()
    {
        await using var f = new Fixture(record: true);
        f.Queue.Watched = "event:counter";
        f.Clock.Frozen = DateTimeOffset.UtcNow;
        f.Probe.FailingAttempts = 1;
        // The group's own drain loop does the work; every commit below wakes it.
        using var stop = new CancellationTokenSource();
        var worker = f.Dispatcher.RunAsync(stop.Token);
        await f.SendAsync(new Awarded("a", 4));
        Assert.True(await WaitUntilAsync(() => f.Scalar(CounterPending + " AND attempt_count=1") == 1));

        await f.SendAsync(new Awarded("a", 6));
        Assert.True(await WaitUntilAsync(() => f.Count("counter") == 6));
        Assert.Equal(6, f.Count("counter"));
        Assert.Equal(1, f.Scalar(CounterPending + " AND attempt_count=1"));

        // The backed-off fact comes due first; the next commit wakes the loop for it and the four new ones.
        f.Clock.Frozen += TimeSpan.FromMinutes(1);
        for (var i = 0; i < 4; i++) await f.SendAsync(new Awarded("a", 1));
        Assert.True(await WaitUntilAsync(() => f.Count("counter") == 14 && f.Scalar(CounterPending) == 0));

        Assert.Equal(14, f.Count("counter"));
        Assert.Equal(0, f.Scalar(CounterPending));
        Assert.Equal(1, f.Queue.MaximumOutstanding);
        await stop.CancelAsync();
        await worker.WaitAsync(Patience);
    }

    /// <summary>
    /// A drain from a test thread may run beside the group's own loop (the GM host tests do). What keeps
    /// that correct is the claim, not a gate: a delivery is taken only while it is pending and due.
    /// </summary>
    [Fact]
    public async Task Concurrent_drains_of_one_group_consume_each_delivery_once_and_never_retry_a_backed_off_one_early()
    {
        await using var f = new Fixture(record: true);
        f.Clock.Frozen = DateTimeOffset.UtcNow;
        f.Probe.FailingAttempts = 1;
        await f.SendAsync(new Awarded("a", 4));

        // Two drains read the same due fact before either consumes it. The first fails it and backs it off;
        // the second finds it backed off and leaves it.
        var held = new ExecutorBlocker();
        await held.HoldAsync(f.Queue, Patience);
        var before = f.Queue.EnqueuedCount("event:counter");
        var racing = Enumerable.Range(0, 2).Select(_ => f.Dispatcher.DrainOnceAsync("counter").AsTask()).ToArray();
        Assert.True(await WaitUntilAsync(() => f.Queue.EnqueuedCount("event:counter") == before + 2));
        await held.ReleaseAsync(Patience);
        await Task.WhenAll(racing).WaitAsync(Patience);
        Assert.Equal(1, f.Probe.CounterAttempts);
        Assert.Equal(1, f.Scalar(CounterPending + " AND attempt_count=1"));

        // Three later facts, read together by two drains and the running group loop.
        for (var i = 0; i < 3; i++) await f.SendAsync(new Awarded("a", 1));
        var heldAgain = new ExecutorBlocker();
        await heldAgain.HoldAsync(f.Queue, Patience);
        before = f.Queue.EnqueuedCount("event:counter");
        using var stop = new CancellationTokenSource();
        var worker = f.Dispatcher.RunAsync(stop.Token);
        racing = Enumerable.Range(0, 2).Select(_ => f.Dispatcher.DrainOnceAsync("counter").AsTask()).ToArray();
        Assert.True(await WaitUntilAsync(() => f.Queue.EnqueuedCount("event:counter") == before + 3));
        await heldAgain.ReleaseAsync(Patience);
        await Task.WhenAll(racing).WaitAsync(Patience);
        Assert.True(await WaitUntilAsync(() => f.Count("counter") == 3 && f.Scalar(CounterPending) == 1));
        await stop.CancelAsync();
        await worker.WaitAsync(Patience);

        var counter = f.Probe.Persisted.Where(x => x.Subscriber == "counter").ToArray();
        Assert.Equal(3, counter.Count(x => x.Method == "Complete"));
        Assert.Equal(1, counter.Count(x => x.Method == "Fail"));
        Assert.True(counter.Count(x => x.Method == "TryBegin") > 4, "the drains never competed for a delivery");
        Assert.Equal(4, f.Probe.CounterAttempts);
        Assert.Equal(3, f.Count("counter"));
        Assert.Equal(1, f.Scalar(CounterPending + " AND attempt_count=1"));
    }

    [Fact]
    public async Task An_event_cmd_queued_when_the_loop_stops_still_runs_and_leaves_its_delivery_pending()
    {
        await using var f = new Fixture(record: true);
        await f.SendAsync(new Awarded("a", 3));
        var blocker = new ExecutorBlocker();
        await blocker.HoldAsync(f.Queue, Patience);
        using var stop = new CancellationTokenSource();
        var worker = f.Dispatcher.RunAsync(stop.Token);
        Assert.True(await WaitUntilAsync(() => f.Queue.EnqueuedCount("event:counter") == 1));

        await stop.CancelAsync();
        await blocker.ReleaseAsync(Patience);
        await worker.WaitAsync(Patience);

        Assert.Contains(f.Queue.Ran, x => x.CommandType == "event:counter" && x.OnExecutor);
        Assert.Equal(0, f.Count("counter"));
        Assert.Equal(1, f.Scalar(CounterPending + " AND attempt_count=0"));
        Assert.DoesNotContain(f.Logs.Entries, x => x.Message.StartsWith("Gameplay event:", StringComparison.Ordinal)
            && x.Message.Contains(" failed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Group_loops_end_quietly_when_the_executor_has_already_stopped()
    {
        await using var f = new Fixture(record: true);
        await f.SendAsync(new Awarded("a", 3));
        await f.Services.GetRequiredService<GameplayExecutor>().StopAsync(default);

        await f.Dispatcher.RunAsync(CancellationToken.None).WaitAsync(Patience);

        Assert.Equal(3, f.Logs.Entries.Count(x => x.Level == LogLevel.Information
            && x.Message.Contains("the gameplay executor has stopped", StringComparison.Ordinal)));
        Assert.DoesNotContain(f.Logs.Entries, x => x.Message.Contains("could not persist or scan", StringComparison.Ordinal));
        Assert.Equal(3, f.Scalar("SELECT COUNT(*) FROM domain_event_deliveries WHERE state=0"));
    }

    [Fact]
    public async Task Draining_from_inside_a_job_fails_at_once_as_a_program_error()
    {
        await using var f = new Fixture(record: true);
        await f.SendAsync(new Awarded("a", 3));
        var holding = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Exception? caught = null;
        var elapsed = TimeSpan.MaxValue;
        var job = TestGameplay.RunAsync(f.Services, async () =>
        {
            holding.TrySetResult();
            // The counter loop is now waiting on a CMD queued behind this job.
            if (!await WaitUntilAsync(() => f.Queue.EnqueuedCount("event:counter") == 1)) return;
            var watch = Stopwatch.StartNew();
            caught = await Record.ExceptionAsync(() => f.Dispatcher.DrainOnceAsync("counter").AsTask().WaitAsync(Patience));
            elapsed = watch.Elapsed;
        });
        await holding.Task.WaitAsync(Patience);
        using var stop = new CancellationTokenSource();
        var worker = f.Dispatcher.RunAsync(stop.Token);

        await Assert.ThrowsAsync<GameplaySelfWaitException>(() => job);
        Assert.IsType<GameplaySelfWaitException>(caught);
        Assert.True(elapsed < TimeSpan.FromSeconds(1), $"The refusal took {elapsed}.");
        await f.Probe.CounterCommitted.Task.WaitAsync(Patience);
        await stop.CancelAsync();
        await worker.WaitAsync(Patience);
    }

    [Fact]
    public async Task A_direct_subscriber_runs_inside_the_calling_cmd_and_enqueues_nothing()
    {
        await using var f = new Fixture(record: true);
        GameplayCallChain? caller = null;
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            caller = GameplayJob.Current;
            await using var tx = f.Scope.Begin();
            await f.Events.DirectAsync(new Awarded("a", 1));
            await tx.CommitAsync(default);
        });

        var seen = Assert.Single(f.Probe.DirectSeen);
        Assert.True(seen.OnExecutor);
        Assert.Equal(caller!.Value.Id, seen.Current!.Value.Id);
        Assert.DoesNotContain(f.Queue.Enqueued, x => x.CommandType.StartsWith("event:", StringComparison.Ordinal));
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition)
    {
        var deadline = Stopwatch.StartNew();
        while (!condition())
        {
            if (deadline.Elapsed > Patience) return false;
            await Task.Delay(5);
        }
        return true;
    }

    private sealed record LogEntry(LogLevel Level, string Message);

    private sealed class LogRecorder : ILoggerProvider
    {
        public ConcurrentQueue<LogEntry> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new Logger(this);

        public void Dispose() { }

        private sealed class Logger(LogRecorder owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter) => owner.Entries.Enqueue(new(logLevel, formatter(state, exception)));
        }
    }
}
