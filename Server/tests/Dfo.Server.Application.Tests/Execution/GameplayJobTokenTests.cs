using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Application.Execution;

namespace Dfo.Server.Application.Tests.Execution;

/// <summary>
/// "On the executor" is the job token, not the thread or the flow: it dies with the job, detached
/// work never inherits it, and nobody resumes on the consumer loop holding it.
/// </summary>
public sealed class GameplayJobTokenTests
{
    [Fact]
    public async Task Outside_any_job_nothing_is_on_the_executor()
    {
        var (executor, _) = ExecutionTest.Executor();
        await using var _executor = executor;
        var chain = ExecutionTest.Root();

        var seen = await executor.RunAsync(ExecutionTest.Work(
            _ => ValueTask.FromResult((GameplayJob.IsOnExecutor, GameplayJob.Current)), "Probe", chain),
            CancellationToken.None);

        Assert.False(GameplayJob.IsOnExecutor);
        Assert.Null(GameplayJob.Current);
        GameplayJob.AssertNotOnExecutor("probing outside");
        Assert.True(seen.IsOnExecutor);
        Assert.Equal(chain.Id, seen.Current!.Value.Id);
        Assert.Equal("Probe", seen.Current.Value.CommandType);
    }

    [Fact]
    public async Task A_context_captured_inside_a_job_is_off_the_executor_once_the_job_ends()
    {
        var (executor, _) = ExecutionTest.Executor();
        await using var _executor = executor;

        var captured = await executor.RunAsync(ExecutionTest.Work(_ =>
        {
            Assert.True(GameplayJob.IsOnExecutor);
            return ValueTask.FromResult(ExecutionContext.Capture()!);
        }), CancellationToken.None);

        bool? onExecutor = null;
        GameplayCallChain? current = default;
        ExecutionContext.Run(captured, _ =>
        {
            onExecutor = GameplayJob.IsOnExecutor;
            current = GameplayJob.Current;
        }, null);

        Assert.False(onExecutor);
        Assert.Null(current);
    }

    [Fact]
    public async Task Work_detached_under_suppressed_flow_is_not_on_the_executor_even_while_the_job_runs()
    {
        var (executor, _) = ExecutionTest.Executor();
        await using var _executor = executor;

        var detachedSaw = await executor.RunAsync(ExecutionTest.Work(async _ =>
        {
            Task<bool> detached;
            using (ExecutionContext.SuppressFlow())
                detached = Task.Run(() => GameplayJob.IsOnExecutor);
            var saw = await detached;
            Assert.True(GameplayJob.IsOnExecutor);
            return saw;
        }), CancellationToken.None);

        Assert.False(detachedSaw);
    }

    [Fact]
    public async Task Work_detached_with_flow_reads_off_the_executor_after_the_job_ends()
    {
        var (executor, _) = ExecutionTest.Executor();
        await using var _executor = executor;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<(bool During, bool After)>? detached = null;
        var duringObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await executor.RunAsync(ExecutionTest.Work(_ =>
        {
            detached = Task.Run(async () =>
            {
                var during = GameplayJob.IsOnExecutor;
                duringObserved.SetResult();
                await release.Task;
                return (during, GameplayJob.IsOnExecutor);
            });
            return new ValueTask(duringObserved.Task);
        }), CancellationToken.None);
        release.SetResult();
        var seen = await detached!.WaitAsync(ExecutionTest.Patience);

        Assert.True(seen.During);
        Assert.False(seen.After);
    }

