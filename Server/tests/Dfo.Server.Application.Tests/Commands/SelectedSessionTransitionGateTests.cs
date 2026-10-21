using Dfo.Server.Application.Commands;
using Dfo.Server.Application.OnlineUsers;
using Dfo.Server.Data.Accounts;
using Microsoft.Extensions.DependencyInjection;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.Commands;

public sealed class SelectedSessionTransitionGateTests
{
    private static readonly AccountId Account = new(7);

    [Fact]
    public async Task ExclusiveBorrowPreservesActualOwnerModeAcrossSharedChildrenAndParentDisposal()
    {
        var gate = new SelectedSessionTransitionGate();
        var owner = await gate.BorrowOrAcquireExclusiveAsync(Account, default);
        var shared = await gate.BorrowOrAcquireSharedAsync(Account, default);
        var exclusive = gate.BorrowOrAcquireExclusiveAsync(Account, default);
        Assert.True(exclusive.IsCompletedSuccessfully);
        var child = await exclusive;
        await owner.DisposeAsync(); await shared.DisposeAsync();
        var waiter = await IsolatedAcquire(gate, UserAccessMode.Shared);
        Assert.False(waiter.IsCompleted);
        await child.DisposeAsync();
        await (await waiter.WaitAsync(TimeSpan.FromSeconds(5))).DisposeAsync();
        Assert.Equal(0, gate.TrackedEntryCount);
    }

