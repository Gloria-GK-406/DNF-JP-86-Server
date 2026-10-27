using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Execution;
using Dfo.Server.Application.Tests.Commands;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.Tests.Execution;

// Real typed internal commands, scopes and failure accounting behind a manually ticked production source.
public sealed class TypedGameplayTimerFailureTests
{
    [Theory]
    [InlineData("sticky")]
    [InlineData("thrown")]
    public async Task A_failed_tick_is_absorbed_with_one_error_and_one_warning_and_the_next_tick_runs(string failure)
    {
        var executorLog = new RecordingExecutionLogger<GameplayExecutor>();
        await using var world = new ProtocolCommandExecutionTests.CommandWorld(configure: services => services.AddSingleton<ILogger<GameplayExecutor>>(executorLog));
        var log = new RecordingExecutionLogger<TypedGameplayTimerFailureTests>();
        using var timer = new TypedTimerProbe(world.Bus, log);
        world.Settings.Action = _ =>
        {
            if (failure == "thrown") throw new InvalidOperationException("tick body");
            try { GameplayJob.AssertNotOnExecutor("test:sticky-program-error"); } catch (GameplaySelfWaitException) { }
            return ValueTask.FromResult(1);
        };
        await timer.Tick().WaitAsync(ExecutionTest.Patience);
        Assert.Equal(LogLevel.Error, Assert.Single(executorLog.Entries, entry => entry.Level >= LogLevel.Warning).Level);
        var warning = Assert.Single(log.Entries);
        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.Null(warning.Exception);
        Assert.Equal("timer:Test", warning.Fields["CommandType"]);
        world.Settings.Action = _ => ValueTask.FromResult(2);
        await timer.Tick().WaitAsync(ExecutionTest.Patience);
        Assert.Equal(2, world.Queue.Calls);
        Assert.All(world.Settings.Resources, resource => Assert.True(resource.Disposed));
    }

    [Fact]
    public async Task Refusal_reaches_the_loop_and_does_not_log_a_timer_warning()
    {
        await using var world = new ProtocolCommandExecutionTests.CommandWorld();
        var log = new RecordingExecutionLogger<TypedGameplayTimerFailureTests>();
        using var timer = new TypedTimerProbe(world.Bus, log);
        await world.Services.GetRequiredService<GameplayExecutor>().StopAsync(default);
        await Assert.ThrowsAsync<GameplayExecutorStoppedException>(() => timer.Tick());
        await Assert.ThrowsAsync<GameplayExecutorStoppedException>(() => timer.Tick());
        Assert.Empty(log.Entries);
        Assert.Empty(world.Settings.Seen);
    }

    [Fact]
    public async Task Producer_cancellation_reaches_the_loop_without_execution_errors()
    {
        var executorLog = new RecordingExecutionLogger<GameplayExecutor>();
        await using var world = new ProtocolCommandExecutionTests.CommandWorld(configure: services => services.AddSingleton<ILogger<GameplayExecutor>>(executorLog));
        var log = new RecordingExecutionLogger<TypedGameplayTimerFailureTests>();
        using var timer = new TypedTimerProbe(world.Bus, log);
        using var stopping = new CancellationTokenSource(); stopping.Cancel();
        var failure = new OperationCanceledException(stopping.Token);
        world.Settings.Action = _ => ValueTask.FromException<int>(failure);
        Assert.Same(failure, await Record.ExceptionAsync(() => timer.Tick(stopping.Token)));
        Assert.Empty(log.Entries);
        Assert.DoesNotContain(executorLog.Entries, entry => entry.Level >= LogLevel.Warning);
    }
}

internal sealed class TypedTimerProbe(ICommandBus commands, ILogger logger) : TypedGameplayTimerSource(commands, logger)
{
    protected override string SourceName => "timer:Test";
    protected override async Task RunRoundAsync(GameplayCallChain root, CancellationToken token) =>
        _ = await SubmitAsync(new ProtocolCommandExecutionTests.InternalProbe(), root, SourceName, token);
    public Task Tick(CancellationToken token = default) => HostedAsync(token);
    public Task Run(CancellationToken token = default) => RunAsync(token);
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.CompletedTask;
}
