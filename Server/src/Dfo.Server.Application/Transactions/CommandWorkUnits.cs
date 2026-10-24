using System.Runtime.ExceptionServices;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Commands;

namespace Dfo.Server.Application.Transactions;

internal sealed class CommandWorkUnits(DomainTransactionScope transactions, SelectedSessionSafety safety) : ICommandWorkUnits
{
    public ValueTask<TResult> WriteAsync<TResult>(CommandExecutionContext context, CommandWorkUnitDeclaration declaration,
        Func<CommandWorkUnit, CancellationToken, ValueTask<TResult>> action, CancellationToken cancellationToken = default)
        where TResult : notnull => StartAsync(context, declaration, action, readOnly: false, cancellationToken);

    public ValueTask<TResult> ReadOnlyAsync<TResult>(CommandExecutionContext context, CommandWorkUnitDeclaration declaration,
        Func<CancellationToken, ValueTask<TResult>> action, CancellationToken cancellationToken = default)
        where TResult : notnull => StartAsync(context, declaration, (_, ct) => action(ct), readOnly: true, cancellationToken);

    private ValueTask<TResult> StartAsync<TResult>(CommandExecutionContext context, CommandWorkUnitDeclaration declaration,
        Func<CommandWorkUnit, CancellationToken, ValueTask<TResult>> action, bool readOnly, CancellationToken ct) where TResult : notnull
    {
        ArgumentNullException.ThrowIfNull(context);
        var request = context.Request ?? throw new InvalidOperationException("An explicit command request is required.");
        var operation = RunAsync(context, declaration, action, readOnly, ct).AsTask();
        request.TrackOperation(operation);
        return new(operation);
    }

    private async ValueTask<TResult> RunAsync<TResult>(CommandExecutionContext context, CommandWorkUnitDeclaration declaration,
        Func<CommandWorkUnit, CancellationToken, ValueTask<TResult>> action, bool readOnly, CancellationToken ct)
        where TResult : notnull
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(declaration);
        ArgumentNullException.ThrowIfNull(action);
        var request = context.Request ?? throw new InvalidOperationException("An explicit command request is required.");
        SelectedSessionRecipient[] participants = [];
        DomainTransactionLease? lease = null;
        CommandWorkUnit? unit = null;
        var began = false;
        try
        {
            request.DemandCurrent();
            ArgumentException.ThrowIfNullOrWhiteSpace(declaration.Name);
            if (declaration.Kind != CommandWorkUnitKind.Ordinary)
                throw new NotSupportedException("Explicit effect main settlement is not enabled.");
            participants = [.. declaration.Participants ?? throw new InvalidOperationException("Participants are required.")];
            if (participants.Any(p => p is null) || participants.Select(p => p.Session).Distinct().Count() != participants.Length)
                throw new InvalidOperationException("Work-unit participants must be exact and unique.");
            foreach (var participant in participants) safety.DemandCurrent(participant);
            request.BeginUnit(); began = true;
            var transaction = lease = transactions.Begin(readOnly);
            unit = new CommandWorkUnit(transaction.Transaction, request);
            var result = await action(unit, ct).ConfigureAwait(false);
            request.DemandCurrent();
            foreach (var participant in participants) safety.DemandCurrent(participant);
            if (transaction.Transaction.IsRollbackOnly)
                await transaction.DisposeAsync().ConfigureAwait(false);
            else
            {
                var settlement = transaction.CommitAsync(ct).AsTask();
                await settlement.ConfigureAwait(false);
            }
            if (!readOnly && transaction.Completion == DomainTransactionCompletion.Committed)
                request.ConfirmCommit(participants);
            return result;
        }
        catch (Exception cause)
        {
            Exception error = cause;
            if (lease is not null)
            {
                try { await lease.AbortAsync(cause).ConfigureAwait(false); }
                catch (Exception cleanup) { error = cleanup; }
                if (!readOnly && lease.Completion == DomainTransactionCompletion.Committed)
                    request.ConfirmCommit(participants);
                if (lease.Completion == DomainTransactionCompletion.Uncertain || TransactionOutcomeUncertainException.Contains(error))
                    request.Uncertain(participants);
            }
            request.Failed(error);
            ExceptionDispatchInfo.Capture(error).Throw();
            throw;
        }
        finally
        {
            unit?.Close();
            if (began) request.EndUnit();
            if (lease is not null) await lease.DisposeAsync().ConfigureAwait(false);
        }
    }

    internal static async ValueTask<Exception> FailAsync(CommandRequestContext request, Exception cause, SelectedSessionSafety safety, string operation)
    {
        request.Failed(cause);
        await request.DrainOperationsAsync().ConfigureAwait(false);
        var error = request.Failure!;
        if (request.HasCommittedUnit && !TransactionOutcomeUncertainException.Contains(error))
            error = new TransactionOutcomeUncertainException(TransactionUncertaintyReason.PostCommitPublicationFailure,
                "A command failed after a confirmed work-unit commit.", error);
        foreach (var recipient in request.CommittedParticipants.DistinctBy(p => p.Session))
        {
            try { await safety.BlockAsync(recipient, error, operation).ConfigureAwait(false); }
            catch (Exception cleanup) { error = new AggregateException("Settlement fencing failed.", error, cleanup); }
        }
        request.Failed(error);
        return error;
    }
}
