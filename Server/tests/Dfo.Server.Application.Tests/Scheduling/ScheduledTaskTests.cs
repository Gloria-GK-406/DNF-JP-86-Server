using Dfo.Server.Abstractions.Scheduling;
using Dfo.Server.Application.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Collections.Concurrent;

namespace Dfo.Server.Application.Tests.Scheduling;

[Collection("Scheduler")]
public sealed class ScheduledTaskTests
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(10);
    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static async Task Until(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(Budget);
        while (!condition()) await Task.Delay(1, timeout.Token);
    }
    private static ScheduledTaskRegistration Job(string id, ScheduledTaskCallback callback,
        ScheduledTaskMissedRunPolicy policy = ScheduledTaskMissedRunPolicy.Skip) =>
        new(id, new IntervalTaskSchedule(TimeSpan.FromMinutes(1)), callback) { MissedRun = policy };
    private sealed class Running : IAsyncDisposable
    {
        public SchedulerClock Clock { get; } = new();
        public ServiceProvider Provider { get; }
        public IHostedService Worker { get; }
        public Running(IEnumerable<ScheduledTaskRegistration> jobs, int concurrency = 4,
            Action<IServiceCollection>? configure = null)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<TimeProvider>(Clock);
            services.AddTaskScheduling(new(concurrency));
            foreach (var job in jobs) services.AddScheduledTask(job);
            configure?.Invoke(services);
            Provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            Worker = Assert.Single(Provider.GetServices<IHostedService>().OfType<ScheduledTaskHostedService>());
        }
        public async Task Start() { await Worker.StartAsync(default); await Until(() => Clock.ActiveTimers > 0); }
        public async Task Tick(TimeSpan amount)
        {
            var before = Clock.TimerVersion;
            if (Clock.Advance(amount) > 0) await Until(() => Clock.TimerVersion > before);
        }
        public async ValueTask DisposeAsync()
        {
            await Worker.StopAsync(default).WaitAsync(Budget);
            await Provider.DisposeAsync();
            Assert.Equal(0, Clock.ActiveTimers);
        }
    }

    [Fact]
    public async Task Skip_waits_for_the_future_and_clock_jump_runs_only_latest_occurrence()
    {
        var calls = new ConcurrentQueue<ScheduledTaskContext>();
        await using var run = new Running([Job("skip", (context, _) => { calls.Enqueue(context); return ValueTask.CompletedTask; })]);
        await run.Start();
        Assert.Empty(calls);
        await run.Tick(TimeSpan.FromDays(3));
        await Until(() => calls.Count == 1);
        Assert.Equal(run.Clock.GetUtcNow(), Assert.Single(calls).ScheduledAt);
        await run.Tick(TimeSpan.FromSeconds(30));
        Assert.Single(calls);
    }

    [Theory]
    [InlineData(ScheduledTaskMissedRunPolicy.RunOnce, 2)]
    [InlineData(ScheduledTaskMissedRunPolicy.Skip, 1)]
    public async Task Slow_task_never_overlaps_and_coalesces_or_skips_missed_occurrences(
        ScheduledTaskMissedRunPolicy policy, int expected)
    {
        var entered = NewSignal(); var release = NewSignal(); var finished = NewSignal();
        var count = 0; var active = 0; var maximum = 0;
        await using var run = new Running([Job("slow", async (_, ct) =>
        {
            var current = Interlocked.Increment(ref active);
            maximum = Math.Max(maximum, current);
            var ordinal = Interlocked.Increment(ref count);
            try
            {
                entered.TrySetResult();
                if (ordinal == 1) await release.Task.WaitAsync(ct);
            }
            finally { Interlocked.Decrement(ref active); finished.TrySetResult(); }
        }, policy)]);
        await run.Start();
        if (policy == ScheduledTaskMissedRunPolicy.Skip) await run.Tick(TimeSpan.FromMinutes(1));
        await entered.Task.WaitAsync(Budget);
        await run.Tick(TimeSpan.FromDays(3));
        Assert.Equal(1, count);
        release.SetResult();
        await finished.Task.WaitAsync(Budget);
        if (expected == 2) await Until(() => Volatile.Read(ref count) == 2 && Volatile.Read(ref active) == 0);
        await run.Tick(TimeSpan.FromSeconds(1));
        Assert.Equal(expected, count);
        Assert.Equal(1, maximum);
    }

    [Fact]
    public async Task Global_limit_queues_one_instance_and_coalesces_waiting_runs()
    {
        var first = NewSignal(); var release = NewSignal();
        var calls = new ConcurrentQueue<ScheduledTaskContext>();
        var active = 0; var maximum = 0;
        ScheduledTaskCallback callback = async (context, ct) =>
        {
            var current = Interlocked.Increment(ref active);
            maximum = Math.Max(maximum, current);
            try
            {
                calls.Enqueue(context);
                if (calls.Count == 1) { first.SetResult(); await release.Task.WaitAsync(ct); }
            }
            finally { Interlocked.Decrement(ref active); }
        };
        await using var run = new Running([Job("a", callback, ScheduledTaskMissedRunPolicy.RunOnce),
            Job("b", callback, ScheduledTaskMissedRunPolicy.RunOnce)], 1);
        await run.Start();
        await first.Task.WaitAsync(Budget);
        var firstId = Assert.Single(calls).TaskId;
        await run.Tick(TimeSpan.FromDays(3));
        Assert.Single(calls);
        release.SetResult();
        await Until(() => calls.Count == 3 && Volatile.Read(ref active) == 0);
        await run.Tick(TimeSpan.FromSeconds(1));
        Assert.Equal(3, calls.Count); // Running task + its catch-up + one coalesced waiting task.
        Assert.Equal(run.Clock.GetUtcNow().AddSeconds(-1), Assert.Single(calls, x => x.TaskId != firstId).ScheduledAt);
        Assert.Equal(1, maximum);
    }

    private sealed class ScopedProbe : IAsyncDisposable
    {
        public bool Disposed { get; private set; }
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }

    [Fact]
    public async Task Failure_retries_are_finite_and_each_attempt_disposes_its_own_scope()
    {
        var probes = new ConcurrentQueue<ScopedProbe>(); var attempts = new ConcurrentQueue<int>();
        var succeeded = NewSignal();
        await using var run = new Running([
            Job("failing", (context, _) =>
            {
                Assert.All(probes, x => Assert.True(x.Disposed));
                probes.Enqueue(context.Services.GetRequiredService<ScopedProbe>());
                attempts.Enqueue(context.Attempt);
                throw new InvalidOperationException("intentional");
            }, ScheduledTaskMissedRunPolicy.RunOnce) with { RetryDelay = TimeSpan.Zero },
            Job("healthy", (_, _) => { succeeded.TrySetResult(); return ValueTask.CompletedTask; }, ScheduledTaskMissedRunPolicy.RunOnce)
        ], configure: services => services.AddScoped<ScopedProbe>());
        await run.Start();
        await succeeded.Task.WaitAsync(Budget);
        await Until(() => attempts.Count == 3 && probes.All(x => x.Disposed));
        Assert.Equal([1, 2, 3], attempts.ToArray());
        Assert.Equal(3, probes.Distinct().Count());
        await run.Tick(TimeSpan.FromSeconds(1));
        Assert.Equal(3, attempts.Count);
        await run.Tick(TimeSpan.FromSeconds(59));
        await Until(() => attempts.Count == 6 && probes.All(x => x.Disposed));
        Assert.Equal([1, 2, 3, 1, 2, 3], attempts.ToArray());
    }

    [Fact]
    public async Task Retry_delay_does_not_hold_the_global_execution_slot()
    {
        var failed = NewSignal(); var other = NewSignal(); var retried = NewSignal();
        var calls = 0;
        await using var run = new Running([
            Job("retry", (_, _) =>
            {
                if (Interlocked.Increment(ref calls) == 1) { failed.SetResult(); throw new InvalidOperationException(); }
                retried.TrySetResult(); return ValueTask.CompletedTask;
            }, ScheduledTaskMissedRunPolicy.RunOnce) with { RetryDelay = TimeSpan.FromMinutes(2) },
            Job("other", (_, _) => { other.TrySetResult(); return ValueTask.CompletedTask; })
        ], 1);
        await run.Start();
        await failed.Task.WaitAsync(Budget);
        await Until(() => run.Clock.ActiveTimers == 2);
        // Trigger the other job while the first job is in its retry delay.
        await run.Tick(TimeSpan.FromMinutes(1));
        await other.Task.WaitAsync(Budget);
        Assert.Equal(1, Volatile.Read(ref calls));
        await run.Tick(TimeSpan.FromMinutes(1));
        await retried.Task.WaitAsync(Budget);
    }

    /// <summary>
    /// RQ-013 reverses what this case once protected: scheduled tasks no longer run beside each
    /// other. A task held up behind a blocked one is postponed, not lost, and stopping still cancels
    /// the task that blocks (formerly <c>Different_task_can_finish_while_long_task_is_blocked_and_stop_cancels_it</c>).
    /// </summary>
    [Fact]
    public async Task Blocked_task_postpones_rather_than_loses_another_and_stop_cancels_it()
    {
        var entered = NewSignal(); var release = NewSignal(); var reentered = NewSignal(); var canceled = NewSignal();
        var blockedRuns = 0; var shortRuns = 0;
        ScopedProbe? probe = null;
        await using var run = new Running([
            Job("blocked", async (context, ct) =>
            {
                probe = context.Services.GetRequiredService<ScopedProbe>();
                if (Interlocked.Increment(ref blockedRuns) == 1) { entered.SetResult(); await release.Task.WaitAsync(ct); return; }
                reentered.TrySetResult();
                try { await Task.Delay(Timeout.Infinite, ct); }
                finally { canceled.TrySetResult(); }
            }, ScheduledTaskMissedRunPolicy.RunOnce),
            Job("short", (_, _) => { Interlocked.Increment(ref shortRuns); return ValueTask.CompletedTask; })
        ], configure: services => services.AddScoped<ScopedProbe>());
        await run.Start(); await entered.Task.WaitAsync(Budget);
        await run.Tick(TimeSpan.FromMinutes(1));
        await Task.Delay(100);
        Assert.Equal(0, Volatile.Read(ref shortRuns));
        release.SetResult();
        await Until(() => Volatile.Read(ref shortRuns) == 1);
        await reentered.Task.WaitAsync(Budget);
        await run.Worker.StopAsync(default).WaitAsync(Budget);
        await canceled.Task.WaitAsync(Budget);
        Assert.True(probe!.Disposed);
        Assert.Equal(1, Volatile.Read(ref shortRuns));
        Assert.Equal(0, run.Clock.ActiveTimers);
    }

    [Fact]
    public async Task Empty_registration_has_no_timer_or_callback()
    {
        await using var run = new Running([]);
        await run.Worker.StartAsync(default);
        Assert.Equal(0, run.Clock.ActiveTimers);
    }

    private sealed class InvalidSchedule(bool failImmediately) : IScheduledTaskSchedule
    {
        private int reads;
        public DateTimeOffset LastOccurrence(DateTimeOffset at) => at;
        public DateTimeOffset NextOccurrence(DateTimeOffset after) =>
            failImmediately || Interlocked.Increment(ref reads) > 1 ? after : after.AddMinutes(1);
    }

    [Fact]
    public void Invalid_schedule_is_rejected_at_startup()
    {
        var job = new ScheduledTaskRegistration("invalid", new InvalidSchedule(true), (_, _) => ValueTask.CompletedTask);
        Assert.Throws<InvalidOperationException>(() => new Running([job]));
    }

    [Fact]
    public async Task Runtime_schedule_failure_disables_only_that_job()
    {
        var count = 0; var invalidCount = 0;
        await using var run = new Running([
            new("invalid", new InvalidSchedule(false), (_, _) => { invalidCount++; return ValueTask.CompletedTask; }),
            Job("healthy", (_, _) => { Interlocked.Increment(ref count); return ValueTask.CompletedTask; })
        ]);
        await run.Start();
        await run.Tick(TimeSpan.FromMinutes(1));
        await Until(() => Volatile.Read(ref count) == 1);
        await run.Tick(TimeSpan.FromMinutes(1));
        await Until(() => Volatile.Read(ref count) == 2);
        Assert.Equal(0, invalidCount);
    }

    private sealed record TestScheduleConfig(TimeOnly Time);

    [Fact]
    public async Task Factory_uses_configured_singletons_and_explicit_options_override_defaults()
    {
        var clock = new SchedulerClock(); var called = NewSignal();
        var services = new ServiceCollection();
        services.AddLogging(); services.AddTaskScheduling();
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton(new TestScheduleConfig(new(6, 0)));
        services.AddScheduledTaskFactory(root => new("factory",
            new DailyTaskSchedule(root.GetRequiredService<TestScheduleConfig>().Time, TimeSpan.FromHours(8)),
            (_, _) => { called.SetResult(); return ValueTask.CompletedTask; })
        { MissedRun = ScheduledTaskMissedRunPolicy.RunOnce });
        services.AddTaskScheduling(new(1));
        services.AddTaskScheduling();
        await using var provider = services.BuildServiceProvider();
        Assert.Equal(1, provider.GetRequiredService<ScheduledTaskSchedulerOptions>().MaximumConcurrency);
        var worker = Assert.Single(provider.GetServices<IHostedService>().OfType<ScheduledTaskHostedService>());
        try { await worker.StartAsync(default); await called.Task.WaitAsync(Budget); }
        finally { await worker.StopAsync(default); }
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("attempts")]
    [InlineData("policy")]
    [InlineData("parallelism")]
    [InlineData("id")]
    public void Invalid_configuration_fails_before_callbacks(string kind)
    {
        var job = Job("valid", (_, _) => throw new InvalidOperationException("must never run"));
        var jobs = kind switch
        {
            "duplicate" => new[] { job, job },
            "attempts" => [job with { MaximumAttempts = 0 }],
            "policy" => [job with { MissedRun = (ScheduledTaskMissedRunPolicy)99 }],
            "id" => [job with { Id = " " }],
            _ => [job]
        };
        Assert.ThrowsAny<Exception>(() => new Running(jobs, kind == "parallelism" ? 0 : 4));
    }

    [Fact]
    public async Task Startup_catchup_executes_the_registered_callback_once()
    {
        var called = new TaskCompletionSource<ScheduledTaskContext>(TaskCreationOptions.RunContinuationsAsynchronously);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScheduledTask(new("test.startup", new DailyTaskSchedule(new(6, 0), TimeSpan.FromHours(8)),
            (context, ct) => { called.SetResult(context); return ValueTask.CompletedTask; })
        { MissedRun = ScheduledTaskMissedRunPolicy.RunOnce });
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var worker = Assert.Single(provider.GetServices<IHostedService>().OfType<ScheduledTaskHostedService>());
        try
        {
            await worker.StartAsync(default);
            var result = await called.Task.WaitAsync(Budget);
            Assert.Equal("test.startup", result.TaskId);
            Assert.Equal(1, result.Attempt);
            Assert.True(result.ScheduledAt <= result.StartedAt);
        }
        finally { await worker.StopAsync(default); }
    }
}

[CollectionDefinition("Scheduler", DisableParallelization = true)]
public sealed class SchedulerTestCollection;
