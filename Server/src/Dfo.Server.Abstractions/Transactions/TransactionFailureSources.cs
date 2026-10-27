namespace Dfo.Server.Abstractions.Transactions;

/// <summary>A local fact from a participant's actual failure point; it never grants execution or recovery.</summary>
public enum TransactionFailureKind { Unknown, Guard, KnownStorage }
public enum TransactionFailurePhase { Action, Prepare, FallibleCommit }
public sealed class TransactionFailureFact(object source, Exception cause, TransactionFailurePhase phase, TransactionFailureKind kind)
{
    public object Source { get; } = source;
    public Exception Cause { get; } = cause;
    public TransactionFailurePhase Phase { get; } = phase;
    public TransactionFailureKind Kind { get; } = kind;
    public object Occurrence { get; } = new();
}
public interface ITransactionFailureSource
{
    TransactionFailureFact? LastFailure { get; }
}
