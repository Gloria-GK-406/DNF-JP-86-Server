using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Application.Execution;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.Tests.Execution;

/// <summary>AC-002 (enqueue half): a CMD that enqueues and waits on the executor is a program error.</summary>
public sealed class GameplayExecutorSelfWaitTests
{
    [Fact]
    public async Task Enqueueing_from_inside_a_job_throws_logs_the_chain_and_fails_the_job()
    {
        var (executor, log) = ExecutionTest.Executor();
        await using var _executor = executor;
        var outer = ExecutionTest.Root("conn-1");
        var innerRan = false;

        var failure = await Assert.ThrowsAsync<GameplaySelfWaitException>(async () =>
            await executor.RunAsync(ExecutionTest.Work(async ct =>
            {
                var inner = GameplayJob.Current!.Value.Child(GameplayCallSource.Event);
                await executor.RunAsync(ExecutionTest.Work(_ =>
                {
                    innerRan = true;
                    return ValueTask.CompletedTask;
                }, "Inner", inner), ct);
            }, "Outer", outer), CancellationToken.None).AsTask().WaitAsync(ExecutionTest.Patience));

        // The executor is still serving after the program error.
        Assert.Equal(3, await executor.RunAsync(ExecutionTest.Work(_ => ValueTask.FromResult(3)),
            CancellationToken.None));
        Assert.False(innerRan);
        Assert.Equal(outer.Id, failure.Chain.Id);
        var selfWait = Assert.Single(log.Entries,
            entry => entry.Level == LogLevel.Error && entry.Exception is null);
        Assert.Equal(outer.RootId, selfWait.Fields["RootId"]);
        Assert.Equal("Inner", selfWait.Fields["CommandType"]);
        Assert.Contains("Connection:Outer", (string)selfWait.Fields["Chain"]!, StringComparison.Ordinal);
        var jobFailure = Assert.Single(log.Entries,
            entry => entry.Level == LogLevel.Error && entry.Exception is not null);
        Assert.Same(failure, jobFailure.Exception);
        Assert.Equal("Outer", jobFailure.Fields["CommandType"]);
    }

    [Fact]
    public async Task Enqueueing_from_inside_a_job_never_reaches_the_queue()
    {
        var (executor, _) = ExecutionTest.Executor();
        await using var _executor = executor;
        var order = new List<string>();

        await Assert.ThrowsAsync<GameplaySelfWaitException>(async () =>
            await executor.RunAsync(ExecutionTest.Work(_ =>
            {
                order.Add("outer");
                // Not awaited: a real self-wait would deadlock, so even fire-and-forget must not enqueue.
                var attempt = executor.RunAsync(ExecutionTest.Work(_ =>
                {
                    order.Add("inner");
                    return ValueTask.CompletedTask;
                }), CancellationToken.None);
                return attempt;
            }), CancellationToken.None).AsTask().WaitAsync(ExecutionTest.Patience));
        await executor.RunAsync(ExecutionTest.Work(_ =>
        {
            order.Add("after");
            return ValueTask.CompletedTask;
        }), CancellationToken.None);

        Assert.Equal(new[] { "outer", "after" }, order);
    }

    [Fact]
    public async Task A_swallowed_self_wait_still_fails_the_cmd()
    {
        var (executor, log) = ExecutionTest.Executor();
        await using var _executor = executor;

        var failure = await Assert.ThrowsAsync<GameplaySelfWaitException>(async () =>
            await executor.RunAsync(ExecutionTest.Work(async ct =>
            {
                try
                {
                    await executor.RunAsync(ExecutionTest.Work(_ => ValueTask.CompletedTask, "Inner"), ct);
                }
                catch (Exception)
                {
                }
                return 7;
            }, "Swallowing"), CancellationToken.None).AsTask().WaitAsync(ExecutionTest.Patience));

        Assert.Contains("Inner", failure.Message, StringComparison.Ordinal);
        Assert.Contains(log.Entries, entry =>
            entry.Level == LogLevel.Error && ReferenceEquals(entry.Exception, failure)
            && Equals(entry.Fields["CommandType"], "Swallowing"));
    }

    [Fact]
    public async Task A_swallowed_off_executor_assertion_still_fails_the_cmd_even_if_the_body_then_throws()
    {
        var (executor, _) = ExecutionTest.Executor();
        await using var _executor = executor;

        var failure = await Assert.ThrowsAsync<GameplaySelfWaitException>(async () =>
            await executor.RunAsync(ExecutionTest.Work<int>(_ =>
            {
                try
                {
                    GameplayJob.AssertNotOnExecutor("waiting for the network");
                }
                catch (GameplaySelfWaitException)
                {
                }
                throw new InvalidOperationException("later failure");
            }), CancellationToken.None).AsTask().WaitAsync(ExecutionTest.Patience));

        Assert.Contains("waiting for the network", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_swallowed_depth_refusal_inside_a_job_still_fails_the_cmd()
    {
        var (executor, _) = ExecutionTest.Executor();
        await using var _executor = executor;
        var tooDeep = ExecutionTest.Root();
        for (var i = 0; i <= GameplayCallDepth.Limit; i++) tooDeep = tooDeep.ForCommand($"C{i}").Child(GameplayCallSource.Event);

        await Assert.ThrowsAsync<GameplayDepthExceededException>(async () =>
            await executor.RunAsync(ExecutionTest.Work(async ct =>
            {
                try
                {
                    await executor.RunAsync(ExecutionTest.Work(_ => ValueTask.CompletedTask, "TooDeep", tooDeep), ct);
                }
                catch (GameplayDepthExceededException)
                {
                }
            }), CancellationToken.None).AsTask().WaitAsync(ExecutionTest.Patience));
    }
}