    [Fact]
    public async Task The_awaiting_caller_resumes_off_the_consumer_loop_and_without_the_token()
    {
        var (executor, _) = ExecutionTest.Executor();
        await using var _executor = executor;
        var secondStarted = new ManualResetEventSlim();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = executor.RunAsync(ExecutionTest.Work(async _ =>
        {
            await gate.Task;
            return 1;
        }), CancellationToken.None);
        var second = executor.RunAsync(ExecutionTest.Work(_ =>
        {
            secondStarted.Set();
            return ValueTask.CompletedTask;
        }), CancellationToken.None);

        // The first job is held at the gate, so this registers a real continuation on its completion.
        var observed = ObserveAsync(first);
        gate.SetResult();
        var (secondRanWhileWeBlocked, onExecutor, current) = await observed.WaitAsync(ExecutionTest.Patience * 2);
        await second;

        Assert.True(secondRanWhileWeBlocked);
        Assert.False(onExecutor);
        Assert.Null(current);

        async Task<(bool, bool, GameplayCallChain?)> ObserveAsync(ValueTask<int> pending)
        {
            // No captured context: xunit's would post the continuation and hide an inline resume.
            await pending.ConfigureAwait(false);
            // Were this continuation inlined on the consumer loop, the loop could not reach the
            // second job while we block here.
            var ran = secondStarted.Wait(ExecutionTest.Patience);
            return (ran, GameplayJob.IsOnExecutor, GameplayJob.Current);
        }
    }

    [Fact]
    public async Task A_job_does_not_inherit_the_callers_async_local_context()
    {
        var (executor, _) = ExecutionTest.Executor();
        await using var _executor = executor;
        var callerValue = new AsyncLocal<string?> { Value = "caller" };

        var seen = await executor.RunAsync(ExecutionTest.Work(_ => ValueTask.FromResult(callerValue.Value)),
            CancellationToken.None);

        Assert.Null(seen);
        Assert.Equal("caller", callerValue.Value);
    }

    [Fact]
    public async Task Asserting_off_the_executor_inside_a_job_is_a_program_error_naming_the_chain()
    {
        var (executor, _) = ExecutionTest.Executor();
        await using var _executor = executor;
        var chain = ExecutionTest.Root();

        var failure = await Assert.ThrowsAsync<GameplaySelfWaitException>(async () =>
            await executor.RunAsync(ExecutionTest.Work(_ =>
            {
                GameplayJob.AssertNotOnExecutor("waiting for the network");
                return ValueTask.CompletedTask;
            }, "Sender", chain), CancellationToken.None));

        Assert.Contains("waiting for the network", failure.Message, StringComparison.Ordinal);
        Assert.Contains(chain.RootId.ToString(), failure.Message, StringComparison.Ordinal);
        Assert.Equal(chain.Id, failure.Chain.Id);
    }

