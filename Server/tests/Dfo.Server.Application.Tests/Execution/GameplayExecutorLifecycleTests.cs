using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Application.Execution;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Execution;

/// <summary>Started by the first enqueue, drained by stop (RQ-009), no loop left behind, cancellation.</summary>
public sealed class GameplayExecutorLifecycleTests
{
    [Fact]
    public async Task The_first_enqueue_starts_the_consumer_loop()
    {
        var (executor, _) = ExecutionTest.Executor();
        await using var _executor = executor;

        Assert.False(executor.IsStarted);
        var result = await executor.RunAsync(ExecutionTest.Work(_ => ValueTask.FromResult(42)),
            CancellationToken.None).AsTask().WaitAsync(ExecutionTest.Patience);

        Assert.Equal(42, result);
        Assert.True(executor.IsStarted);
    }

    [Fact]
    public async Task Stopping_runs_every_cmd_already_queued_then_ends_the_loop()
    {
        var (executor, _) = ExecutionTest.Executor();
        await using var _executor = executor;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ran = new List<int>();

        var blocker = executor.RunAsync(ExecutionTest.Work(async _ => await release.Task), CancellationToken.None);
        var queued = Enumerable.Range(0, 10).Select(i => executor.RunAsync(ExecutionTest.Work(_ =>
        {
            ran.Add(i);
            return ValueTask.CompletedTask;
        }), CancellationToken.None).AsTask()).ToArray();

        var stopping = executor.StopAsync(CancellationToken.None).AsTask();
        Assert.False(stopping.IsCompleted);
        release.SetResult();
        await stopping.WaitAsync(ExecutionTest.Patience);

        await blocker;
        await Task.WhenAll(queued);
        Assert.Equal(Enumerable.Range(0, 10), ran);
        Assert.True(executor.Completion.IsCompleted);
    }

    [Fact]
    public async Task Once_stopping_begins_new_work_is_refused_distinctly()
    {
        var (executor, _) = ExecutionTest.Executor();
        await using var _executor = executor;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blocker = executor.RunAsync(ExecutionTest.Work(async _ => await release.Task), CancellationToken.None);

        var stopping = executor.StopAsync(CancellationToken.None).AsTask();
        var refusedWhileDraining = await Assert.ThrowsAsync<GameplayExecutorStoppedException>(async () =>
            await executor.RunAsync(ExecutionTest.Work(_ => ValueTask.CompletedTask), CancellationToken.None));
        release.SetResult();
        await stopping.WaitAsync(ExecutionTest.Patience);
        await blocker;
        await Assert.ThrowsAsync<GameplayExecutorStoppedException>(async () =>
            await executor.RunAsync(ExecutionTest.Work(_ => ValueTask.CompletedTask), CancellationToken.None));

        Assert.NotNull(refusedWhileDraining);
    }

    [Fact]
    public async Task Stopping_from_inside_a_job_is_a_self_wait_and_does_not_stop_the_executor()
    {
        var (executor, _) = ExecutionTest.Executor();
        await using var _executor = executor;

        await Assert.ThrowsAsync<GameplaySelfWaitException>(async () =>
            await executor.RunAsync(ExecutionTest.Work(ct => executor.StopAsync(ct)), CancellationToken.None));

        Assert.Equal(1, await executor.RunAsync(ExecutionTest.Work(_ => ValueTask.FromResult(1)),
            CancellationToken.None));
    }

    [Fact]
    public async Task Stopping_an_executor_that_never_ran_anything_completes()
    {
        var (executor, _) = ExecutionTest.Executor();

        await executor.StopAsync(CancellationToken.None).AsTask().WaitAsync(ExecutionTest.Patience);

        Assert.True(executor.Completion.IsCompleted);
        await executor.DisposeAsync();
    }

    [Fact]
    public async Task Disposing_the_container_stops_the_executor_and_leaves_no_loop_behind()
    {
        Task loop;
        await using (var provider = new ServiceCollection()
            .AddLogging()
            .AddGameplayExecution()
            .BuildServiceProvider())
        {
            var queue = provider.GetRequiredService<IGameplayQueue>();
            await queue.RunAsync(new GameplayWork(GameplayShardKey.World, ExecutionTest.Root(), "Probe",
                _ => ValueTask.CompletedTask), CancellationToken.None);
            loop = provider.GetRequiredService<GameplayExecutor>().Completion;
            Assert.False(loop.IsCompleted);
        }

        Assert.True(loop.IsCompleted);
    }

