using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Application.Execution;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.Tests.Execution;

/// <summary>
/// One tick of a timer loop: a CMD that failed is absorbed with one Warning beside the executor's
/// Error (F-30), so the loop goes on; a refusal and the caller's cancellation still reach the loop.
/// </summary>
public sealed class GameplayTimerWorkTests
{
    public static TheoryData<string> Failures => new() { "sticky", "thrown" };

    [Theory]
    [MemberData(nameof(Failures))]
    public async Task A_tick_whose_cmd_fails_is_absorbed_with_one_error_and_one_warning_without_the_exception(string failure)
    {
        var (executor, executorLog) = ExecutionTest.Executor();
        await using var _ = executor;
        var log = new RecordingExecutionLogger<GameplayTimerWorkTests>();
        var ticks = new CoalescingGameplaySource(new GameplayQueue(new GameplayShardSelector(executor)));

        await GameplayTimerWork.TickAsync(ticks, "timer:Test", _ =>
        {
            if (failure == "thrown") throw new InvalidOperationException("tick body");
            try { GameplayJob.AssertNotOnExecutor("test:sticky-program-error"); }
            catch (GameplaySelfWaitException) { }
            return ValueTask.CompletedTask;
        }, log, CancellationToken.None).WaitAsync(ExecutionTest.Patience);

        var error = Assert.Single(executorLog.Entries, x => x.Level >= LogLevel.Warning);
        Assert.Equal(LogLevel.Error, error.Level);
        var warning = Assert.Single(log.Entries);
        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.Null(warning.Exception);
        Assert.Equal("timer:Test", warning.Fields["CommandType"]);
        // The source is free again: the next tick is enqueued, not coalesced.
        var next = ticks.TryRun(ExecutionTest.Work(_ => ValueTask.CompletedTask), CancellationToken.None);
        Assert.Equal(GameplaySourceTickOutcome.Enqueued, next.Outcome);
        await next.Completion.WaitAsync(ExecutionTest.Patience);
    }

    [Fact]
    public async Task A_refused_tick_is_still_thrown_to_the_loop_and_logs_nothing_here()
    {
        var (executor, _) = ExecutionTest.Executor();
        var log = new RecordingExecutionLogger<GameplayTimerWorkTests>();
        var ticks = new CoalescingGameplaySource(new GameplayQueue(new GameplayShardSelector(executor)));
        await executor.DisposeAsync();

        await Assert.ThrowsAsync<GameplayExecutorStoppedException>(() =>
            GameplayTimerWork.TickAsync(ticks, "timer:Test", _ => ValueTask.CompletedTask, log, CancellationToken.None)
                .WaitAsync(ExecutionTest.Patience));
        Assert.Empty(log.Entries);
    }

    [Fact]
    public async Task A_tick_cancelled_by_its_loop_is_still_thrown_as_a_cancellation()
    {
        var (executor, _) = ExecutionTest.Executor();
        await using var _executor = executor;
        var log = new RecordingExecutionLogger<GameplayTimerWorkTests>();
        var ticks = new CoalescingGameplaySource(new GameplayQueue(new GameplayShardSelector(executor)));
        using var stopping = new CancellationTokenSource();
        stopping.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            GameplayTimerWork.TickAsync(ticks, "timer:Test", token =>
            {
                token.ThrowIfCancellationRequested();
                return ValueTask.CompletedTask;
            }, log, stopping.Token).WaitAsync(ExecutionTest.Patience));
        Assert.Empty(log.Entries);
    }
}
