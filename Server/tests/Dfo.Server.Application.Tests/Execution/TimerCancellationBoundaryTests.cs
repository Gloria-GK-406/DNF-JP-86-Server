using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Execution;
using Dfo.Server.Application.Tests.Commands;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.Tests.Execution;

public sealed class TimerCancellationBoundaryTests
{
    [Fact]
    public async Task Producer_cancellation_is_rethrown_after_typed_completion_with_the_original_nonfailure_policy()
    {
        using var producer = new CancellationTokenSource();
        producer.Cancel();
        var cause = new OperationCanceledException("timer producer cancellation", producer.Token);
        var log = new RecordingExecutionLogger<GameplayExecutor>();
        await using var world = new ProtocolCommandExecutionTests.CommandWorld(configure: services =>
            services.AddSingleton<ILogger<GameplayExecutor>>(log));
        world.Settings.Action = _ => ValueTask.FromException<int>(cause);

        var failure = await Record.ExceptionAsync(async () =>
        {
            await world.Bus.SendInternalAsync(new ProtocolCommandExecutionTests.InternalProbe(),
                new(GameplayCallSource.Timer, GameplayCallChain.NewRoot(GameplayCallSource.Timer),
                    "timer:cancellation-control", InternalCommandCancellation.BodyOnly), producer.Token);
        });

        Assert.Same(cause, failure);
        Assert.Equal(1, world.Queue.Calls);
        Assert.False(GameplayJob.EndedAJob(cause));
        Assert.Equal(0, log.Entries.Count(entry => entry.Level == LogLevel.Error));
        Assert.True(Assert.Single(world.Settings.Resources).Disposed);
        Assert.True(Assert.Single(world.Settings.Seen).Request!.IsClosed);
        Assert.Empty(world.Outbound.Entries);
    }
}
