using Dfo.Server.Application.Effects;
using Dfo.Server.Abstractions.Transactions;

namespace Dfo.Server.Application.Commands.Pipeline;

internal sealed class SelectedSessionSettlementBehavior<TResult>(SelectedSessionSafety safety,
    SelectedSessionCommandScope scope, NetworkCommandAuthorization authorization, EffectOperationContext operations) : ICommandPipelineBehavior<TResult> where TResult : notnull
{
    public int Order => 58;
    public async ValueTask<TResult> ExecuteAsync(CommandExecutionContext context, ICommand command,
        CommandExecutionDelegate<TResult> next, CancellationToken ct)
    {
        authorization.DemandCurrent();
        var recipient = scope.Current;
        if (recipient is not null) safety.DemandCurrent(recipient);
        try
        {
            var result = await next().ConfigureAwait(false);
            try { operations.Settled(context, command); }
            catch (Exception recordFailure)
            {
                throw new TransactionOutcomeUncertainException(TransactionUncertaintyReason.PostCommitPublicationFailure,
                    "Durable primary settlement could not be recorded in its preallocated operation entry.", recordFailure);
            }
            return result;
        }
        catch (Exception error) when (TransactionOutcomeUncertainException.Contains(error))
        {
            operations.Failed(context, command, uncertain: true);
            if (recipient is not null) await safety.BlockAsync(recipient, error, context.CommandType.Name).ConfigureAwait(false);
            throw;
        }
        catch
        {
            operations.Failed(context, command, uncertain: false);
            throw;
        }
    }
}