    [Fact]
    public async Task A_job_that_fails_or_is_cancelled_also_leaves_its_token_invalid()
    {
        var (executor, _) = ExecutionTest.Executor();
        await using var _executor = executor;
        ExecutionContext? failedContext = null;
        ExecutionContext? cancelledContext = null;
        using var cancellation = new CancellationTokenSource();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await executor.RunAsync(ExecutionTest.Work<int>(_ =>
            {
                failedContext = ExecutionContext.Capture();
                throw new InvalidOperationException("fails");
            }), CancellationToken.None));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await executor.RunAsync(ExecutionTest.Work(_ =>
            {
                cancelledContext = ExecutionContext.Capture();
                cancellation.Cancel();
                cancellation.Token.ThrowIfCancellationRequested();
                return ValueTask.CompletedTask;
            }), cancellation.Token));

        Assert.False(ReadOnExecutor(failedContext!));
        Assert.False(ReadOnExecutor(cancelledContext!));

        static bool ReadOnExecutor(ExecutionContext context)
        {
            var seen = true;
            ExecutionContext.Run(context, _ => seen = GameplayJob.IsOnExecutor, null);
            return seen;
        }
    }

    [Fact]
    public async Task Running_detached_reads_off_the_executor_inside_and_restores_the_job_after_it()
    {
        var (executor, _) = ExecutionTest.Executor();
        await using var _executor = executor;

        var seen = await executor.RunAsync(ExecutionTest.Work(_ =>
        {
            // The shape of a transport callback that the job's own send runs inline.
            var inside = GameplayJob.RunDetached(() =>
            {
                GameplayJob.AssertNotOnExecutor("closing inline");
                return (GameplayJob.IsOnExecutor, GameplayJob.Current);
            });
            var afterValue = GameplayJob.IsOnExecutor;
            var actionSaw = true;
            GameplayJob.RunDetached(() => actionSaw = GameplayJob.IsOnExecutor);
            return ValueTask.FromResult((Inside: inside, AfterValue: afterValue, ActionSaw: actionSaw,
                AfterAction: GameplayJob.IsOnExecutor));
        }), CancellationToken.None);

        Assert.False(seen.Inside.IsOnExecutor);
        Assert.Null(seen.Inside.Current);
        Assert.True(seen.AfterValue);
        Assert.False(seen.ActionSaw);
        Assert.True(seen.AfterAction);
    }

    [Fact]
    public async Task Running_detached_restores_the_job_after_a_throw_and_across_awaits()
    {
        var (executor, _) = ExecutionTest.Executor();
        await using var _executor = executor;

        var seen = await executor.RunAsync(ExecutionTest.Work(async _ =>
        {
            try
            {
                GameplayJob.RunDetached(() => throw new InvalidOperationException("callback failed"));
            }
            catch (InvalidOperationException)
            {
            }
            var afterThrow = GameplayJob.IsOnExecutor;
            var duringAsync = await GameplayJob.RunDetachedAsync(async () =>
            {
                await Task.Yield();
                return GameplayJob.IsOnExecutor;
            });
            var afterAsync = GameplayJob.IsOnExecutor;
            var duringVoid = true;
            await GameplayJob.RunDetachedAsync(async () =>
            {
                await Task.Yield();
                duringVoid = GameplayJob.IsOnExecutor;
            });
            return (AfterThrow: afterThrow, DuringAsync: duringAsync, AfterAsync: afterAsync,
                DuringVoid: duringVoid, After: GameplayJob.IsOnExecutor);
        }), CancellationToken.None);

        Assert.True(seen.AfterThrow);
        Assert.False(seen.DuringAsync);
        Assert.True(seen.AfterAsync);
        Assert.False(seen.DuringVoid);
        Assert.True(seen.After);
    }

    [Fact]
    public async Task Running_detached_after_the_job_ended_does_not_revive_its_token()
    {
        var (executor, _) = ExecutionTest.Executor();
        await using var _executor = executor;

        var captured = await executor.RunAsync(
            ExecutionTest.Work(_ => ValueTask.FromResult(ExecutionContext.Capture()!)), CancellationToken.None);

        var seen = true;
        ExecutionContext.Run(captured, _ =>
        {
            GameplayJob.RunDetached(() => { });
            seen = GameplayJob.IsOnExecutor;
        }, null);
        var outside = GameplayJob.RunDetached(() => GameplayJob.IsOnExecutor);

        Assert.False(seen);
        Assert.False(outside);
        Assert.False(GameplayJob.IsOnExecutor);
    }

    [Fact]
    public async Task A_job_on_one_executor_may_enqueue_to_and_dispose_another()
    {
        var (first, _) = ExecutionTest.Executor();
        await using var _first = first;
        var (second, _) = ExecutionTest.Executor();

        var seen = await first.RunAsync(ExecutionTest.Work(async ct =>
        {
            var onSecond = await second.RunAsync(
                ExecutionTest.Work(_ => ValueTask.FromResult(GameplayJob.IsOnExecutor)), ct);
            // Still "on an executor": the off-executor assertion does not ask which one.
            var stillOnFirst = GameplayJob.IsOnExecutor;
            await second.DisposeAsync();
            return (OnSecond: onSecond, StillOnFirst: stillOnFirst);
        }), CancellationToken.None).AsTask().WaitAsync(ExecutionTest.Patience);

        Assert.True(seen.OnSecond);
        Assert.True(seen.StillOnFirst);
        Assert.True(second.Completion.IsCompleted);
        Assert.Equal(4, await first.RunAsync(ExecutionTest.Work(_ => ValueTask.FromResult(4)),
            CancellationToken.None));
    }

    [Fact]
    public void The_abstractions_keep_their_internals_from_the_application()
    {
        var granted = typeof(GameplayJob).Assembly
            .GetCustomAttributes(typeof(System.Runtime.CompilerServices.InternalsVisibleToAttribute), false)
            .Cast<System.Runtime.CompilerServices.InternalsVisibleToAttribute>()
            .Select(attribute => attribute.AssemblyName)
            .ToArray();

        Assert.DoesNotContain("Dfo.Server.Application", granted);
        Assert.Contains("Dfo.Server.Persistence", granted);
    }

}
