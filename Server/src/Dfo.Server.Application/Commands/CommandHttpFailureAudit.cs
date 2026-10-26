using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Application.Transactions;

namespace Dfo.Server.Application.Commands;

/// <summary>Only the fixed failure audit of this HTTP grant. It never restores ordinary command work.</summary>
public interface ICommandHttpFailureAudit
{
    ValueTask RecordAsync(CommandExecutionContext context, Exception cause, CancellationToken token);
}

public sealed class CommandHttpAuditedFailureException : Exception
{
    private readonly HttpRouteCommand owner;
    private readonly Guid job;
    internal CommandHttpAuditedFailureException(HttpRouteCommand command, Guid jobId, HttpRouteReply reply, Exception cause)
        : base("The HTTP operation failed; its failure audit has finished.", cause)
    { owner = command; job = jobId; Reply = reply; }
    public HttpRouteReply Reply { get; }
    public bool BelongsTo(HttpRouteCommand command, Guid chainId) => ReferenceEquals(owner, command) && job == chainId;
}

internal sealed class HttpCommandFailureState(CommandRequestContext request, HttpRouteCommand command, CancellationToken producerToken)
{
    private readonly Guid job = GameplayJob.Current!.Value.Id;
    private FailureFact? fact;
    private bool claimed;
    private bool finished;
    private bool invalidated;
    internal void Invalidate() => invalidated = true;
    internal void OrdinaryRunnerAttempted() { if (fact is not null || claimed) Invalidate(); }
    private object[]? expectedSources;
    private HttpRouteReply? reply;
    internal string Secret => command.Input switch
    {
        HttpRouteInput.CeraGrantRequest p => p.Secret,
        HttpRouteInput.MailSendRequest p => p.Secret,
        _ => throw new InvalidOperationException("This HTTP operation cannot audit a grant.")
    };
    internal Guid RequestId => command.Input switch
    {
        HttpRouteInput.CeraGrantRequest p => p.Value.RequestId,
        HttpRouteInput.MailSendRequest p => p.Value.RequestId,
        _ => Guid.Empty
    };
    internal bool IsCera => command.Operation == HttpRouteOperation.CeraGrant;
    internal void ObserveFailure(string name, bool actualCommit, Exception cause, Exception output,
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
            || name != (IsCera ? "grant-gm-cera" : "send-gm-mail")
                && !(requestedCancellation && name == (IsCera ? "query-gm-cera-normalize" : "query-gm-mail-prepare"))) return;
        fact = new(cause, output, lease.Completion, name is "query-gm-cera-normalize" or "query-gm-mail-prepare");
    }
    internal void Claim(Exception cause)
    {
        request.DemandHttpFailureAuditLifetime();
        if (invalidated || claimed || RequestId == Guid.Empty || fact is null || !ReferenceEquals(fact.Cause, cause)
            || !request.HasOnlyFailure(fact.Output) || !request.OperationsCompleted)
            throw new InvalidOperationException("The HTTP failure audit has no matching completed grant failure.");
        claimed = true;
        request.ProducerCancellation?.Invalidate();
    }
    internal string Reason => Outcome == "Unknown" ? "提交状态暂未确认" :
        (IsCera ? "发放请求未提交，原因：" : "发送请求未提交，原因：") + (fact!.Cause switch
        {
            Dfo.Server.Data.Administration.GmCeraException cera => cera.Message,
            Dfo.Server.Data.Administration.GmMailException mail => mail.Message,
            _ => "存储或规则处理失败"
        });
    internal string Outcome => fact!.Completion == DomainTransactionCompletion.Uncertain ? "Unknown" : fact.Preparing ? "Rejected" : "RolledBack";
    internal void Finish(Exception? auditActionFailure)
    {
        request.DemandHttpFailureAuditLifetime();
        if (!claimed || finished) throw new InvalidOperationException("The HTTP failure audit has ended.");
        finished = true;
        expectedSources = request.FailureSources();
        reply = new(Error: fact!.Cause, Outcome: auditActionFailure is not null ? "auditFailed" : Outcome == "Unknown" ? "unknown" : Outcome == "Rejected" ? "rejected" : "rolledBack");
    }
    internal bool TryFinalize(out HttpRouteReply result)
    {
        result = reply!;
        return !invalidated && finished && reply is not null && GameplayJob.Current?.Id == job && request.IsClosed
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
