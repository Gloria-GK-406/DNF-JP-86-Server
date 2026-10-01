namespace Dfo.Server.Abstractions.Transactions;

public enum TransactionUncertaintyReason
{
    NonFallibleCommitContractViolation,
    RollbackFailure,
    PostDurableCleanupFailure,
    SettlementCleanupFailure,
    PostCommitPublicationFailure,
}

/// <summary>The command cannot safely be assumed to have rolled back or be paid again.</summary>
public sealed class TransactionOutcomeUncertainException(
    TransactionUncertaintyReason reason, string message, Exception cause) : Exception(message, cause)
{
    public TransactionUncertaintyReason Reason { get; } = reason;

    public static bool Contains(Exception failure) => Find(failure) is not null;

    public static TransactionOutcomeUncertainException? Find(Exception failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        if (failure is TransactionOutcomeUncertainException uncertain) return uncertain;
        if (failure is AggregateException aggregate)
        {
            foreach (var cause in aggregate.InnerExceptions)
                if (Find(cause) is { } found) return found;
            return null;
        }
        return failure.InnerException is { } inner ? Find(inner) : null;
    }
}
