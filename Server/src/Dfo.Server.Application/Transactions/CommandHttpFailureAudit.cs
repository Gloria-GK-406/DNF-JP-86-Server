using System.Runtime.ExceptionServices;
using Dfo.Server.Abstractions.Administration;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Commands;

namespace Dfo.Server.Application.Transactions;

internal sealed class CommandHttpFailureAudit(DomainTransactionScope transactions,
    IGmCeraDomainService cera, IGmOfficialMailDomainService mail) : ICommandHttpFailureAudit
{
    public ValueTask RecordAsync(CommandExecutionContext context, Exception cause, CancellationToken token)
    {
        var request = context.Request ?? throw new InvalidOperationException("An explicit HTTP request is required.");
        try
        {
            (request.HttpFailure ?? throw new InvalidOperationException("This command cannot audit an HTTP failure.")).Claim(cause);
        }
        catch (Exception error) { request.HttpFailure?.Invalidate(); request.FailedIndependent(error, new object()); Dfo.Server.Abstractions.Execution.GameplayJob.RecordProgramError(error); throw; }
        var operation = RunAsync(request, token).AsTask();
        request.TrackOperation(operation);
        return new(operation);
    }
    private async ValueTask RunAsync(CommandRequestContext request, CancellationToken ct)
    {
        var state = request.HttpFailure!;
        DomainTransactionLease? lease = null;
        Exception? failure = null;
        Exception? actionFailure = null;
        bool began = false;
        try
        {
            request.BeginHttpFailureAuditUnit(); began = true;
            lease = transactions.Begin();
            try
            {
                if (state.IsCera) await cera.RecordFailureAsync(state.Secret, state.RequestId, state.Outcome, state.Reason, ct);
                else await mail.RecordFailureAsync(state.Secret, state.RequestId, state.Outcome, state.Reason, ct);
            }
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
        state.Finish(actionFailure);
    }
}
