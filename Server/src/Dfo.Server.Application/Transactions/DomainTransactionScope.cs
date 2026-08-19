using Dfo.Server.Abstractions.Transactions;

namespace Dfo.Server.Application.Transactions;

/// <summary>
/// Holds the transaction of the use case currently executing on this flow.
/// </summary>
/// <remarks>
/// The stores that take part are singletons and the containers that drive them are held per
/// character, so neither can be handed the transaction by injection. They ask this scope for it
/// instead, which is what keeps the business code free of any transaction vocabulary.
/// </remarks>
internal sealed class DomainTransactionScope : IDomainTransactionAccessor
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

    public DomainTransactionLease Begin()
    {
        if (this.ambient.Value is { IsSettled: false })
        {
            throw new InvalidOperationException(
                "A domain transaction is already open on this flow. One use case opens one.");
        }

        var transaction = new DomainTransaction();
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
    public ValueTask CommitAsync(CancellationToken cancellationToken) =>
        transaction.CommitAsync(cancellationToken);

    // A settled transaction ignores this, so what it actually covers is every way of leaving the
    // use case without committing: a business failure, a refusal, a throw.
    public ValueTask DisposeAsync() => transaction.RollbackAsync();
}
