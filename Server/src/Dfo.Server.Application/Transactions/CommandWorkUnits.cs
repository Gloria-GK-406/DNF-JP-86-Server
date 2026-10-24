using System.Runtime.ExceptionServices;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Application.Effects;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Commands;

namespace Dfo.Server.Application.Transactions;

internal sealed class CommandWorkUnits(DomainTransactionScope transactions, SelectedSessionSafety safety) : ICommandWorkUnits
{
    public ValueTask<CommandWriteResult<TResult>> WriteResultAsync<TResult>(CommandExecutionContext context, CommandWorkUnitDeclaration declaration,
        Func<CommandWorkUnit, CancellationToken, ValueTask<TResult>> action, CancellationToken cancellationToken = default)
        where TResult : notnull => StartAsync(context, declaration, action, readOnly: false, recoverCommit: true, cancellationToken);

    public async ValueTask<TResult> WriteAsync<TResult>(CommandExecutionContext context, CommandWorkUnitDeclaration declaration,
        Func<CommandWorkUnit, CancellationToken, ValueTask<TResult>> action, CancellationToken cancellationToken = default)
        where TResult : notnull => ((CommandWriteResult<TResult>.Returned)await StartAsync(context, declaration, action,
            readOnly: false, recoverCommit: false, cancellationToken).ConfigureAwait(false)).Value;

    public async ValueTask<TResult> ReadOnlyAsync<TResult>(CommandExecutionContext context, CommandWorkUnitDeclaration declaration,
        Func<CancellationToken, ValueTask<TResult>> action, CancellationToken cancellationToken = default)
        where TResult : notnull => ((CommandWriteResult<TResult>.Returned)await StartAsync(context, declaration, (_, ct) => action(ct),
            readOnly: true, recoverCommit: false, cancellationToken).ConfigureAwait(false)).Value;

    private ValueTask<CommandWriteResult<TResult>> StartAsync<TResult>(CommandExecutionContext context, CommandWorkUnitDeclaration declaration,
        Func<CommandWorkUnit, CancellationToken, ValueTask<TResult>> action, bool readOnly, bool recoverCommit, CancellationToken ct) where TResult : notnull
    {
        ArgumentNullException.ThrowIfNull(context);
        var request = context.Request ?? throw new InvalidOperationException("An explicit command request is required.");
        var operation = RunAsync(context, declaration, action, readOnly, recoverCommit, ct).AsTask();
        request.TrackOperation(operation);
        return new(operation);
    }

