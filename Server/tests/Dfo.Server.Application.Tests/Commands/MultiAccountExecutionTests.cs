using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Commands.Pipeline;
using Dfo.Server.Application.OnlineUsers;
using Dfo.Server.Data.Accounts;

namespace Dfo.Server.Application.Tests.Commands;

public sealed class MultiAccountExecutionTests
{
    private sealed record Command(IReadOnlyList<AccountId> AccountIds) : IMultiAccountCommand;
    private static readonly CommandExecutionContext Context = new(Guid.Empty, typeof(Command),
        typeof(MultiAccountExecutionTests), CommandExecutionKind.Transactional,
        AccountAccessMode: UserAccessMode.Exclusive);

    [Fact]
    public async Task All_accounts_are_locked_once_and_released_when_execution_fails()
    {
        var gate = new AccountExecutionGate();
        var behavior = new AccountExecutionBehavior<bool>(gate);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await behavior.ExecuteAsync(
            Context, new Command([new(2), new(1), new(2)]), async () =>
            {
                foreach (var account in new[] { new AccountId(1), new AccountId(2) })
                {
                    using var cancel = new CancellationTokenSource();
                    var blocked = gate.AcquireAsync(account, UserAccessMode.Exclusive, cancel.Token).AsTask();
                    Assert.False(blocked.IsCompleted);
                    cancel.Cancel();
                    await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await blocked);
                }
                await Task.Yield();
                throw new InvalidOperationException("rollback");
            }, default));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await using var first = await gate.AcquireAsync(new(1), UserAccessMode.Exclusive, timeout.Token);
        await using var second = await gate.AcquireAsync(new(2), UserAccessMode.Exclusive, timeout.Token);
    }

    [Fact]
    public async Task Cancellation_while_waiting_for_second_account_releases_first_account()
    {
        var gate = new AccountExecutionGate();
        var behavior = new AccountExecutionBehavior<bool>(gate);
        await using var held = await gate.AcquireAsync(new(2), UserAccessMode.Exclusive, default);
        using var cancel = new CancellationTokenSource();
        var pending = behavior.ExecuteAsync(Context, new Command([new(2), new(1)]),
            () => throw new InvalidOperationException("must not execute"), cancel.Token).AsTask();
        Assert.False(pending.IsCompleted);
        // Canonical ordering takes account 1 before waiting for the already-held account 2.
        using var probeCancel = new CancellationTokenSource();
        var probe = gate.AcquireAsync(new(1), UserAccessMode.Exclusive, probeCancel.Token).AsTask();
        Assert.False(probe.IsCompleted);
        probeCancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await probe);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pending);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await using var released = await gate.AcquireAsync(new(1), UserAccessMode.Exclusive, timeout.Token);
    }
}
