using System.Runtime.ExceptionServices;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Commands;

namespace Dfo.Server.Application.Transactions;

internal sealed class CommandFailureAudit(DomainTransactionScope transactions) : ICommandFailureAudit
{
    public ValueTask RecordAsync(CommandExecutionContext context, Exception cause,
        Func<CommandFailureAuditFact, CancellationToken, ValueTask> record, CancellationToken token)
    {
        var request = context.Request ?? throw new InvalidOperationException("An explicit request is required.");
        CommandFailureAuditFact claim;
        try
        {
            ArgumentNullException.ThrowIfNull(record);
            claim = (request.FailureAudit ?? throw new InvalidOperationException("This command cannot audit a failure.")).Claim(cause);
        }
        catch (Exception error) { request.FailureAudit?.Invalidate(); request.FailedIndependent(error, new object()); Dfo.Server.Abstractions.Execution.GameplayJob.RecordProgramError(error); throw; }
        var operation = RunAsync(request, claim, record, token).AsTask();
        request.TrackOperation(operation);
        return new(operation);
    }
    private async ValueTask RunAsync(CommandRequestContext request, CommandFailureAuditFact claim,
        Func<CommandFailureAuditFact, CancellationToken, ValueTask> record, CancellationToken ct)
    {
        var state = request.FailureAudit!;
        DomainTransactionLease? lease = null;
        Exception? failure = null;
        Exception? actionFailure = null;
        bool began = false;
        try
        {
            request.BeginFailureAuditUnit(); began = true;
            lease = transactions.Begin();
            try { await record(claim, ct); }
            catch (Exception error) when (error is not OperationCanceledException && !TransactionOutcomeUncertainException.Contains(error)
                && lease.GuardFailure is null)
            {
                actionFailure = error;
                await lease.AbortAsync(error);
                if (lease.Completion != DomainTransactionCompletion.ConfirmedRollback || lease.GuardFailure is not null) throw;
                request.FailedIndependent(error, new object());
            }
            if (actionFailure is null) await lease.CommitAsync(ct);
        }
        catch (Exception error) { failure = error; request.FailedIndependent(error, new object()); }
        finally
        {
            if (began) request.EndUnit();
            if (lease is not null)
                try { await lease.DisposeAsync(); }
                catch (Exception cleanup) { failure = cleanup; request.FailedIndependent(cleanup, new object()); }
        }
        if (failure is not null) ExceptionDispatchInfo.Throw(failure);
        state.Finish(claim, actionFailure);
    }
}
