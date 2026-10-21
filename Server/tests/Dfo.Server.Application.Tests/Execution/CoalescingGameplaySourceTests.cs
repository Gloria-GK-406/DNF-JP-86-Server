using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Application.Execution;

namespace Dfo.Server.Application.Tests.Execution;

/// <summary>AC-004: a timer source never has two CMDs outstanding.</summary>
public sealed class CoalescingGameplaySourceTests
{
    [Fact]
    public async Task A_tick_while_the_previous_cmd_is_queued_is_coalesced_and_ticks_resume_after_it_completes()
    {
        var (executor, _) = ExecutionTest.Executor();
        await using var _executor = executor;
        var queue = new GameplayQueue(new GameplayShardSelector(executor));
        var source = new CoalescingGameplaySource(queue);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ticks = 0;

        // Occupy the executor so the first tick stays in the queue.
        var blocker = executor.RunAsync(ExecutionTest.Work(async _ => await release.Task), CancellationToken.None);
        var first = source.TryRun(Tick(), CancellationToken.None);
        var second = source.TryRun(Tick(), CancellationToken.None);
        var third = source.TryRun(Tick(), CancellationToken.None);

        Assert.Equal(GameplaySourceTickOutcome.Enqueued, first.Outcome);
        Assert.Equal(GameplaySourceTickOutcome.Coalesced, second.Outcome);
        Assert.Equal(GameplaySourceTickOutcome.Coalesced, third.Outcome);
        Assert.True(second.Completion.IsCompletedSuccessfully);
        release.SetResult();
        await blocker;
        await first.Completion.WaitAsync(ExecutionTest.Patience);
        Assert.Equal(1, ticks);

        var fourth = source.TryRun(Tick(), CancellationToken.None);
        Assert.Equal(GameplaySourceTickOutcome.Enqueued, fourth.Outcome);
        await fourth.Completion.WaitAsync(ExecutionTest.Patience);
        Assert.Equal(2, ticks);

        GameplayWork Tick() => new(GameplayShardKey.World,
            GameplayCallChain.NewRoot(GameplayCallSource.Timer), "Tick",
            _ =>
            {
                ticks++;
                return ValueTask.CompletedTask;
            });
    }

    [Fact]
    public async Task A_tick_while_the_previous_cmd_is_running_is_coalesced()
    {
        var (executor, _) = ExecutionTest.Executor();
        await using var _executor = executor;
        var source = new CoalescingGameplaySource(new GameplayQueue(new GameplayShardSelector(executor)));
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = source.TryRun(new GameplayWork(GameplayShardKey.World,
            GameplayCallChain.NewRoot(GameplayCallSource.Timer), "Tick", async _ =>
            {
                started.SetResult();
                await release.Task;
            }), CancellationToken.None);
        await started.Task.WaitAsync(ExecutionTest.Patience);

        var during = source.TryRun(Noop(), CancellationToken.None);
        release.SetResult();
        await first.Completion.WaitAsync(ExecutionTest.Patience);

        Assert.Equal(GameplaySourceTickOutcome.Coalesced, during.Outcome);
        Assert.Equal(GameplaySourceTickOutcome.Enqueued, source.TryRun(Noop(), CancellationToken.None).Outcome);
    }

    [Fact]
    public async Task A_failed_cmd_frees_the_source_and_its_failure_reaches_the_tick()
    {
        var (executor, _) = ExecutionTest.Executor();
        await using var _executor = executor;
        var source = new CoalescingGameplaySource(new GameplayQueue(new GameplayShardSelector(executor)));

        var failed = source.TryRun(new GameplayWork(GameplayShardKey.World,
            GameplayCallChain.NewRoot(GameplayCallSource.Timer), "Tick",
            _ => throw new InvalidOperationException("tick failed")), CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => failed.Completion.WaitAsync(ExecutionTest.Patience));
        var next = source.TryRun(Noop(), CancellationToken.None);
        Assert.Equal(GameplaySourceTickOutcome.Enqueued, next.Outcome);
        await next.Completion.WaitAsync(ExecutionTest.Patience);
    }

    [Fact]
    public async Task A_refused_enqueue_reports_rejected_and_frees_the_source()
    {
        var (executor, _) = ExecutionTest.Executor();
        await using var _executor = executor;
        var source = new CoalescingGameplaySource(new GameplayQueue(new GameplayShardSelector(executor)));
        await executor.StopAsync(CancellationToken.None);

        var refused = source.TryRun(Noop(), CancellationToken.None);
        var again = source.TryRun(Noop(), CancellationToken.None);

        Assert.Equal(GameplaySourceTickOutcome.Rejected, refused.Outcome);
        await Assert.ThrowsAsync<GameplayExecutorStoppedException>(() => refused.Completion);
        Assert.Equal(GameplaySourceTickOutcome.Rejected, again.Outcome);
    }

    [Fact]
    public async Task A_cmd_that_already_ran_and_failed_before_the_call_returned_is_not_reported_as_rejected()
    {
        // The queue runs the body before returning, the one case a real executor only reaches by a race.
        var source = new CoalescingGameplaySource(new InlineQueue(runBody: true));

        var tick = source.TryRun(new GameplayWork(GameplayShardKey.World,
            GameplayCallChain.NewRoot(GameplayCallSource.Timer), "Tick",
            _ => throw new InvalidOperationException("ran")), CancellationToken.None);

        Assert.Equal(GameplaySourceTickOutcome.Enqueued, tick.Outcome);
        await Assert.ThrowsAsync<InvalidOperationException>(() => tick.Completion);
        Assert.Equal(GameplaySourceTickOutcome.Enqueued, source.TryRun(Noop(), CancellationToken.None).Outcome);
    }

    [Fact]
    public async Task A_call_that_failed_without_running_the_body_is_reported_as_rejected()
    {
        var source = new CoalescingGameplaySource(new InlineQueue(runBody: false));

        var tick = source.TryRun(Noop(), CancellationToken.None);

        Assert.Equal(GameplaySourceTickOutcome.Rejected, tick.Outcome);
        await Assert.ThrowsAsync<GameplayExecutorStoppedException>(() => tick.Completion);
    }

    /// <summary>Completes every call before returning: with the body's failure, or a refusal without it.</summary>
    private sealed class InlineQueue(bool runBody) : IGameplayQueue
    {
        public ValueTask<T> RunAsync<T>(GameplayWork<T> work, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask RunAsync(GameplayWork work, CancellationToken cancellationToken)
        {
            if (!runBody) return ValueTask.FromException(new GameplayExecutorStoppedException(work.CommandType));
            try
            {
                return work.Body(cancellationToken);
            }
            catch (Exception exception)
            {
                return ValueTask.FromException(exception);
            }
        }
    }

    private static GameplayWork Noop() => new(GameplayShardKey.World,
        GameplayCallChain.NewRoot(GameplayCallSource.Timer), "Tick", _ => ValueTask.CompletedTask);
}
