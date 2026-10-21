using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Application.Execution;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.Tests.Execution;

/// <summary>RQ-004 beyond the job body: nothing a producer or a log provider does may stop the loop.</summary>
public sealed class GameplayExecutorRobustnessTests
{
    [Theory]
    [InlineData(LogLevel.Trace, false)]
    [InlineData(LogLevel.Debug, false)]
    [InlineData(LogLevel.Warning, false)]
    [InlineData(LogLevel.Error, false)]
    [InlineData(LogLevel.Critical, false)]
    [InlineData(LogLevel.Trace, true)]
    public async Task A_throwing_log_provider_changes_no_outcome_and_stops_nothing(LogLevel throwingLevel, bool everyLevel)
    {
        var executor = new GameplayExecutor(new ThrowingLogger(throwingLevel, everyLevel))
        {
            DisposeTimeout = TimeSpan.FromMilliseconds(200),
        };
        await using var _executor = executor;

        Assert.Equal(1, await Run(_ => ValueTask.FromResult(1)));
        var own = await Assert.ThrowsAsync<InvalidOperationException>(() => Run<int>(_ => throw new InvalidOperationException("own")));
        await Assert.ThrowsAsync<GameplaySelfWaitException>(() => Run<int>(async ct =>
        {
            await executor.RunAsync(ExecutionTest.Work(_ => ValueTask.CompletedTask), ct);
            return 0;
        }));
        var deep = ExecutionTest.Root();
        for (var i = 0; i <= GameplayCallDepth.Limit; i++) deep = deep.Child(GameplayCallSource.Event);
        await Assert.ThrowsAsync<GameplayDepthExceededException>(() =>
            executor.RunAsync(ExecutionTest.Work(_ => ValueTask.CompletedTask, chain: deep), CancellationToken.None)
                .AsTask().WaitAsync(ExecutionTest.Patience));
        Assert.Equal(2, await Run(_ => ValueTask.FromResult(2)));

        Assert.Equal("own", own.Message);
        Assert.False(executor.Completion.IsCompleted);

        Task<T> Run<T>(Func<CancellationToken, ValueTask<T>> body) =>
            executor.RunAsync(ExecutionTest.Work(body), CancellationToken.None).AsTask().WaitAsync(ExecutionTest.Patience);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public async Task Work_without_a_cmd_type_is_refused_at_enqueue_and_the_executor_keeps_serving(string? commandType)
    {
        var (executor, _) = ExecutionTest.Executor();
        await using var _executor = executor;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ran = false;

        // With the executor busy, only a refusal at enqueue can have completed by the time the call returns.
        var blocker = executor.RunAsync(ExecutionTest.Work(async _ => await release.Task), CancellationToken.None);
        var refused = executor.RunAsync(new GameplayWork(GameplayShardKey.World, ExecutionTest.Root(), commandType!, _ =>
        {
            ran = true;
            return ValueTask.CompletedTask;
        }), CancellationToken.None);
        var refusedAtEnqueue = refused.IsFaulted;
        release.SetResult();
        await blocker;

        Assert.True(refusedAtEnqueue);
        await Assert.ThrowsAsync<ArgumentException>(() => refused.AsTask());
        Assert.Equal(3, await executor.RunAsync(ExecutionTest.Work(_ => ValueTask.FromResult(3)),
            CancellationToken.None).AsTask().WaitAsync(ExecutionTest.Patience));
        Assert.False(ran);
    }

    [Fact]
    public async Task A_faulted_consumer_loop_is_logged_critical_ends_and_refuses_later_work()
    {
        var (executor, log) = ExecutionTest.Executor();
        await using var _executor = executor;
        await executor.RunAsync(ExecutionTest.Work(_ => ValueTask.CompletedTask), CancellationToken.None);

        // Jobs contain their own failures, so the loop can only be faulted from outside: through its channel.
        var queue = typeof(GameplayExecutor)
            .GetField("queue", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(executor)!;
        var writer = queue.GetType().GetProperty("Writer")!.GetValue(queue)!;
        var fault = new InvalidOperationException("loop fault");
        writer.GetType().GetMethod("TryComplete")!.Invoke(writer, [fault]);

        await executor.Completion.WaitAsync(ExecutionTest.Patience);
        var later = await Assert.ThrowsAsync<GameplayExecutorStoppedException>(() =>
            executor.RunAsync(ExecutionTest.Work(_ => ValueTask.CompletedTask), CancellationToken.None).AsTask());

        var critical = Assert.Single(log.Entries, entry => entry.Level == LogLevel.Critical);
        Assert.Same(fault, critical.Exception);
        Assert.NotNull(later);
    }

    private sealed class ThrowingLogger(LogLevel level, bool everyLevel) : ILogger<GameplayExecutor>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) =>
            everyLevel ? throw new InvalidOperationException("provider IsEnabled") : true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (everyLevel || logLevel == level) throw new InvalidOperationException($"provider {logLevel}");
        }
    }
}
