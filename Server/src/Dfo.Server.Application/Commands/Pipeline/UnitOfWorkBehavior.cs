using Dfo.Server.Application.Effects;
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
internal sealed class UnitOfWorkBehavior<TResult>(DomainTransactionScope scope, EffectOperationContext operations)
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
        if (context.ExecutionKind == CommandExecutionKind.ReadOnly)
        {
            await using var query = scope.Begin(readOnly: true);
            try
            {
                var result = await next().ConfigureAwait(false);
                await query.CommitAsync(cancellationToken).ConfigureAwait(false);
                return result;
            }
            catch (Exception cause)
            {
                await query.AbortAsync(cause).ConfigureAwait(false);
                throw;
            }
        }
        if (context.ExecutionKind != CommandExecutionKind.Transactional)
        {
            return await next().ConfigureAwait(false);
        }

        // An ordinary failed command leaves no change only after rollback is confirmed.
        // Abort keeps the original cause alongside any rollback failure; an uncertain outcome
        // remains a typed exception rather than being described as a successful rollback.
        await using var transaction = scope.Begin();
        operations.TransactionStarted(context, command);
        try
        {
            var result = await next().ConfigureAwait(false);
            operations.Freeze(context, command);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch (Exception cause)
        {
            await transaction.AbortAsync(cause).ConfigureAwait(false);
            operations.RollbackConfirmed(context, command);
            throw;
        }
    }
}
