using System.Runtime.ExceptionServices;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Application.Effects;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Commands;

namespace Dfo.Server.Application.Transactions;

internal sealed class CommandWorkUnits(DomainTransactionScope transactions, SelectedSessionSafety safety) : ICommandWorkUnits
{
    public ValueTask<CommandSelfSettledWriteResult<TResult>> WriteSelfSettledResultAsync<TResult>(CommandExecutionContext context,
        CommandWorkUnitDeclaration declaration, Func<CommandWorkUnit, CancellationToken, ValueTask<TResult>> action,
        CancellationToken cancellationToken = default) where TResult : notnull
    {
        ArgumentNullException.ThrowIfNull(context);
        var request = context.Request ?? throw new InvalidOperationException("An explicit command request is required.");
        request.ProducerCancellation?.RunnerAttempted();
        request.HttpFailure?.OrdinaryRunnerAttempted();
        var operation = RunSelfSettledAsync(context, declaration, action, cancellationToken).AsTask();
        request.TrackOperation(operation);
        return new(operation);
    }

    private async ValueTask<CommandSelfSettledWriteResult<TResult>> RunSelfSettledAsync<TResult>(CommandExecutionContext context,
        CommandWorkUnitDeclaration declaration, Func<CommandWorkUnit, CancellationToken, ValueTask<TResult>> action,
        CancellationToken ct) where TResult : notnull
    {
        var request = context.Request!;
        DomainTransactionLease? lease = null;
        CommandWorkUnit? unit = null;
        bool began = false;
        Exception? failure = null;
        CommandSelfSettledWriteResult<TResult>? candidate = null;
        Exception? retained = null;
        object? handoffCall = null;
        DomainTransaction.DomainActionRollbackFact? actionFact = null;
        var qualifyingHandoff = false;
        try
        {
            ArgumentNullException.ThrowIfNull(declaration);
            ArgumentNullException.ThrowIfNull(action);
            request.DemandCurrent();
            ArgumentException.ThrowIfNullOrWhiteSpace(declaration.Name);
            if (request is not CommandRequestContext.ProtocolNonEffect
                || request.SelfSettlement != CommandSelfSettlementCapability.SelfSettledAction
                || declaration.Kind != CommandWorkUnitKind.Ordinary || declaration.Footprint != CommandFootprintMode.None
                || declaration.Participants is not { Count: 0 } || declaration.FootprintOwners is not null)
                throw new InvalidOperationException("Self settlement requires a permitted ordinary non-effect protocol write with no participants or footprint.");
            request.BeginUnit(); began = true;
            lease = transactions.Begin();
            if (lease.OutboxUnit is null) throw new InvalidOperationException("Self settlement requires its original outbox unit.");
            unit = new(lease.Transaction, request, selfLease: lease);
            TResult result = default!;
            unit.BeginAction();
            try { result = await action(unit, ct).ConfigureAwait(false); }
            catch (CommandSelfSettlementPropagationException signal) when (ReferenceEquals(signal, unit.Handoff)
                && ReferenceEquals(signal.Unit, unit) && ReferenceEquals(signal.Call, unit.SelfCall))
            {
                actionFact = signal.Fact;
                retained = signal.Fact.Cause;
                handoffCall = signal.Call;
                MergeAdmissionGuards();
                request.DemandSelfSettlementCurrent(handoffCall);
                qualifyingHandoff = true;
                if (ct.IsCancellationRequested || retained is OperationCanceledException
                    || TransactionOutcomeUncertainException.Contains(signal.Fact.Output)
                    || signal.Fact.Completion != DomainTransactionCompletion.ConfirmedRollback
                    || lease.Completion != DomainTransactionCompletion.ConfirmedRollback || lease.GuardFailure is not null)
                    ExceptionDispatchInfo.Throw(signal.Fact.Output);
                candidate = new CommandSelfSettledWriteResult<TResult>.ActionRolledBack(retained);
            }
            finally { await unit.EndActionAsync().ConfigureAwait(false); }
            if (candidate is null)
            {
                if (unit.HasUnfinishedObservation)
                {
                    var unfinished = new InvalidOperationException("The action returned before its direct domain observation completed.");
                    request.FailedIndependent(unfinished, new object());
                    await unit.DrainObservationAsync().ConfigureAwait(false);
                    throw unfinished;
                }
                // Completion alone does not prove the caller consumed a faulted direct observation.
                await unit.DrainObservationAsync().ConfigureAwait(false);
                request.DemandCurrent();
                MergeAdmissionGuards();
                if (lease.GuardFailure is { } guard) ExceptionDispatchInfo.Throw(guard);
                if (lease.Transaction.IsRollbackOnly)
                {
                    retained = unit.RollbackCause;
                    if (retained is not null) await lease.AbortAsync(retained).ConfigureAwait(false);
                    else await lease.DisposeAsync().ConfigureAwait(false);
                }
                else
                {
                    try { await lease.CommitAsync(ct).ConfigureAwait(false); }
                    catch (Exception cause) when (!ct.IsCancellationRequested && cause is not OperationCanceledException
                        && !TransactionOutcomeUncertainException.Contains(cause) && request.Failure is null
                        && lease.Completion == DomainTransactionCompletion.ConfirmedRollback && lease.GuardFailure is null
                        && lease.CommitFailure is { Kind: TransactionFailureKind.KnownStorage, Phase: TransactionFailurePhase.FallibleCommit } fact
                        && ReferenceEquals(fact.Cause, cause))
                    {
                        retained = cause;
                        candidate = new CommandSelfSettledWriteResult<TResult>.CommitRolledBack(cause);
                    }
                }
                if (lease.Completion == DomainTransactionCompletion.Committed) request.ConfirmCommit([]);
                candidate ??= new CommandSelfSettledWriteResult<TResult>.Returned(result);
            }
        }
        catch (Exception error)
        {
            // A foreign/closed request must not let the active caller swallow this admission guard.
            if (!began) GameplayJob.RecordProgramError(error);
            // The qualified handoff's own output is still its signed occurrence, not a new outer failure.
            var ownOutputPropagation = qualifyingHandoff && actionFact is not null
                && ReferenceEquals(error, actionFact.Output);
            if (unit?.HasObservation == true)
            {
                if (!ownOutputPropagation) request.Failed(error);
                await unit.DrainObservationAsync().ConfigureAwait(false);
            }
            // A swallowed/replaced handoff cannot erase the already signed action occurrence.
            actionFact ??= unit?.ObservedFact;
            retained ??= actionFact?.Cause;
            if (actionFact is not null)
            {
                if (request.Failure is null && !ownOutputPropagation)
                    request.FailedIndependent(error, new object());
                request.FailedIndependent(actionFact.Output, actionFact.Occurrence);
            }
            failure = request.Failure ?? error;
            if (retained is not null && !Contains(failure, retained))
                failure = new AggregateException("Self settlement was followed by another failure.", retained, failure);
            if (lease is not null)
            {
                try { await lease.AbortAsync(failure).ConfigureAwait(false); }
                catch (Exception cleanup) { failure = cleanup; }
                if (lease.Completion == DomainTransactionCompletion.Committed) request.ConfirmCommit([]);
                if (lease.Completion == DomainTransactionCompletion.Uncertain || TransactionOutcomeUncertainException.Contains(failure))
                    request.Uncertain([]);
            }
            request.Failed(failure);
        }
        finally
        {
            try { unit?.Close(); }
            catch (Exception cleanup) { RecordCleanup(cleanup, new object()); }
            if (began)
            {
                try { request.EndUnit(); }
                catch (Exception cleanup) { RecordCleanup(cleanup, new object()); }
            }
            if (lease is not null)
            {
                try { await lease.DisposeAsync().ConfigureAwait(false); }
                catch (Exception cleanup) { RecordCleanup(cleanup, new object()); }
            }
        }
        MergeAdmissionGuards();
        if (failure is null)
        {
            try
            {
                if (handoffCall is not null)
                {
                    request.DemandSelfSettlementCurrent(handoffCall);
                    lease!.OutboxUnit!.ReleaseSelfSettlement();
                    request.EndSelfSettlement(handoffCall);
                }
                else request.DemandCurrent();
            }
            catch (Exception validation)
            {
                if (actionFact is not null) request.FailedIndependent(actionFact.Output, actionFact.Occurrence);
                failure = request.Failure ?? validation;
                request.Failed(failure);
            }
        }
        if (failure is not null) ExceptionDispatchInfo.Throw(request.Failure ?? failure);
        return candidate ?? throw new InvalidOperationException("Self settlement produced no outcome.");

        void MergeAdmissionGuards()
        {
            if (lease?.OutboxUnit is not { } outbox) return;
            foreach (var (occurrence, error) in outbox.SelfSettlementGuards)
                request.FailedIndependent(error, occurrence);
        }
        void RecordCleanup(Exception cleanup, object occurrence)
        {
            failure ??= retained is null ? cleanup : new AggregateException("Self settlement cleanup failed.", retained, cleanup);
            if (request.Failure is null && retained is not null)
                request.FailedIndependent(actionFact?.Output ?? retained, actionFact?.Occurrence ?? new object());
            request.FailedIndependent(cleanup, occurrence);
        }
    }

