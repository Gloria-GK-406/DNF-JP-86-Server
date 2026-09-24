using Dfo.Server.Application.OnlineUsers;
using Dfo.Server.Data.Accounts;

namespace Dfo.Server.Application.Commands.Pipeline;

internal sealed class AccountExecutionBehavior<TResult>(AccountExecutionGate gate)
    : ICommandPipelineBehavior<TResult>
    where TResult : notnull
{
    public int Order => 55;

    public async ValueTask<TResult> ExecuteAsync(
        CommandExecutionContext context,
        ICommand command,
        CommandExecutionDelegate<TResult> next,
        CancellationToken cancellationToken)
    {
        if (context.AccountAccessMode == UserAccessMode.Unscoped)
        {
            return await next().ConfigureAwait(false);
        }

        IReadOnlyList<AccountId> accounts = command switch
        {
            IMultiAccountCommand multiple => multiple.AccountIds,
            IAccountCommand single => [single.AccountId],
            _ => throw new InvalidOperationException(
                $"Command '{context.CommandType.Name}' is registered as account " +
                $"'{context.AccountAccessMode}' but does not implement " +
                $"'{nameof(IAccountCommand)}' or '{nameof(IMultiAccountCommand)}'."),
        };
        if (accounts.Count == 0)
            throw new InvalidOperationException("An account-scoped command requires an account.");

        // The same order across overlapping groups avoids lock inversion. No transaction is
        // opened until every lease has been acquired; cancellation also releases partial leases.
        var leases = new List<IAsyncDisposable>();
        try
        {
            foreach (var account in accounts.Distinct().OrderBy(value => value.Value))
                leases.Add(await gate.AcquireAsync(account, context.AccountAccessMode,
                    cancellationToken).ConfigureAwait(false));
            return await next().ConfigureAwait(false);
        }
        finally
        {
            for (var index = leases.Count - 1; index >= 0; index--)
                await leases[index].DisposeAsync().ConfigureAwait(false);
        }
    }
}
