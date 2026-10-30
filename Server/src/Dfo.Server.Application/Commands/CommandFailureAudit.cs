using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Application.Transactions;

namespace Dfo.Server.Application.Commands;

/// <summary>The registered command may write one fixed failure record after its declared unit failed the request.</summary>
public enum CommandFailureAuditCapability { None, AuditedFailure }

/// <summary>
/// The part a work unit plays for the failure audit. Only an <see cref="Execution"/> unit's confirmed rollback or
/// uncertain completion is audited; a <see cref="Preparation"/> unit is audited only for the producer's requested cancellation.
/// </summary>
public enum CommandFailureAuditRole { None, Preparation, Execution }

public enum CommandFailureAuditOutcome { RolledBack, Rejected, Unknown, AuditFailed }

/// <summary>What the mechanism established about the failed unit; the command decides what to record from it.</summary>
public sealed record CommandFailureAuditFact(Exception Cause, CommandFailureAuditOutcome Outcome);

/// <summary>Only the fixed failure audit of a failed request. It never restores ordinary command work.</summary>
public interface ICommandFailureAudit
{
    ValueTask RecordAsync(CommandExecutionContext context, Exception cause,
        Func<CommandFailureAuditFact, CancellationToken, ValueTask> record, CancellationToken token);
}

public sealed class CommandAuditedFailureException : Exception
{
    private readonly ICommand owner;
    private readonly Guid job;
    internal CommandAuditedFailureException(ICommand command, Guid jobId, Exception auditedCause, CommandFailureAuditOutcome outcome, Exception cause)
        : base("The command failed; its failure audit has finished.", cause)
    { owner = command; job = jobId; AuditedCause = auditedCause; Outcome = outcome; }
    /// <summary>The unit's own failure that was audited.</summary>
    public Exception AuditedCause { get; }
    public CommandFailureAuditOutcome Outcome { get; }
    public bool BelongsTo(ICommand command, Guid chainId) => ReferenceEquals(owner, command) && job == chainId;
}

internal sealed class CommandFailureAuditState(CommandRequestContext request, CancellationToken producerToken)
{
    private readonly Guid job = GameplayJob.Current!.Value.Id;
    private FailureFact? fact;
    private bool claimed;
    private bool finished;
    private bool invalidated;
    internal void Invalidate() => invalidated = true;
    internal void OrdinaryRunnerAttempted() { if (fact is not null || claimed) Invalidate(); }
    private object[]? expectedSources;
    private CommandFailureAuditOutcome? outcome;
    internal void ObserveFailure(CommandFailureAuditRole role, bool actualCommit, Exception cause, Exception output,
        DomainTransactionLease lease, CancellationToken actionToken)
    {
        var cancellation = HasCancellation(cause);
        var requestedCancellation = cancellation && producerToken.IsCancellationRequested && actionToken == producerToken;
        if (fact is not null || claimed || cancellation && !requestedCancellation
            || !actualCommit && !requestedCancellation && (lease.ActionAbort is not { } aborted || !ReferenceEquals(aborted.Output, cause)
                || aborted.Completion != DomainTransactionCompletion.ConfirmedRollback)
            || CleanupUncertainty(output)
            || lease.GuardFailure is not null || !request.HasOnlyFailure(output)
            || lease.Completion is not (DomainTransactionCompletion.ConfirmedRollback or DomainTransactionCompletion.Uncertain)
            || role != CommandFailureAuditRole.Execution && !(requestedCancellation && role == CommandFailureAuditRole.Preparation)) return;
        fact = new(cause, output, lease.Completion, role == CommandFailureAuditRole.Preparation);
    }
    internal CommandFailureAuditFact Claim(Exception cause)
    {
        request.DemandFailureAuditLifetime();
        if (invalidated || claimed || fact is null || !ReferenceEquals(fact.Cause, cause)
            || !request.HasOnlyFailure(fact.Output) || !request.OperationsCompleted)
            throw new InvalidOperationException("The failure audit has no matching completed unit failure.");
        claimed = true;
        request.ProducerCancellation?.Invalidate();
        return new(fact.Cause, fact.Completion == DomainTransactionCompletion.Uncertain ? CommandFailureAuditOutcome.Unknown
            : fact.Preparing ? CommandFailureAuditOutcome.Rejected : CommandFailureAuditOutcome.RolledBack);
    }
    internal void Finish(CommandFailureAuditFact claim, Exception? auditActionFailure)
    {
        request.DemandFailureAuditLifetime();
        if (!claimed || finished) throw new InvalidOperationException("The failure audit has ended.");
        finished = true;
        expectedSources = request.FailureSources();
        outcome = auditActionFailure is not null ? CommandFailureAuditOutcome.AuditFailed : claim.Outcome;
    }
    internal bool TryFinalize(out Exception auditedCause, out CommandFailureAuditOutcome result)
    {
        auditedCause = fact?.Cause!;
        result = outcome.GetValueOrDefault();
        return !invalidated && finished && outcome is not null && GameplayJob.Current?.Id == job && request.IsClosed
            && request.OperationsCompleted && expectedSources is not null
            && request.FailureSources().SequenceEqual(expectedSources, ReferenceEqualityComparer.Instance);
    }
    private static bool HasCancellation(Exception error) => error is OperationCanceledException
        || error is AggregateException aggregate && aggregate.InnerExceptions.Any(HasCancellation)
        || error.InnerException is { } inner && HasCancellation(inner);
    private static bool CleanupUncertainty(Exception error) =>
        error is Dfo.Server.Abstractions.Transactions.TransactionOutcomeUncertainException uncertain
            && uncertain.Reason != Dfo.Server.Abstractions.Transactions.TransactionUncertaintyReason.NonFallibleCommitContractViolation
        || error is AggregateException aggregate && aggregate.InnerExceptions.Any(CleanupUncertainty)
        || error.InnerException is { } inner && CleanupUncertainty(inner);
    private sealed record FailureFact(Exception Cause, Exception Output, DomainTransactionCompletion Completion, bool Preparing);
}
