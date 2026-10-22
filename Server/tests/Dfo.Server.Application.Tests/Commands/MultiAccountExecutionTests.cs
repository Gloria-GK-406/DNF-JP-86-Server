using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Commands.Pipeline;
using Dfo.Server.Application.OnlineUsers;
using Dfo.Server.Application.Tests.Execution;
using Dfo.Server.Data.Accounts;

namespace Dfo.Server.Application.Tests.Commands;

/// <summary>
/// The account behaviour as jobs see it (TASK-17): a job never waits on an account, so a group either
/// takes every account at once or fails without keeping any. Removed with the account leases in T-005.
/// </summary>
public sealed class MultiAccountExecutionTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);
    private sealed record Command(IReadOnlyList<AccountId> AccountIds) : IMultiAccountCommand;
    private static readonly CommandExecutionContext Context = new(Guid.Empty, typeof(Command),
        typeof(MultiAccountExecutionTests), CommandExecutionKind.Transactional,
        AccountAccessMode: UserAccessMode.Exclusive);

    [Fact]
    public async Task All_accounts_are_locked_once_and_released_when_execution_fails()
    {
        var gate = new AccountExecutionGate();
        var behavior = new AccountExecutionBehavior<bool>(gate);
        var (executor, _) = ExecutionTest.Executor();
        await using var _executor = executor;
        var held = -1;

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await executor.RunAsync(ExecutionTest.Work(
            async ct => await behavior.ExecuteAsync(Context, new Command([new(2), new(1), new(2)]), () =>
            {
                held = gate.TrackedEntryCount;
                throw new InvalidOperationException("rollback");
            }, ct)), CancellationToken.None).AsTask().WaitAsync(Patience));

        Assert.Equal(2, held);
        await executor.RunAsync(ExecutionTest.Work(async ct =>
        {
            await using var first = await gate.AcquireAsync(new(1), UserAccessMode.Exclusive, ct);
            await using var second = await gate.AcquireAsync(new(2), UserAccessMode.Exclusive, ct);
        }), CancellationToken.None).AsTask().WaitAsync(Patience);
        Assert.Equal(0, gate.TrackedEntryCount);
    }

    [Fact]
    public async Task A_second_account_held_by_an_earlier_job_fails_at_once_and_releases_the_first()
    {
        var gate = new AccountExecutionGate();
        var behavior = new AccountExecutionBehavior<bool>(gate);
        var (executor, _) = ExecutionTest.Executor();
        await using var _executor = executor;
        // Job A ends while still holding account 2.
        var leaked = await executor.RunAsync(ExecutionTest.Work(ct =>
            gate.AcquireAsync(new(2), UserAccessMode.Exclusive, ct)), CancellationToken.None).AsTask().WaitAsync(Patience);
        var ran = false;

        // Job B: canonical order takes account 1, then finds account 2 taken.
        await Assert.ThrowsAsync<GameplayGateWaitException>(async () => await executor.RunAsync(ExecutionTest.Work(
            async ct => await behavior.ExecuteAsync(Context, new Command([new(2), new(1), new(2)]), () =>
            {
                ran = true;
                return ValueTask.FromResult(true);
            }, ct)), CancellationToken.None).AsTask().WaitAsync(Patience));

        Assert.False(ran);
        // Job C: account 1 was released when job B failed.
        await executor.RunAsync(ExecutionTest.Work(async ct =>
            await (await gate.AcquireAsync(new(1), UserAccessMode.Exclusive, ct)).DisposeAsync()),
            CancellationToken.None).AsTask().WaitAsync(Patience);
        await leaked.DisposeAsync();
        Assert.Equal(0, gate.TrackedEntryCount);
    }

    [Fact]
    public async Task A_repeated_account_is_taken_once_and_the_group_runs()
    {
        var gate = new AccountExecutionGate();
        var behavior = new AccountExecutionBehavior<bool>(gate);
        var (executor, _) = ExecutionTest.Executor();
        await using var _executor = executor;
        var held = -1;

        // Taking account 2 twice would be a wait on the job's own lease and fail the job.
        var result = await executor.RunAsync(ExecutionTest.Work(
            async ct => await behavior.ExecuteAsync(Context, new Command([new(2), new(1), new(2)]), () =>
            {
                held = gate.TrackedEntryCount;
                return ValueTask.FromResult(true);
            }, ct)), CancellationToken.None).AsTask().WaitAsync(Patience);

        Assert.True(result);
        Assert.Equal(2, held);
        Assert.Equal(0, gate.TrackedEntryCount);
    }
}