    private static bool Contains(Exception outer, Exception inner) => ReferenceEquals(outer, inner)
        || outer is AggregateException aggregate && aggregate.InnerExceptions.Any(child => Contains(child, inner))
        || outer.InnerException is { } cause && Contains(cause, inner);

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

    public async ValueTask<TResult> ReadOnlyAsync<TResult>(CommandExecutionContext context, CommandWorkUnitDeclaration declaration,
        Func<CommandWorkUnit, CancellationToken, ValueTask<TResult>> action, CancellationToken cancellationToken = default)
        where TResult : notnull => ((CommandWriteResult<TResult>.Returned)await StartAsync(context, declaration, action,
            readOnly: true, recoverCommit: false, cancellationToken).ConfigureAwait(false)).Value;

    private ValueTask<CommandWriteResult<TResult>> StartAsync<TResult>(CommandExecutionContext context, CommandWorkUnitDeclaration declaration,
        Func<CommandWorkUnit, CancellationToken, ValueTask<TResult>> action, bool readOnly, bool recoverCommit, CancellationToken ct) where TResult : notnull
    {
        ArgumentNullException.ThrowIfNull(context);
        var request = context.Request ?? throw new InvalidOperationException("An explicit command request is required.");
        request.ProducerCancellation?.RunnerAttempted();
        request.HttpFailure?.OrdinaryRunnerAttempted();
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
        (Exception Cause, Exception Output, bool Commit)? httpFailure = null;
        ExplicitEffectRequest? main = null;
        IEffectFootprintRecorder? footprint = null;
        ExplicitNonRequestFootprint? nonRequest = null;
        ExplicitDeclaredNonRequestFootprint? declaredNonRequest = null;
        Dfo.Server.Data.World.SelectedSessionIdentity[] owners = [];
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
            if (declaration.Footprint == CommandFootprintMode.DeclaredNonRequest)
            {
                if (readOnly || recoverCommit || declaration.Kind != CommandWorkUnitKind.Ordinary
                    || request is not (CommandRequestContext.ProtocolNonEffect or CommandRequestContext.TrustedInternal))
                    throw new InvalidOperationException("DeclaredNonRequest requires an ordinary writable non-effect protocol or trusted internal request.");
                owners = [.. declaration.FootprintOwners ?? throw new InvalidOperationException("DeclaredNonRequest requires explicit owners.")];
                if (owners.Length == 0 || owners.Any(owner => owner is null) || owners.Distinct().Count() != owners.Length)
                    throw new InvalidOperationException("DeclaredNonRequest owners must be nonempty, exact and unique.");
            }
            else if (declaration.FootprintOwners is not null)
                throw new InvalidOperationException("Only DeclaredNonRequest accepts an explicit owner set.");
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
            if (declaration.Footprint == CommandFootprintMode.DeclaredNonRequest)
                footprint = declaredNonRequest = new(request, owners,
                    (DomainTransaction)transaction.Transaction);
            unit = new CommandWorkUnit(transaction.Transaction, request, footprint);
            TResult result;
            stage = FailureStage.Action;
            unit.BeginAction();
            try { result = await action(unit, ct).ConfigureAwait(false); stage = FailureStage.PostAction; }
            catch (OperationCanceledException cancellation)
            {
                if (!recoverCommit && declaration.Kind == CommandWorkUnitKind.Ordinary
                    && (!readOnly || request is CommandRequestContext.TrustedInternal
                        && (request.HttpOperation is HttpRouteOperation.Events or HttpRouteOperation.WeeklyAttendance or HttpRouteOperation.JoustSettings or HttpRouteOperation.JoustRound
                            || request.TimerReadOnlyCancellation == TimerReadOnlyCancellationCapability.PremiumSnapshot && declaration.Name == "premium.snapshot"
                            || request.TimerReadOnlyCancellation == TimerReadOnlyCancellationCapability.ItemUsageSnapshot && declaration.Name == "item-usage.snapshot")))
                    request.ProducerCancellation?.ObserveAction(request, lease, ct, cancellation);
                throw;
            }
            finally { await unit.EndActionAsync().ConfigureAwait(false); nonRequest?.Close(); declaredNonRequest?.Close(); }
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
                stage = FailureStage.Commit;
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
                catch (Exception cleanup) { request.ProducerCancellation?.Invalidate(); error = cleanup; }
                if (!readOnly && lease.Completion == DomainTransactionCompletion.Committed)
                    request.ConfirmCommit(participants);
                if (lease.Completion == DomainTransactionCompletion.Uncertain || TransactionOutcomeUncertainException.Contains(error))
                    request.Uncertain(participants);
            }
            main?.Failed(lease?.Completion == DomainTransactionCompletion.ConfirmedRollback,
                lease?.Completion == DomainTransactionCompletion.Uncertain || TransactionOutcomeUncertainException.Contains(error));
            request.Failed(error);
            if (lease is not null && lease.GuardFailure is null && stage is FailureStage.Action or FailureStage.Commit)
                httpFailure = (cause, error, stage == FailureStage.Commit);
            recordedFailure = true;
            ExceptionDispatchInfo.Capture(error).Throw();
            throw;
        }
        finally
        {
            nonRequest?.Close();
            declaredNonRequest?.Close();
            unit?.Close();
            if (began) request.EndUnit();
            main?.EndMain();
            if (lease is not null)
            {
                try
                {
                    await lease.DisposeAsync().ConfigureAwait(false);
                    request.ProducerCancellation?.CompleteAction(lease);
                }
                catch (Exception cleanup)
                {
                    request.ProducerCancellation?.Invalidate();
                    var retained = unit?.RollbackCause ?? commitRollbackCause;
                    var error = !recordedFailure && retained is not null
                        ? new AggregateException("Rollback completed but lease cleanup failed.", retained, cleanup) : cleanup;
                    request.FailedIndependent(error, lease);
                    httpFailure = null;
                    ExceptionDispatchInfo.Capture(error).Throw(); throw;
                }
                if (httpFailure is { } http)
                    request.HttpFailure?.ObserveFailure(declaration.Name, http.Commit, http.Cause, http.Output, lease, ct);
            }
        }
    }

    private enum FailureStage { Setup, Action, PostAction, Commit, GuardPropagation, BusinessRollback, PostRollback, RecoveredValidation }

    internal static IEnumerable<SelectedSessionRecipient> ProtectionTargets(IReadOnlyList<SelectedSessionRecipient> declared)
    {
        var physicalSessions = declared.Where(p => p.Connection is not null).Select(p => p.Session).ToHashSet();
        var seen = new List<SelectedSessionRecipient>();
        foreach (var target in declared)
        {
            if (target.Connection is null && physicalSessions.Contains(target.Session)) continue;
            if (seen.Any(previous => previous.Session == target.Session && ReferenceEquals(previous.Connection, target.Connection))) continue;
            seen.Add(target);
            yield return target;
        }
    }

    internal static async ValueTask<Exception> FailAsync(CommandRequestContext request, Exception cause, SelectedSessionSafety safety, string operation)
    {
        request.Failed(cause);
        var error = request.Failure!;
        if (request.RetainsRawEncoderFailure(error)) return error;
        if (request.HasCommittedUnit && !TransactionOutcomeUncertainException.Contains(error))
            error = new TransactionOutcomeUncertainException(TransactionUncertaintyReason.PostCommitPublicationFailure,
                "A command failed after a confirmed work-unit commit.", error);
        foreach (var recipient in ProtectionTargets(request.CommittedParticipants))
        {
            try { await safety.BlockAsync(recipient, error, operation).ConfigureAwait(false); }
            catch (Exception cleanup) { error = new AggregateException("Settlement fencing failed.", error, cleanup); }
        }
        request.Failed(error);
        return error;
    }
}
