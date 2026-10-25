using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Execution;
using Dfo.Server.Application.Tests.Commands;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.Tests.Execution;

public sealed class TimerCancellationBoundaryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Producer_cancellation_is_rethrown_after_completion_and_current_typed_entry_logs_it_as_a_job_failure(bool typed)
    {
        using var producer = new CancellationTokenSource();
        producer.Cancel();
        var cause = new OperationCanceledException("timer producer cancellation", producer.Token);
        var log = new RecordingExecutionLogger<GameplayExecutor>();
        await using var world = new ProtocolCommandExecutionTests.CommandWorld(configure: services =>
            services.AddSingleton<ILogger<GameplayExecutor>>(log));
        bool? queueTokenCancelled = null;
        world.Settings.Action = _ => ValueTask.FromException<int>(cause);

        var failure = await Record.ExceptionAsync(async () =>
        {
            if (typed)
                await world.Bus.SendInternalAsync(new ProtocolCommandExecutionTests.InternalProbe(),
                    new(GameplayCallSource.Timer, GameplayCallChain.NewRoot(GameplayCallSource.Timer),
                        "timer:cancellation-control", InternalCommandCancellation.BodyOnly), producer.Token);
            else
                await GameplayTimerWork.RunAsync(world.Services.GetRequiredService<IGameplayQueue>(),
                    "timer:cancellation-control", token =>
                    {
                        queueTokenCancelled = token.IsCancellationRequested;
                        return ValueTask.FromException(cause);
                    }, producer.Token);
        });

        Assert.Same(cause, failure);
        Assert.Equal(1, world.Queue.Calls);
        Assert.Equal(typed, GameplayJob.EndedAJob(cause));
        Assert.Equal(typed ? 1 : 0, log.Entries.Count(entry => entry.Level == LogLevel.Error));
        if (typed)
        {
            Assert.True(Assert.Single(world.Settings.Resources).Disposed);
            Assert.True(Assert.Single(world.Settings.Seen).Request!.IsClosed);
        }
        else Assert.True(queueTokenCancelled);
        Assert.Empty(world.Outbound.Entries);
    }
}
