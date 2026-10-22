using System.Collections.Concurrent;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Application.Execution;
using Dfo.Server.Application.Notifications;
using Dfo.Server.Application.Tests.Execution;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dfo.Server.Application.Tests.Notifications;

/// <summary>Each retry pass is one timer CMD on the executor until T-006 removes the loop.</summary>
public sealed class NotificationRetryDrainExecutorTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Every_pass_runs_on_the_executor_and_a_throwing_pass_does_not_end_the_loop()
    {
        await using var executor = new GameplayExecutor(NullLogger<GameplayExecutor>.Instance);
        var drain = new RecordingDrain { ThrowOnPass = 1 };
        await using var service = Service(drain, new GameplayQueue(new GameplayShardSelector(executor)));

        await service.StartAsync(CancellationToken.None);
        try
        {
            await drain.ReachedAsync(3);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }

        Assert.True(drain.Seen.Count >= 3);
        Assert.All(drain.Seen, seen =>
        {
            Assert.True(seen.OnExecutor);
            var chain = Assert.NotNull(seen.Chain);
            Assert.Equal(GameplayCallSource.Timer, chain.Source);
            Assert.Equal("timer:NotificationRetryDrainService", chain.CommandType);
        });
    }

    /// <summary>
    /// RQ-009 as ruled for timer CMDs: one already queued when its producer stops still runs before
    /// the executor stops; the stopping token only ends the waiting for the next tick.
    /// </summary>
    [Fact]
    public async Task A_pass_already_queued_when_the_producer_stops_still_runs()
    {
        await using var executor = new GameplayExecutor(NullLogger<GameplayExecutor>.Instance);
        var queue = new RecordingGameplayQueue(new GameplayQueue(new GameplayShardSelector(executor)));
        var blocker = new ExecutorBlocker();
        await blocker.HoldAsync(queue, Patience);
        var drain = new RecordingDrain();
        await using var service = Service(drain, queue);

        await service.StartAsync(CancellationToken.None);
        await Until(() => queue.EnqueuedCount("timer:NotificationRetryDrainService") == 1);
        var stopping = service.StopAsync(CancellationToken.None);
        Assert.Empty(drain.Seen);

        await blocker.ReleaseAsync(Patience);
        await stopping.WaitAsync(Patience);
        await executor.StopAsync(CancellationToken.None).AsTask().WaitAsync(Patience);

        var seen = Assert.Single(drain.Seen);
        Assert.True(seen.OnExecutor);
        Assert.Equal(1, queue.EnqueuedCount("timer:NotificationRetryDrainService"));
    }

    private static NotificationRetryDrainService Service(INotificationRetryDrain drain, IGameplayQueue queue) =>
        new(drain,
            new NotificationDeliveryOptions(PerRecipientTimeout: TimeSpan.FromSeconds(1), RetryInterval: TimeSpan.FromMilliseconds(5)),
            queue,
            NullLogger<NotificationRetryDrainService>.Instance);

    private static async Task Until(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(Patience);
        while (!condition()) await Task.Delay(1, timeout.Token);
    }

    private sealed class RecordingDrain : INotificationRetryDrain
    {
        private readonly TaskCompletionSource reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int wanted = int.MaxValue;

        public ConcurrentQueue<(bool OnExecutor, GameplayCallChain? Chain)> Seen { get; } = new();

        public int ThrowOnPass { get; init; }

        public async Task ReachedAsync(int passes)
        {
            wanted = passes;
            if (Seen.Count >= passes) return;
            await reached.Task.WaitAsync(Patience);
        }

        public ValueTask DrainAsync(CancellationToken cancellationToken)
        {
            Seen.Enqueue((GameplayJob.IsOnExecutor, GameplayJob.Current));
            if (Seen.Count >= wanted) reached.TrySetResult();
            return Seen.Count == ThrowOnPass
                ? ValueTask.FromException(new InvalidOperationException("this pass went wrong"))
                : ValueTask.CompletedTask;
        }
    }
}
