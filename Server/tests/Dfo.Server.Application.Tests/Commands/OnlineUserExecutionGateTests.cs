using Dfo.Server.Application.Commands;
using Dfo.Server.Application.OnlineUsers;
using Dfo.Server.Data.OnlineUsers;

namespace Dfo.Server.Application.Tests.Commands;

public sealed class OnlineUserExecutionGateTests
{
    [Fact]
    public async Task SharedExecutionsOverlap_AndWaitingExclusiveBlocksLaterSharedExecution()
    {
        var gate = new OnlineUserExecutionGate();
        var userId = OnlineUserId.New();
        await using var firstShared = await gate.AcquireAsync(
            userId, OnlineUserAccessMode.Shared, CancellationToken.None);
        await using var secondShared = await gate.AcquireAsync(
            userId, OnlineUserAccessMode.Shared, CancellationToken.None);

        var exclusiveTask = gate.AcquireAsync(
            userId, OnlineUserAccessMode.Exclusive, CancellationToken.None).AsTask();
        var laterSharedTask = gate.AcquireAsync(
            userId, OnlineUserAccessMode.Shared, CancellationToken.None).AsTask();

        Assert.False(exclusiveTask.IsCompleted);
        Assert.False(laterSharedTask.IsCompleted);

        await firstShared.DisposeAsync();
        Assert.False(exclusiveTask.IsCompleted);
        await secondShared.DisposeAsync();

        await using var exclusive = await exclusiveTask;
        Assert.False(laterSharedTask.IsCompleted);
        await exclusive.DisposeAsync();

        await using var laterShared = await laterSharedTask;
    }

    [Fact]
    public async Task CancelledWaiterIsRemoved_AndDoesNotLeakExecutionRights()
    {
        var gate = new OnlineUserExecutionGate();
        var userId = OnlineUserId.New();
        await using var exclusive = await gate.AcquireAsync(
            userId, OnlineUserAccessMode.Exclusive, CancellationToken.None);
        using var cancelled = new CancellationTokenSource();
        var waiting = gate.AcquireAsync(
            userId, OnlineUserAccessMode.Shared, cancelled.Token).AsTask();

        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        await exclusive.DisposeAsync();

        await using var next = await gate.AcquireAsync(
            userId, OnlineUserAccessMode.Exclusive, CancellationToken.None);
    }

    [Fact]
    public async Task DifferentOnlineUsersDoNotBlockEachOther()
    {
        var gate = new OnlineUserExecutionGate();
        await using var first = await gate.AcquireAsync(
            OnlineUserId.New(), OnlineUserAccessMode.Exclusive, CancellationToken.None);

        await using var second = await gate.AcquireAsync(
            OnlineUserId.New(), OnlineUserAccessMode.Exclusive, CancellationToken.None);
    }

    [Fact]
    public async Task CancellationRacingWithGrant_DoesNotDeadlockOrLeakRights()
    {
        var gate = new OnlineUserExecutionGate();
        var userId = OnlineUserId.New();

        for (var iteration = 0; iteration < 250; iteration++)
        {
            var active = await gate.AcquireAsync(
                userId, OnlineUserAccessMode.Exclusive, CancellationToken.None);
            using var cancellation = new CancellationTokenSource();
            var waiting = gate.AcquireAsync(
                userId, OnlineUserAccessMode.Shared, cancellation.Token).AsTask();

            await Task.WhenAll(
                Task.Run(cancellation.Cancel),
                active.DisposeAsync().AsTask()).WaitAsync(TimeSpan.FromSeconds(5));

            try
            {
                await using var granted = await waiting;
            }
            catch (OperationCanceledException)
            {
            }

            await using var next = await gate.AcquireAsync(
                userId, OnlineUserAccessMode.Exclusive, CancellationToken.None);
        }

        Assert.Equal(0, gate.TrackedEntryCount);
    }

    [Fact]
    public async Task IdleEntriesAreReclaimedWithoutSplittingConcurrentExclusiveExecution()
    {
        var gate = new OnlineUserExecutionGate();
        var userId = OnlineUserId.New();
        var active = 0;
        var maximumActive = 0;

        var workers = Enumerable.Range(0, 16).Select(async _ =>
        {
            for (var iteration = 0; iteration < 25; iteration++)
            {
                await using var lease = await gate.AcquireAsync(
                    userId, OnlineUserAccessMode.Exclusive, CancellationToken.None);
                var current = Interlocked.Increment(ref active);
                InterlockedExtensions.Max(ref maximumActive, current);
                await Task.Yield();
                Interlocked.Decrement(ref active);
            }
        });

        await Task.WhenAll(workers);

        Assert.Equal(1, maximumActive);
        Assert.Equal(0, gate.TrackedEntryCount);
    }

    private static class InterlockedExtensions
    {
        public static void Max(ref int target, int value)
        {
            var current = Volatile.Read(ref target);
            while (current < value)
            {
                var observed = Interlocked.CompareExchange(ref target, value, current);
                if (observed == current)
                {
                    return;
                }

                current = observed;
            }
        }
    }
}
