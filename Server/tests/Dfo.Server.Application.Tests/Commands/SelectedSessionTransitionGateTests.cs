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
}
