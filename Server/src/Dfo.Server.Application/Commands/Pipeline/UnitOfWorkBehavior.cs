using Dfo.Server.Application.Transactions;

namespace Dfo.Server.Application.Commands.Pipeline;

/// <summary>
/// Opens the transaction a transactional command runs inside, and settles it when the command
/// returns.
/// </summary>
/// <remarks>
/// Its order places it strictly inside the account gate and strictly outside the execution
/// policy, so a transaction never outlives the lock that serialises the account it belongs to,
/// and the handler always runs with one open. This is the whole of what a handler has to do to
/// get all-or-nothing: nothing.
/// </remarks>
internal sealed class UnitOfWorkBehavior<TResult>(DomainTransactionScope scope)
    : ICommandPipelineBehavior<TResult>
    where TResult : notnull
{
    public int Order => 60;

    public async ValueTask<TResult> ExecuteAsync(
        CommandExecutionContext context,
        ICommand command,
        CommandExecutionDelegate<TResult> next,
        CancellationToken cancellationToken)
    {
        if (context.ExecutionKind != CommandExecutionKind.Transactional)
        {
            return await next().ConfigureAwait(false);
        }

        // Disposal rolls back unless the commit below already settled it, so every path out of
        // the command that is not a clean return leaves nothing behind.
        await using var transaction = scope.Begin();
        var result = await next().ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }
}
