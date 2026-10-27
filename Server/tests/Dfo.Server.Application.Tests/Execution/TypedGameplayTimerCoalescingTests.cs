using Dfo.Server.Application.Execution;
using Dfo.Server.Application.Tests.Commands;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dfo.Server.Application.Tests.Execution;

public sealed class TypedGameplayTimerCoalescingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_tick_while_the_previous_typed_command_is_queued_or_running_is_coalesced(bool running)
    {
        await using var world = new ProtocolCommandExecutionTests.CommandWorld();
        using var source = new TypedTimerProbe(world.Bus, NullLogger.Instance);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task blocker = Task.CompletedTask;
        if (running)
            world.Settings.Action = async _ => { started.SetResult(); await release.Task; return 1; };
        else
            blocker = world.Services.GetRequiredService<GameplayExecutor>().RunAsync(ExecutionTest.Work(async _ => { started.SetResult(); await release.Task; }), default).AsTask();
        var first = source.Tick();
        await started.Task.WaitAsync(ExecutionTest.Patience);
        var second = source.Tick(); var third = source.Tick();
        Assert.True(second.IsCompletedSuccessfully); Assert.True(third.IsCompletedSuccessfully);
        Assert.Equal(1, world.Queue.Calls);
        release.SetResult(); await blocker; await first.WaitAsync(ExecutionTest.Patience);
        world.Settings.Action = _ => ValueTask.FromResult(2);
        await source.Tick().WaitAsync(ExecutionTest.Patience);
        Assert.Equal(2, world.Queue.Calls); Assert.Equal(2, world.Settings.Seen.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Immediate_or_asynchronous_failure_releases_the_source_and_reaches_the_direct_caller(bool thrown)
    {
        await using var world = new ProtocolCommandExecutionTests.CommandWorld();
        using var source = new TypedTimerProbe(world.Bus, NullLogger.Instance);
        var failure = new InvalidOperationException("ran");
        world.Settings.Action = _ => thrown ? throw failure : ValueTask.FromException<int>(failure);
        Assert.Same(failure, await Record.ExceptionAsync(() => source.Run()));
        world.Settings.Action = _ => ValueTask.FromResult(1);
        await source.Tick().WaitAsync(ExecutionTest.Patience);
        Assert.Equal(2, world.Queue.Calls);
    }

    [Fact]
    public async Task A_drained_source_refuses_new_ticks_without_enqueuing()
    {
        await using var world = new ProtocolCommandExecutionTests.CommandWorld();
        using var source = new TypedTimerProbe(world.Bus, NullLogger.Instance);
        await source.DrainAsync(default);
        await Assert.ThrowsAsync<Dfo.Server.Abstractions.Execution.GameplayExecutorStoppedException>(() => source.Tick());
        Assert.Equal(0, world.Queue.Calls);
    }
}
