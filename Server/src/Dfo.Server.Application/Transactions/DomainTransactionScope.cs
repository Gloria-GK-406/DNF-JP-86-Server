using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Execution.Outbox;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.Transactions;

/// <summary>
/// Holds the transaction of the use case currently executing on this flow.
/// </summary>
/// <remarks>
/// The stores that take part are singletons and the containers that drive them are held per
/// character, so neither can be handed the transaction by injection. They ask this scope for it
/// instead, which is what keeps the business code free of any transaction vocabulary.
/// <para>
/// A writable transaction opened on a job opens the job's outbox work unit with it (plan "组装点"); a
/// read-only one and the read snapshot never commit, so they open none, and what is registered under
/// them counts as outside a unit.
/// </para>
/// </remarks>
internal sealed class DomainTransactionScope(
    ILogger<DomainTransaction> logger, GameplayOutboxes outboxes) : IDomainTransactionAccessor
{
    private readonly AsyncLocal<DomainTransaction?> ambient = new();

    /// <summary>
    /// The transaction of the use case running on this flow, if one is still open.
    /// </summary>
    /// <remarks>
    /// A settled transaction answers as none. Releasing the slot is not enough on its own: the
    /// release happens inside the lease's disposal, and an async method writes to a copy of the
    /// flow's state, so whoever opened the transaction can still be holding the old value. Reading
    /// the transaction's own state instead means a settled one is never joined late -- it would
    /// refuse anyway, but the caller is told there is nothing open rather than handed something
    /// that throws the moment it is used.
    /// </remarks>
    public IDomainTransaction? Current =>
        this.ambient.Value is { IsSettled: false } open ? open : null;

    public DomainTransactionLease Begin(bool readOnly = false)
    {
        if (!readOnly) GameplayWriteAssertion.Writing(nameof(DomainTransactionScope) + "." + nameof(Begin));
        if (this.ambient.Value is { IsSettled: false })
        {
            throw new InvalidOperationException(
                "A domain transaction is already open on this flow. One use case opens one.");
        }

        var transaction = new DomainTransaction(readOnly, logger, readOnly ? null : outboxes.CurrentOrNull?.BeginUnit());
        this.ambient.Value = transaction;
        return new DomainTransactionLease(transaction);
    }

    /// <summary><see cref="Begin"/> in write mode without the RQ-061 assertion, for the never-committed read snapshot.</summary>
    /// <remarks>
    /// Only <c>AmbientStoreAccess.ReadSnapshotAsync</c> may call it, and that never commits; both are pinned by
    /// <c>GameplayWriteEntryTableTests</c> (TASK-18 ruling 3). Write mode is kept because item custody sessions
    /// enlist lifecycle participants a read-only transaction refuses.
    /// </remarks>
    internal DomainTransactionLease BeginSnapshot()
    {
        if (this.ambient.Value is { IsSettled: false })
        {
            throw new InvalidOperationException(
                "A domain transaction is already open on this flow. One use case opens one.");
        }

        var transaction = new DomainTransaction(readOnly: false, logger);
        this.ambient.Value = transaction;
        return new DomainTransactionLease(transaction);
    }
}

/// <summary>The open transaction, held by the use case boundary that opened it.</summary>
/// <remarks>
/// Disposal does not give the flow's slot back, because it cannot: this runs inside an async
/// method, whose writes to the flow's state land on a copy. Nothing needs it to. The slot belongs
/// to the async method that opened the transaction and goes when that method returns, and until
/// then the transaction it holds reads as settled, which is the same as holding nothing.
/// </remarks>
internal sealed class DomainTransactionLease(DomainTransaction transaction) : IAsyncDisposable
{
    internal DomainTransactionCompletion Completion => transaction.Completion;
    internal TransactionFailureFact? CommitFailure => transaction.CommitFailure;
    internal Exception? GuardFailure => transaction.GuardFailure;
    internal IDomainTransaction Transaction => transaction;
    internal GameplayOutboxUnit? OutboxUnit => transaction.OutboxUnit;
    internal DomainTransaction.DomainActionAbortFact? ActionAbort => transaction.ActionAbort;
    internal object ObserveAction() => transaction.ObserveAction();
    internal DomainTransaction.DomainActionRollbackFact? OwnActionFact(object call) =>
        transaction.ActionRollback is { } fact && ReferenceEquals(fact.Transaction, transaction) && ReferenceEquals(fact.Call, call) ? fact : null;
    internal DomainTransaction.DomainActionRollbackFact? ConsumeAction(object call, Exception error) =>
        transaction.ActionRollback is { } fact && fact.TryConsume(transaction, call, error) ? fact : null;
    internal ValueTask AbortAsync(Exception cause) => transaction.AbortAsync(cause);

    public ValueTask CommitAsync(CancellationToken cancellationToken) =>
        transaction.CommitAsync(cancellationToken);

    // A settled transaction ignores this, so what it actually covers is every way of leaving the
    // use case without committing: a business failure, a refusal, a throw.
    public ValueTask DisposeAsync() => transaction.RollbackAsync();
}