    [Fact]
    public async Task Synchronous_container_disposal_also_stops_the_executor()
    {
        Task loop;
        using (var provider = new ServiceCollection()
            .AddLogging()
            .AddGameplayExecution()
            .BuildServiceProvider())
        {
            await provider.GetRequiredService<IGameplayQueue>().RunAsync(
                new GameplayWork(GameplayShardKey.World, ExecutionTest.Root(), "Probe",
                    _ => ValueTask.CompletedTask), CancellationToken.None);
            loop = provider.GetRequiredService<GameplayExecutor>().Completion;
        }

        Assert.True(loop.IsCompleted);
    }

    [Fact]
    public async Task Disposal_that_cannot_drain_in_time_abandons_the_queued_work_and_returns()
    {
        var log = new RecordingExecutionLogger<GameplayExecutor>();
        var executor = new GameplayExecutor(log) { DisposeTimeout = TimeSpan.FromMilliseconds(100) };
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ran = false;

        var hanging = executor.RunAsync(ExecutionTest.Work(async _ =>
        {
            started.SetResult();
            await release.Task;
        }), CancellationToken.None).AsTask();
        var queued = executor.RunAsync(ExecutionTest.Work(_ =>
        {
            ran = true;
            return ValueTask.CompletedTask;
        }), CancellationToken.None).AsTask();
        await started.Task.WaitAsync(ExecutionTest.Patience);

        try
        {
            await executor.DisposeAsync().AsTask().WaitAsync(ExecutionTest.Patience);

            // The stuck job is never released here: whoever queued behind it must still get an answer.
            await Assert.ThrowsAsync<GameplayExecutorStoppedException>(() => queued.WaitAsync(ExecutionTest.Patience));
            Assert.Contains(log.Entries, entry => entry.Level == Microsoft.Extensions.Logging.LogLevel.Warning);
            Assert.False(hanging.IsCompleted);
            Assert.False(ran);
        }
        finally
        {
            release.TrySetResult();
        }
        await hanging.WaitAsync(ExecutionTest.Patience);
        await executor.Completion.WaitAsync(ExecutionTest.Patience);
        Assert.False(ran);
    }

    [Fact]
    public async Task Disposing_from_inside_a_job_is_a_self_wait()
    {
        var (executor, _) = ExecutionTest.Executor();
        await using var _executor = executor;

        await Assert.ThrowsAsync<GameplaySelfWaitException>(async () =>
            await executor.RunAsync(ExecutionTest.Work(_ => executor.DisposeAsync()), CancellationToken.None)
                .AsTask().WaitAsync(ExecutionTest.Patience));

        Assert.Equal(2, await executor.RunAsync(ExecutionTest.Work(_ => ValueTask.FromResult(2)),
            CancellationToken.None));
    }

    [Fact]
    public async Task Work_cancelled_before_it_starts_never_runs_and_its_caller_sees_cancellation()
    {
        var (executor, _) = ExecutionTest.Executor();
        await using var _executor = executor;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        var ran = false;

        var blocker = executor.RunAsync(ExecutionTest.Work(async _ => await release.Task), CancellationToken.None);
        var cancelled = executor.RunAsync(ExecutionTest.Work(_ =>
        {
            ran = true;
            return ValueTask.CompletedTask;
        }), cancellation.Token).AsTask();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled.WaitAsync(ExecutionTest.Patience));
        release.SetResult();
        await blocker;
        await executor.RunAsync(ExecutionTest.Work(_ => ValueTask.CompletedTask), CancellationToken.None);
        Assert.False(ran);
    }

    [Fact]
    public async Task Work_whose_token_is_already_cancelled_is_not_run()
    {
        var (executor, _) = ExecutionTest.Executor();
        await using var _executor = executor;
        var ran = false;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await executor.RunAsync(ExecutionTest.Work(_ =>
            {
                ran = true;
                return ValueTask.CompletedTask;
            }), new CancellationToken(canceled: true)));

        await executor.RunAsync(ExecutionTest.Work(_ => ValueTask.CompletedTask), CancellationToken.None);
        Assert.False(ran);
    }

    [Fact]
    public async Task Once_started_the_job_receives_the_callers_token()
    {
        var (executor, _) = ExecutionTest.Executor();
        await using var _executor = executor;
        using var cancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var running = executor.RunAsync(ExecutionTest.Work(async ct =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, ct);
        }), cancellation.Token).AsTask();
        await started.Task.WaitAsync(ExecutionTest.Patience);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running.WaitAsync(ExecutionTest.Patience));
        Assert.Equal(5, await executor.RunAsync(ExecutionTest.Work(_ => ValueTask.FromResult(5)),
            CancellationToken.None));
    }
}