    private async ValueTask<CommandWriteResult<TResult>> RunAsync<TResult>(CommandExecutionContext context, CommandWorkUnitDeclaration declaration,
        Func<CommandWorkUnit, CancellationToken, ValueTask<TResult>> action, bool readOnly, bool recoverCommit, CancellationToken ct)
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
        var stage = FailureStage.Setup;
        Exception? commitRollbackCause = null;
        var recordedFailure = false;
        ExplicitEffectRequest? main = null;
        IEffectFootprintRecorder? footprint = null;
        ExplicitNonRequestFootprint? nonRequest = null;
        try
        {
            request.DemandCurrent();
            ArgumentException.ThrowIfNullOrWhiteSpace(declaration.Name);
            if (!Enum.IsDefined(declaration.Kind)) throw new InvalidOperationException("Unknown work-unit kind.");
            participants = [.. declaration.Participants ?? throw new InvalidOperationException("Participants are required.")];
            if (participants.Any(p => p is null) || participants.Select(p => p.Session).Distinct().Count() != participants.Length)
                throw new InvalidOperationException("Work-unit participants must be exact and unique.");
            foreach (var participant in participants) safety.DemandCurrent(participant);
            if (!Enum.IsDefined(declaration.Footprint)) throw new InvalidOperationException("Unknown footprint declaration.");
            if (declaration.Footprint == CommandFootprintMode.NonRequest
                && (readOnly || declaration.Kind != CommandWorkUnitKind.Ordinary
                    || request is not CommandRequestContext.ProtocolNonEffect plain
                    || participants.Length != 1 || participants[0].Connection is null
                    || !ReferenceEquals(participants[0].Connection, plain.Dispatch.Connection)
                    || participants[0].Session.ConnectionId != plain.Dispatch.Connection.ConnectionId))
                throw new InvalidOperationException("NonRequest requires one exact protocol actor in an ordinary write.");
            if (declaration.Kind == CommandWorkUnitKind.MainSettlement)
            {
                main = (request as CommandRequestContext.ProtocolEffectSelected)?.State
                    ?? throw new InvalidOperationException("Main requires a selected effect request.");
                footprint = main.BeginMain(participants, readOnly);
            }
            request.BeginUnit(); began = true;
            var transaction = lease = transactions.Begin(readOnly);
            main?.TransactionStarted();
            if (declaration.Footprint == CommandFootprintMode.NonRequest)
                footprint = nonRequest = new((CommandRequestContext.ProtocolNonEffect)request, participants[0],
                    (DomainTransaction)transaction.Transaction, safety);
            unit = new CommandWorkUnit(transaction.Transaction, request, footprint);
            TResult result;
            stage = FailureStage.Action;
            try { result = await action(unit, ct).ConfigureAwait(false); stage = FailureStage.PostAction; }
            finally { nonRequest?.Close(); }
            request.DemandCurrent();
            foreach (var participant in participants) safety.DemandCurrent(participant);
            if (transaction.GuardFailure is { } guard)
            {
                stage = FailureStage.GuardPropagation;
                ExceptionDispatchInfo.Throw(guard);
            }
            if (transaction.Transaction.IsRollbackOnly)
            {
                if (unit.RollbackCause is { } business)
                {
                    stage = FailureStage.BusinessRollback;
                    await transaction.AbortAsync(business).ConfigureAwait(false);
                    stage = FailureStage.PostRollback;
                }
                else await transaction.DisposeAsync().ConfigureAwait(false);
                main?.Failed(rollback: transaction.Completion == DomainTransactionCompletion.ConfirmedRollback, uncertain: false);
            }
            else
            {
                main?.Freeze();
                // Only this actual CommitAsync span can yield a known, fully rolled-back result.
                try { await transaction.CommitAsync(ct).ConfigureAwait(false); }
                catch (Exception cause) when (recoverCommit && !ct.IsCancellationRequested && cause is not OperationCanceledException
                    && !TransactionOutcomeUncertainException.Contains(cause) && request.Failure is null
                    && transaction.Completion == DomainTransactionCompletion.ConfirmedRollback
                    && transaction.GuardFailure is null
                    && transaction.CommitFailure is { Kind: TransactionFailureKind.KnownStorage } fact
                    && ReferenceEquals(fact.Cause, cause))
                {
                    commitRollbackCause = cause;
                    stage = FailureStage.RecoveredValidation;
                    request.DemandCurrent();
                    foreach (var participant in participants) safety.DemandCurrent(participant);
                    main?.Failed(rollback: true, uncertain: false);
                    return new CommandWriteResult<TResult>.CommitRolledBack(cause);
                }
            }
            if (!readOnly && transaction.Completion == DomainTransactionCompletion.Committed)
            {
                request.ConfirmCommit(participants);
                main?.Settled();
                if (request is CommandRequestContext.ProtocolEffectSelected selected && selected.State.IsReplay)
                    selected.State.ReplayCommitted();
            }
            return new CommandWriteResult<TResult>.Returned(result);
        }
        catch (Exception cause)
        {
            var retained = unit?.RollbackCause ?? commitRollbackCause;
            // Equality only proves propagation when the real source/phase identifies that event.
            // A later current check throwing the same object is still a second occurrence.
            var propagation = stage == FailureStage.BusinessRollback
                || stage == FailureStage.GuardPropagation && ReferenceEquals(retained, cause)
                || stage == FailureStage.Action && ReferenceEquals(unit?.RejectedRollbackCause, cause) && ReferenceEquals(retained, cause);
            Exception error = retained is not null && !propagation
                ? new AggregateException("A rolled-back operation was followed by another failure.", retained, cause) : cause;
            if (lease is not null)
            {
                try { await lease.AbortAsync(error).ConfigureAwait(false); }
                catch (Exception cleanup) { error = cleanup; }
                if (!readOnly && lease.Completion == DomainTransactionCompletion.Committed)
                    request.ConfirmCommit(participants);
                if (lease.Completion == DomainTransactionCompletion.Uncertain || TransactionOutcomeUncertainException.Contains(error))
                    request.Uncertain(participants);
            }
            main?.Failed(lease?.Completion == DomainTransactionCompletion.ConfirmedRollback,
                lease?.Completion == DomainTransactionCompletion.Uncertain || TransactionOutcomeUncertainException.Contains(error));
            request.Failed(error);
            recordedFailure = true;
            ExceptionDispatchInfo.Capture(error).Throw();
            throw;
        }
        finally
        {
            nonRequest?.Close();
            unit?.Close();
            if (began) request.EndUnit();
            main?.EndMain();
            if (lease is not null)
            {
                try { await lease.DisposeAsync().ConfigureAwait(false); }
                catch (Exception cleanup)
                {
                    var retained = unit?.RollbackCause ?? commitRollbackCause;
                    var error = !recordedFailure && retained is not null
                        ? new AggregateException("Rollback completed but lease cleanup failed.", retained, cleanup) : cleanup;
                    request.FailedIndependent(error, lease);
                    ExceptionDispatchInfo.Capture(error).Throw(); throw;
                }
            }
        }
    }

    private enum FailureStage { Setup, Action, PostAction, GuardPropagation, BusinessRollback, PostRollback, RecoveredValidation }

    internal static async ValueTask<Exception> FailAsync(CommandRequestContext request, Exception cause, SelectedSessionSafety safety, string operation)
    {
        request.Failed(cause);
        var error = request.Failure!;
        if (request.RetainsRawEncoderFailure(error)) return error;
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
