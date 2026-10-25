using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Application.Transactions;

namespace Dfo.Server.Application.Commands;

internal enum ProducerCancellationState { Empty, ActionObserved, Qualified, Finalized, Marshalled, Invalid }

/// <summary>One actual cancellation occurrence; it conveys no permission to resume a failed request.</summary>
internal sealed class ProducerCancellationObservation(CancellationToken producerToken)
{
    private readonly Guid job = GameplayJob.Current?.Id ?? throw new InvalidOperationException("Cancellation observation requires a job.");
    private CommandRequestContext? request;
    private object? occurrence;
    private OperationCanceledException? cause;
    private DomainTransactionLease? lease;
    internal ProducerCancellationState State { get; private set; }
    internal bool AnyRunnerAttempted { get; private set; }
    internal void Bind(CommandRequestContext.TrustedInternal owner) => request = owner;
    internal void RunnerAttempted() { AnyRunnerAttempted = true; if (State != ProducerCancellationState.Empty) Invalidate(); }
    internal void Invalidate() => State = ProducerCancellationState.Invalid;
    private bool Eligible(CommandRequestContext owner, CancellationToken token, Exception error) =>
        ReferenceEquals(request, owner) && GameplayJob.Current?.Id == job && producerToken.IsCancellationRequested
        && token == producerToken && error is OperationCanceledException && owner.Failure is null && State == ProducerCancellationState.Empty;
    internal void ObserveAction(CommandRequestContext owner, DomainTransactionLease actionLease, CancellationToken token, Exception error)
    {
        if (!Eligible(owner, token, error) || actionLease.GuardFailure is not null) return;
        lease = actionLease;
        Observe(owner, (OperationCanceledException)error, ProducerCancellationState.ActionObserved);
    }
    internal void ObserveDirectBody(CommandRequestContext owner, CancellationToken token, Exception error)
    {
        if (AnyRunnerAttempted || !Eligible(owner, token, error)) return;
        Observe(owner, (OperationCanceledException)error, ProducerCancellationState.Qualified);
    }
    private void Observe(CommandRequestContext owner, OperationCanceledException error, ProducerCancellationState state)
    {
        cause = error; occurrence = new object(); State = state;
        owner.FailedIndependent(error, occurrence);
    }
    internal void FailureRecorded(object source)
    {
        if (!ReferenceEquals(source, occurrence)) Invalidate();
    }
    internal void CompleteAction(DomainTransactionLease completed)
    {
        if (State != ProducerCancellationState.ActionObserved || !ReferenceEquals(lease, completed)) return;
        if (completed.Completion != DomainTransactionCompletion.ConfirmedRollback || completed.GuardFailure is not null)
            Invalidate();
        else State = ProducerCancellationState.Qualified;
    }
    internal void Finalize(CommandRequestContext? owner, Exception failure)
    {
        if (State == ProducerCancellationState.Qualified && ReferenceEquals(request, owner)
            && GameplayJob.Current?.Id == job && producerToken.IsCancellationRequested
            && ReferenceEquals(cause, failure) && owner is { HasCommittedUnit: false }
            && owner.HasOnlyFailure(occurrence!, cause!) && owner.OperationsCompleted)
            State = ProducerCancellationState.Finalized;
        else Invalidate();
    }
    internal bool TryMarshal(OperationCanceledException error)
    {
        if (State != ProducerCancellationState.Finalized || !ReferenceEquals(cause, error) || GameplayJob.Current?.Id != job) return false;
        State = ProducerCancellationState.Marshalled;
        return true;
    }
}
