using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Application.Execution;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dfo.Server.Application.Tests.Execution;

/// <summary>
/// The test helper hands the body to a real executor as one job (plan-review-r2 R-2); it is not a
/// token minted around the body, which would be an exemption scope in all but name.
/// </summary>
public sealed class TestGameplayTests
{
    private static readonly AsyncLocal<string?> Ambient = new();

    [Fact]
    public async Task The_body_runs_as_a_job_whose_token_dies_with_it()
    {
        Assert.False(GameplayJob.IsOnExecutor);

        var seen = await TestGameplay.RunAsync(() => Task.FromResult(
            (GameplayJob.IsOnExecutor, GameplayJob.Current, Captured: ExecutionContext.Capture()!)));

        Assert.True(seen.IsOnExecutor);
        Assert.Equal("test:" + nameof(The_body_runs_as_a_job_whose_token_dies_with_it), seen.Current!.Value.CommandType);
        Assert.False(GameplayJob.IsOnExecutor);
        bool? afterwards = null;
        ExecutionContext.Run(seen.Captured, _ => afterwards = GameplayJob.IsOnExecutor, null);
        Assert.False(afterwards);
    }

    [Fact]
    public async Task A_value_comes_out_of_the_job_and_a_failure_comes_out_as_the_same_object()
    {
        Assert.Equal(42, await TestGameplay.RunAsync(async () =>
        {
            await Task.Yield();
            return 42;
        }));

        var failure = new InvalidOperationException("thrown inside the job");
        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => TestGameplay.RunAsync(async () =>
        {
            await Task.Yield();
            throw failure;
        }));
        Assert.Same(failure, thrown);