    [Fact]
    public async Task HeldSharedExplicitlyRefusesExclusiveUpgradeWithoutLeakingOrChangingSharedBorrow()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var gate = new SelectedSessionTransitionGate();
            var shared = await gate.BorrowOrAcquireSharedAsync(Account, default);
            Assert.Throws<InvalidOperationException>(() => gate.BorrowOrAcquireExclusiveAsync(Account, default));
            await (await gate.BorrowOrAcquireSharedAsync(Account, default)).DisposeAsync();
            await shared.DisposeAsync();
            await (await gate.BorrowOrAcquireExclusiveAsync(Account, default)).DisposeAsync();
            Assert.Equal(0, gate.TrackedEntryCount);
        });
    }

    [Fact]
    public async Task PendingCancelledSharedFrameDoesNotCountAsHeldForExclusiveBorrow()
    {
        var gate = new SelectedSessionTransitionGate();
        var blocker = await (await IsolatedAcquire(gate, UserAccessMode.Exclusive));
        using var cancellation = new CancellationTokenSource();
        var pending = gate.AcquireAsync(Account, UserAccessMode.Shared, cancellation.Token).AsTask();
        var next = gate.BorrowOrAcquireExclusiveAsync(Account, default).AsTask();
        Assert.False(next.IsCompleted);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        await blocker.DisposeAsync(); await (await next.WaitAsync(TimeSpan.FromSeconds(5))).DisposeAsync();
        Assert.Equal(0, gate.TrackedEntryCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OuterLeaseIsVisibleThroughActualCommandBusAndOutlivesParentWhileCommandBorrows(bool group)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCommandDispatching();
        services.AddSingleton<SelectedSessionTransitionGate>();
        var barriers = new Barriers();
        services.AddSingleton(barriers);
        services.AddCommandHandler<BorrowCommand, BorrowResult, BorrowHandler>(CommandExecutionKind.Direct);
        await using var provider = services.BuildServiceProvider();
        var gate = provider.GetRequiredService<SelectedSessionTransitionGate>();
        var parent = group
            ? await gate.AcquireManyExclusiveAsync([new AccountId(8), Account], default)
            : await gate.AcquireAsync(Account, UserAccessMode.Exclusive, default);
        using var cancellation = new CancellationTokenSource();
        var execution = provider.GetRequiredService<ICommandBus>().SendAsync(new BorrowCommand(), cancellation.Token).AsTask();
        Task<IAsyncDisposable>? writer = null;
        try
        {
            await barriers.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await parent.DisposeAsync();
            writer = gate.AcquireAsync(Account, UserAccessMode.Exclusive, cancellation.Token).AsTask();
            Assert.False(writer.IsCompleted);
            barriers.Release.SetResult();
            await execution.WaitAsync(TimeSpan.FromSeconds(5));
            await (await writer.WaitAsync(TimeSpan.FromSeconds(5))).DisposeAsync();
        }
        finally
        {
            barriers.Release.TrySetResult();
            cancellation.Cancel();
            await parent.DisposeAsync();
            try { await execution.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (OperationCanceledException) { }
            if (writer is not null)
            {
                try { await (await writer.WaitAsync(TimeSpan.FromSeconds(5))).DisposeAsync(); }
                catch (OperationCanceledException) { }
            }
        }
        Assert.Equal(0, gate.TrackedEntryCount);
    }

    [Fact]
    public async Task Cancelled_group_releases_earlier_accounts_and_invalidates_pending_frames()
    {
        var gate = new SelectedSessionTransitionGate();
        var second = new AccountId(Account.Value + 1);
        var blocker = await gate.AcquireAsync(second, UserAccessMode.Exclusive, default);
        using var cancellation = new CancellationTokenSource();
        var pending = gate.AcquireManyExclusiveAsync([second, Account], cancellation.Token).AsTask();
        Assert.False(pending.IsCompleted);
        var firstWaiter = gate.AcquireAsync(Account, UserAccessMode.Exclusive, default).AsTask();
        Assert.False(firstWaiter.IsCompleted);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        await (await firstWaiter.WaitAsync(TimeSpan.FromSeconds(5))).DisposeAsync();
        await blocker.DisposeAsync();
        await (await gate.BorrowOrAcquireExclusiveAsync(second, default)).DisposeAsync();
        Assert.Equal(0, gate.TrackedEntryCount);
    }

    [Fact]
    public async Task ExplicitAcquireRetainsFifoInsteadOfSilentlyBorrowing()
    {
        var gate = new SelectedSessionTransitionGate();
        var shared = await gate.AcquireAsync(Account, UserAccessMode.Shared, default);
        var writer = gate.AcquireAsync(Account, UserAccessMode.Exclusive, default).AsTask();
        var later = gate.AcquireAsync(Account, UserAccessMode.Shared, default).AsTask();
        Assert.False(writer.IsCompleted);
        Assert.False(later.IsCompleted);
        await shared.DisposeAsync();
        var exclusive = await writer.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(later.IsCompleted);
        await exclusive.DisposeAsync();
        await (await later.WaitAsync(TimeSpan.FromSeconds(5))).DisposeAsync();
        Assert.Equal(0, gate.TrackedEntryCount);
    }


    [Fact]
    public async Task ExclusiveOwnerCanBorrowAndUpcastAcquisitionStillTracksTheLease()
    {
        var gate = new SelectedSessionTransitionGate();
        UserExecutionGate<AccountId> upcast = gate;
        var outer = await upcast.AcquireAsync(Account, UserAccessMode.Exclusive, default);
        var inner = gate.BorrowOrAcquireSharedAsync(Account, default);
        Assert.True(inner.IsCompletedSuccessfully);
        var borrowed = await inner;
        await outer.DisposeAsync();
        var writer = gate.AcquireAsync(Account, UserAccessMode.Exclusive, default).AsTask();
        Assert.False(writer.IsCompleted);
        await borrowed.DisposeAsync();
        await borrowed.DisposeAsync();
        await using var next = await writer.WaitAsync(TimeSpan.FromSeconds(5));
        await next.DisposeAsync();
        Assert.Equal(0, gate.TrackedEntryCount);
    }

    [Fact]
    public async Task BorrowDoesNotQueueBehindWriterButAnotherFlowStillDoes()
    {
        var gate = new SelectedSessionTransitionGate();
        var outer = await gate.AcquireAsync(Account, UserAccessMode.Shared, default);
        var writer = await IsolatedAcquire(gate, UserAccessMode.Exclusive);
        var otherShared = await IsolatedAcquire(gate, UserAccessMode.Shared);
        var borrow = gate.BorrowOrAcquireSharedAsync(Account, default);
        Assert.True(borrow.IsCompletedSuccessfully);
        var child = await borrow;
        Assert.False(writer.IsCompleted);
        Assert.False(otherShared.IsCompleted);
        await outer.DisposeAsync();
        Assert.False(writer.IsCompleted);
        await child.DisposeAsync();
        var next = await writer.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(otherShared.IsCompleted);
        await next.DisposeAsync();
        await (await otherShared.WaitAsync(TimeSpan.FromSeconds(5))).DisposeAsync();
        Assert.Equal(0, gate.TrackedEntryCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DelayedInheritedClosedFrameCannotBorrowAnotherChildsReference(bool exclusiveBorrow)
    {
        var gate = new SelectedSessionTransitionGate();
        var outer = await gate.AcquireAsync(Account, UserAccessMode.Exclusive, default);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempted = new TaskCompletionSource<Task<IAsyncDisposable>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var delayed = Task.Run(async () =>
        {
            await start.Task;
            var acquired = (exclusiveBorrow ? gate.BorrowOrAcquireExclusiveAsync(Account, default)
                : gate.BorrowOrAcquireSharedAsync(Account, default)).AsTask();
            attempted.SetResult(acquired);
            await using var lease = await acquired;
        });
        var child = await gate.BorrowOrAcquireSharedAsync(Account, default);
        await outer.DisposeAsync();
        start.SetResult();
        var pending = await attempted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(pending.IsCompleted);
        await child.DisposeAsync();
        await delayed.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, gate.TrackedEntryCount);
    }

    [Fact]
    public async Task PendingCancelledFrameIsNotHeldAndQueuedAcquisitionPropagatesToItsCaller()
    {
        var gate = new SelectedSessionTransitionGate();
        var blockerTask = await IsolatedAcquire(gate, UserAccessMode.Exclusive);
        var blocker = await blockerTask;
        using var cancel = new CancellationTokenSource();
        var pending = gate.AcquireAsync(Account, UserAccessMode.Shared, cancel.Token).AsTask();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        var realAcquisition = gate.BorrowOrAcquireSharedAsync(Account, default).AsTask();
        Assert.False(realAcquisition.IsCompleted);
        await blocker.DisposeAsync();
        var actual = await realAcquisition.WaitAsync(TimeSpan.FromSeconds(5));
        var borrow = gate.BorrowOrAcquireSharedAsync(Account, default);
        Assert.True(borrow.IsCompletedSuccessfully);
        await (await borrow).DisposeAsync();
        await actual.DisposeAsync();
        Assert.Equal(0, gate.TrackedEntryCount);
    }

    [Fact]
    public async Task DifferentAccountAndGateDoNotBorrowAndCancelledBorrowCannotGainRights()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var gate = new SelectedSessionTransitionGate();
            var other = new SelectedSessionTransitionGate();
            var outer = await gate.AcquireAsync(Account, UserAccessMode.Exclusive, default);
            var different = await gate.BorrowOrAcquireSharedAsync(new(8), default);
            var separate = await other.BorrowOrAcquireSharedAsync(Account, default);
            Assert.Equal(2, gate.TrackedEntryCount);
            Assert.Equal(1, other.TrackedEntryCount);
            using var cancel = new CancellationTokenSource();
            cancel.Cancel();
            Assert.ThrowsAny<OperationCanceledException>(() => gate.BorrowOrAcquireSharedAsync(Account, cancel.Token));
            await separate.DisposeAsync();
            await different.DisposeAsync();
            await outer.DisposeAsync();
            Assert.Equal(0, other.TrackedEntryCount);
            Assert.Equal(0, gate.TrackedEntryCount);
        });
    }

    [Fact]
    public async Task GrantCancelRaceAndExceptionUnwindingDoNotLeakRights()
    {
        var gate = new SelectedSessionTransitionGate();
        for (var iteration = 0; iteration < 100; iteration++)
        {
            var outer = await gate.AcquireAsync(Account, UserAccessMode.Exclusive, default);
            using var cancel = new CancellationTokenSource();
            var waiting = gate.AcquireAsync(Account, UserAccessMode.Shared, cancel.Token).AsTask();
            await Task.WhenAll(Task.Run(cancel.Cancel), outer.DisposeAsync().AsTask());
            try
            {
                await using var acquired = await waiting.WaitAsync(TimeSpan.FromSeconds(5));
                await using var borrowed = await gate.BorrowOrAcquireSharedAsync(Account, default);
                throw new IOException("business failure");
            }
            catch (OperationCanceledException) { }
            catch (IOException) { }
            Assert.Equal(0, gate.TrackedEntryCount);
        }
    }

    private static Task<Task<IAsyncDisposable>> IsolatedAcquire(
        SelectedSessionTransitionGate gate, UserAccessMode mode)
    {
        using (ExecutionContext.SuppressFlow())
            return Task.Run(() => new Box(gate.AcquireAsync(Account, mode, default).AsTask()))
                .ContinueWith(task => task.GetAwaiter().GetResult().Value, TaskScheduler.Default);
    }
    private sealed record Box(Task<IAsyncDisposable> Value);
    private sealed record BorrowCommand : ICommand<BorrowResult>;
    private sealed record BorrowResult;
    private sealed class Barriers
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    private sealed class BorrowHandler(SelectedSessionTransitionGate gate, Barriers barriers)
        : ICommandHandler<BorrowCommand, BorrowResult>
    {
        public async ValueTask<BorrowResult> HandleAsync(CommandExecutionContext context,
            BorrowCommand command, CancellationToken cancellationToken)
        {
            await using var lease = await gate.BorrowOrAcquireSharedAsync(Account, cancellationToken);
            barriers.Entered.SetResult();
            await barriers.Release.Task.WaitAsync(cancellationToken);
            return new();
        }
    }
}
