using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Application.Transactions;

namespace Dfo.Server.Application.Commands;

public enum CommandWorkUnitKind { Ordinary, MainSettlement }
public enum CommandFootprintMode { None, NonRequest, DeclaredNonRequest }
public sealed record CommandWorkUnitDeclaration(string Name, IReadOnlyList<SelectedSessionRecipient> Participants,
    CommandWorkUnitKind Kind = CommandWorkUnitKind.Ordinary, CommandFootprintMode Footprint = CommandFootprintMode.None,
    IReadOnlyList<Dfo.Server.Data.World.SelectedSessionIdentity>? FootprintOwners = null);

public sealed class CommandWorkUnit
{
    private readonly IDomainTransaction transaction;
    private readonly CommandRequestContext request;
    private readonly DomainTransactionLease? selfLease;
    private object? selfCall;
    private Task? observation;
    internal DomainTransaction.DomainActionRollbackFact? ObservedFact { get; private set; }
    internal CommandSelfSettlementPropagationException? Handoff { get; private set; }
    private bool open = true;
    private readonly IEffectFootprintRecorder? footprint;
    internal CommandWorkUnit(IDomainTransaction transaction, CommandRequestContext request, IEffectFootprintRecorder? footprint = null, DomainTransactionLease? selfLease = null)
    { this.transaction = transaction; this.request = request; this.footprint = footprint; this.selfLease = selfLease; }
    public ValueTask<T> InvokeSelfSettlingAsync<T>(Func<CancellationToken, ValueTask<T>> action,
        CancellationToken cancellationToken = default) where T : notnull
    {
        try
        {
            request.DemandCurrent();
            if (!open || selfLease is null || selfCall is not null)
                throw new InvalidOperationException("This work unit cannot observe another self-settling action.");
            ArgumentNullException.ThrowIfNull(action);
            selfCall = selfLease.ObserveAction();
        }
        catch (Exception error) { RecordSelfGuard(error); throw; }
        var operation = ObserveAsync(action, cancellationToken).AsTask();
        observation = operation;
        return new(operation);
    }
    private async ValueTask<T> ObserveAsync<T>(Func<CancellationToken, ValueTask<T>> action, CancellationToken ct) where T : notnull
    {
        T result;
        try { result = await action(ct).ConfigureAwait(false); }
        catch (Exception error)
        {
            ObservedFact = selfLease!.OwnActionFact(selfCall!);
            if (selfLease.ConsumeAction(selfCall!, error) is not { } fact)
            {
                if (ObservedFact is { } owned)
                {
                    // Diagnostic ownership does not grant recovery for a different actual output.
                    request.FailedIndependent(error, new object());
                    request.FailedIndependent(owned.Output, owned.Occurrence);
                }
                else if (selfLease.Completion != DomainTransactionCompletion.Open) request.Failed(error);
                throw;
            }
            request.BeginSelfSettlement(selfCall!);
            Handoff = new(this, selfCall!, fact);
            throw Handoff;
        }
        if (selfLease!.OwnActionFact(selfCall!) is { } aborted)
        {
            ObservedFact = aborted;
            var error = new InvalidOperationException("The directly observed action returned after aborting its transaction.");
            request.FailedIndependent(error, new object());
            request.FailedIndependent(aborted.Output, aborted.Occurrence);
            throw error;
        }
        return result;
    }
    internal bool HasObservation => observation is not null;
    internal bool HasUnfinishedObservation => observation is { IsCompleted: false };
    internal async ValueTask DrainObservationAsync()
    {
        if (observation is null) return;
        try { await observation.ConfigureAwait(false); }
        catch (Exception error)
        {
            // The owning runner adjudicates its own one-time handoff, including its original action fact.
            // A completed fault is accepted only by this unit's explicit same-cause local rollback.
            var acceptedRollback = request.Failure is null && transaction.IsRollbackOnly
                && ReferenceEquals(error, RollbackCause) && RejectedRollbackCause is null;
            if (!ReferenceEquals(error, Handoff) && !acceptedRollback) request.Failed(error);
        }
    }
    private void RecordSelfGuard(Exception error)
    {
        if (selfLease is null || request.Failure is not null) return;
        var guard = selfLease.OutboxUnit?.SelfSettlementGuards.FirstOrDefault(item => ReferenceEquals(item.Error, error));
        request.FailedIndependent(error, guard?.Occurrence ?? new object());
    }
    internal object? SelfCall => selfCall;
    public IEffectFootprintRecorder Footprint
    {
        get
        {
            request.DemandCurrent();
            if (!open || footprint is null) throw new InvalidOperationException("This work unit has no live declared effect footprint.");
            return footprint;
        }
    }
    public void RequireRollback()
    {
        try
        {
            request.DemandCurrent();
            if (!open) throw new InvalidOperationException("The command work unit has ended.");
            transaction.RequireRollback();
        }
        catch (Exception error) { RecordSelfGuard(error); throw; }
    }
    internal Exception? RollbackCause { get; private set; }
    internal Exception? RejectedRollbackCause { get; private set; }
    public void RequireRollback(Exception cause)
    {
        ArgumentNullException.ThrowIfNull(cause);
        RequireRollback();
        RollbackCause ??= cause;
        if (cause is OperationCanceledException || TransactionOutcomeUncertainException.Contains(cause))
        {
            RejectedRollbackCause = cause;
            request.Failed(cause);
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(cause);
        }
    }
    internal void Close() => open = false;
}