        var skip = new SkipException("skipped inside the job");
        var skipped = await Assert.ThrowsAsync<SkipException>(() => TestGameplay.RunAsync(() => throw skip));
        Assert.Same(skip, skipped);
    }

    [Fact]
    public async Task A_nested_call_runs_inline_in_the_outer_job_and_sees_its_flow()
    {
        var (outer, inner, innerSync, ambient) = await TestGameplay.RunAsync(async () =>
        {
            Ambient.Value = "set by the outer job";
            var outerRoot = GameplayJob.Current!.Value.RootId;
            var nested = await TestGameplay.RunAsync(async () =>
            {
                await Task.Yield();
                return (GameplayJob.Current!.Value.RootId, Ambient.Value);
            });
            var nestedSync = TestGameplay.Run(() => GameplayJob.Current!.Value.RootId);
            return (outerRoot, nested.RootId, nestedSync, nested.Value);
        });

        Assert.Equal(outer, inner);
        Assert.Equal(outer, innerSync);
        Assert.Equal("set by the outer job", ambient);
    }

    [Fact]
    public async Task The_job_is_a_flow_boundary_in_both_directions()
    {
        Ambient.Value = "set outside";

        var inside = await TestGameplay.RunAsync(() =>
        {
            var seen = Ambient.Value;
            Ambient.Value = "set inside";
            return Task.FromResult(seen);
        });

        Assert.Null(inside);
        Assert.Equal("set outside", Ambient.Value);
    }

    [Fact]
    public async Task Without_a_container_two_bodies_still_run_at_the_same_time()
    {
        var (first, second) = (new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously),
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));

        // Each body waits for the other to have started: only overlapping bodies get past this.
        await Task.WhenAll(
            TestGameplay.RunAsync(() => Meet(first, second)),
            TestGameplay.RunAsync(() => Meet(second, first))).WaitAsync(ExecutionTest.Patience);
    }

    [Fact]
    public async Task A_container_without_an_executor_gives_each_call_its_own()
    {
        await using var services = new ServiceCollection().BuildServiceProvider();
        var (first, second) = (new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously),
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));

        await Task.WhenAll(
            TestGameplay.RunAsync(services, () => Meet(first, second)),
            TestGameplay.RunAsync(services, () => Meet(second, first))).WaitAsync(ExecutionTest.Patience);
    }

    [Fact]
    public async Task A_container_executor_runs_the_body_so_waiting_on_it_from_inside_is_a_self_wait()
    {
        await using var services = Container();
        var executor = services.GetRequiredService<GameplayExecutor>();

        await Assert.ThrowsAsync<GameplaySelfWaitException>(() => TestGameplay.RunAsync(services, async () =>
            await executor.RunAsync(ExecutionTest.Work(_ => ValueTask.CompletedTask), CancellationToken.None)))
            .WaitAsync(ExecutionTest.Patience);
        Assert.True(executor.IsStarted);
    }

    [Fact]
    public async Task A_container_executor_runs_two_bodies_one_after_the_other()
    {
        await using var services = Container();
        var running = 0;
        var overlapped = false;
        async Task Body()
        {
            if (Interlocked.Increment(ref running) != 1) overlapped = true;
            await Task.Delay(50);
            Interlocked.Decrement(ref running);
        }

        await Task.WhenAll(TestGameplay.RunAsync(services, Body), TestGameplay.RunAsync(services, Body))
            .WaitAsync(ExecutionTest.Patience);

        Assert.False(overlapped);
    }

    [Fact]
    public void The_synchronous_container_forms_run_on_the_container_executor()
    {
        using var services = Container();
        var executor = services.GetRequiredService<GameplayExecutor>();
        using var empty = new ServiceCollection().BuildServiceProvider();

        // Enqueuing onto the container's executor from the body is a self-wait only if the body runs there.
        Assert.Throws<GameplaySelfWaitException>(() => TestGameplay.Run(services, () =>
        {
            _ = executor.RunAsync(ExecutionTest.Work(_ => ValueTask.CompletedTask), CancellationToken.None);
        }));
        Assert.Throws<GameplaySelfWaitException>(() => TestGameplay.Run(services, () =>
            executor.RunAsync(ExecutionTest.Work(_ => ValueTask.CompletedTask), CancellationToken.None).IsCompleted));
        Assert.True(TestGameplay.Run(empty, () => GameplayJob.IsOnExecutor));
        Assert.False(executor.Completion.IsCompleted);
    }

    [Fact]
    public void The_synchronous_forms_run_on_the_executor()
    {
        bool? ranAsJob = null;
        TestGameplay.Run(() => ranAsJob = GameplayJob.IsOnExecutor);

        Assert.True(ranAsJob);
        Assert.True(TestGameplay.Run(() => GameplayJob.IsOnExecutor));
        Assert.False(GameplayJob.IsOnExecutor);
        var failure = new InvalidOperationException("thrown inside the job");
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => TestGameplay.Run(() => throw failure)));
    }

    [Fact]
    public void The_synchronous_forms_refuse_work_that_would_outlive_the_job()
    {
        var ran = false;

        Action asyncVoid = async () =>
        {
            await Task.Yield();
            ran = true;
        };

        Refused(() => TestGameplay.Run(asyncVoid));
        Refused(() => { TestGameplay.Run(async () => await Task.Yield()); });
        Refused(() => { TestGameplay.Run(() => Task.CompletedTask); });
        Refused(() => { TestGameplay.Run(() => Task.FromResult(1)); });
        Refused(() => { TestGameplay.Run(() => ValueTask.CompletedTask); });
        Refused(() => { TestGameplay.Run(() => ValueTask.FromResult(1)); });
        Assert.False(ran);
    }

    [Fact]
    public async Task An_executor_made_for_one_call_is_stopped_when_the_call_returns()
    {
        GameplayExecutor? released = null;

        await TestGameplay.RunObservedAsync(() => Task.FromResult(0), executor => released = executor);

        Assert.NotNull(released);
        Assert.True(released.Completion.IsCompleted);
        await Assert.ThrowsAsync<GameplayExecutorStoppedException>(async () =>
            await released.RunAsync(ExecutionTest.Work(_ => ValueTask.CompletedTask), CancellationToken.None));
    }

    [Fact]
    public async Task An_executor_made_for_one_synchronous_call_is_stopped_when_the_call_returns()
    {
        GameplayExecutor? released = null;

        TestGameplay.RunObserved(() => 0, executor => released = executor);

        Assert.NotNull(released);
        Assert.True(released.Completion.IsCompleted);
        await Assert.ThrowsAsync<GameplayExecutorStoppedException>(async () =>
            await released.RunAsync(ExecutionTest.Work(_ => ValueTask.CompletedTask), CancellationToken.None));
    }

    private static void Refused(Action call) => Assert.IsType<ArgumentException>(Record.Exception(call));

    private static async Task Meet(TaskCompletionSource arrived, TaskCompletionSource other)
    {
        Assert.True(GameplayJob.IsOnExecutor);
        arrived.SetResult();
        await other.Task.WaitAsync(ExecutionTest.Patience);
    }

    private static ServiceProvider Container() => new ServiceCollection()
        .AddSingleton<ILogger<GameplayExecutor>>(NullLogger<GameplayExecutor>.Instance)
        .AddGameplayExecution()
        .BuildServiceProvider();
}
