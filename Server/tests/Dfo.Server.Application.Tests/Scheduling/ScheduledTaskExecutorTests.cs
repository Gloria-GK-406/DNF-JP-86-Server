using System.Collections.Concurrent;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Scheduling;
using Dfo.Server.Application.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.Tests.Scheduling;

/// <summary>
/// Each attempt of a scheduled task is one timer CMD on the executor (spec appendix A row 5, RQ-013);
/// the retry delay and the concurrency slot stay outside it.
/// </summary>
[Collection("Scheduler")]
public sealed class ScheduledTaskExecutorTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Each_attempt_is_its_own_timer_job_and_the_retry_delay_leaves_the_executor_free()
    {
        var clock = new SchedulerClock();
        var attempts = new ConcurrentQueue<(bool OnExecutor, GameplayCallChain? Chain)>();
        var failed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var retried = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(clock);
        services.AddScheduledTask(new ScheduledTaskRegistration("probe.retry", new IntervalTaskSchedule(TimeSpan.FromDays(1)),
            (_, _) =>
            {
                attempts.Enqueue((GameplayJob.IsOnExecutor, GameplayJob.Current));
                if (attempts.Count == 1)
                {
                    failed.TrySetResult();
                    throw new InvalidOperationException("first attempt fails");
                }
                retried.TrySetResult();
                return ValueTask.CompletedTask;
            })
        { MissedRun = ScheduledTaskMissedRunPolicy.RunOnce, RetryDelay = TimeSpan.FromMinutes(2) });
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var scheduler = provider.GetServices<IHostedService>().OfType<ScheduledTaskHostedService>().Single();

        await scheduler.StartAsync(CancellationToken.None);
        try
        {
            await failed.Task.WaitAsync(Patience);
            // The retry delay is waiting on the clock, not on the executor: other work runs meanwhile.
            var ranMeanwhile = await provider.GetRequiredService<IGameplayQueue>().RunAsync(new GameplayWork<bool>(
                GameplayShardKey.World, GameplayCallChain.NewRoot(GameplayCallSource.Connection), "test:meanwhile",
                _ => ValueTask.FromResult(true)), CancellationToken.None).AsTask().WaitAsync(Patience);
            Assert.True(ranMeanwhile);
            Assert.Single(attempts);

            await Until(() => clock.ActiveTimers >= 2);
            clock.Advance(TimeSpan.FromMinutes(2));
            await retried.Task.WaitAsync(Patience);
        }
        finally
        {
            await scheduler.StopAsync(CancellationToken.None).WaitAsync(Patience);
        }

        Assert.Equal(2, attempts.Count);
        Assert.All(attempts, attempt =>
        {
            Assert.True(attempt.OnExecutor);
            var chain = Assert.NotNull(attempt.Chain);
            Assert.Equal(GameplayCallSource.Timer, chain.Source);
            Assert.Equal("timer:ScheduledTaskHostedService/probe.retry", chain.CommandType);
        });
        Assert.Equal(2, attempts.Select(x => x.Chain!.Value.RootId).Distinct().Count());
    }

    /// <summary>
    /// F-30 (TASK-31 section 2.2): the last attempt's CMD fails. The executor logs that failure with its stack;
    /// the scheduler only adds that the attempts are used up, as a warning without the stack.
    /// </summary>
    [Fact]
    public async Task An_exhausted_task_is_logged_once_by_the_executor_and_warned_without_its_stack()
    {
        var clock = new SchedulerClock();
        var logs = new CapturedLogs();
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddProvider(logs));
        services.AddSingleton<TimeProvider>(clock);
        services.AddScheduledTask(new ScheduledTaskRegistration("probe.last-attempt", new IntervalTaskSchedule(TimeSpan.FromDays(1)),
            (_, _) => throw new InvalidOperationException("the only attempt fails"))
        { MissedRun = ScheduledTaskMissedRunPolicy.RunOnce, MaximumAttempts = 1 });
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var scheduler = provider.GetServices<IHostedService>().OfType<ScheduledTaskHostedService>().Single();

        await scheduler.StartAsync(CancellationToken.None);
        try
        {
            await Until(() => logs.Entries.Any(x => x.Message.Contains("exhausted", StringComparison.Ordinal)));
        }
        finally
        {
            await scheduler.StopAsync(CancellationToken.None).WaitAsync(Patience);
        }

        var exhausted = Assert.Single(logs.Entries, x => x.Message.Contains("exhausted", StringComparison.Ordinal));
        Assert.Equal(LogLevel.Warning, exhausted.Level);
        Assert.Null(exhausted.Exception);
        var error = Assert.Single(logs.Entries, x => x.Level >= LogLevel.Error);
        Assert.StartsWith("Gameplay timer:ScheduledTaskHostedService/probe.last-attempt failed", error.Message, StringComparison.Ordinal);
        Assert.IsType<InvalidOperationException>(error.Exception);
    }

    private static async Task Until(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(Patience);
        while (!condition()) await Task.Delay(1, timeout.Token);
    }

    private sealed class CapturedLogs : ILoggerProvider
    {
        public ConcurrentQueue<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new Logger(this);

        public void Dispose() { }

        private sealed class Logger(CapturedLogs owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter) => owner.Entries.Enqueue((logLevel, formatter(state, exception), exception));
        }
    }
}