public abstract class CommandWriteResult<T> where T : notnull
{
    private CommandWriteResult() { }
    public sealed class Returned : CommandWriteResult<T>
    {
        internal Returned(T value) => Value = value;
        public T Value { get; }
    }
    public sealed class CommitRolledBack : CommandWriteResult<T>
    {
        internal CommitRolledBack(Exception cause) => Cause = cause;
        public Exception Cause { get; }
        public CommandWriteCommitAttempt CommitAttempt => CommandWriteCommitAttempt.CommitAsync;
    }
}
public enum CommandWriteCommitAttempt { CommitAsync }

public abstract class CommandSelfSettledWriteResult<T> where T : notnull
{
    private CommandSelfSettledWriteResult() { }
    public sealed class Returned : CommandSelfSettledWriteResult<T>
    {
        internal Returned(T value) => Value = value;
        public T Value { get; }
    }
    public sealed class ActionRolledBack : CommandSelfSettledWriteResult<T>
    {
        internal ActionRolledBack(Exception cause) => Cause = cause;
        public Exception Cause { get; }
    }
    public sealed class CommitRolledBack : CommandSelfSettledWriteResult<T>
    {
        internal CommitRolledBack(Exception cause) => Cause = cause;
        public Exception Cause { get; }
    }
}

public sealed class CommandSelfSettlementPropagationException : Exception
{
    internal CommandSelfSettlementPropagationException(CommandWorkUnit unit, object call,
        DomainTransaction.DomainActionRollbackFact fact) : base("A self-settling domain action completed its abort.", fact.Output)
    { Unit = unit; Call = call; Fact = fact; }
    internal CommandWorkUnit Unit { get; }
    internal object Call { get; }
    internal DomainTransaction.DomainActionRollbackFact Fact { get; }
}

public interface ICommandWorkUnits
{
    ValueTask<CommandSelfSettledWriteResult<TResult>> WriteSelfSettledResultAsync<TResult>(CommandExecutionContext context,
        CommandWorkUnitDeclaration declaration, Func<CommandWorkUnit, CancellationToken, ValueTask<TResult>> action,
        CancellationToken cancellationToken = default) where TResult : notnull =>
        ValueTask.FromException<CommandSelfSettledWriteResult<TResult>>(new NotSupportedException("This work-unit adapter does not support self settlement."));
    ValueTask<CommandWriteResult<TResult>> WriteResultAsync<TResult>(CommandExecutionContext context, CommandWorkUnitDeclaration declaration,
        Func<CommandWorkUnit, CancellationToken, ValueTask<TResult>> action, CancellationToken cancellationToken = default)
        where TResult : notnull;
    ValueTask<TResult> WriteAsync<TResult>(CommandExecutionContext context, CommandWorkUnitDeclaration declaration,
        Func<CommandWorkUnit, CancellationToken, ValueTask<TResult>> action, CancellationToken cancellationToken = default)
        where TResult : notnull;
    ValueTask<TResult> ReadOnlyAsync<TResult>(CommandExecutionContext context, CommandWorkUnitDeclaration declaration,
        Func<CancellationToken, ValueTask<TResult>> action, CancellationToken cancellationToken = default)
        where TResult : notnull;
}
