namespace Dfo.Server.Abstractions.Transactions;

/// <summary>
/// A state holder that takes part in one use case's all-or-nothing outcome.
/// </summary>
/// <remarks>
/// Splitting the work into <see cref="PrepareAsync"/> and <see cref="CommitAsync"/> is what
/// lets a mixed set of participants -- some backed by a database, some living only in memory
/// -- succeed or fail together without a distributed commit protocol. Everything that can
/// fail happens during prepare, while nothing is visible yet, so the only step that can still
/// fail afterwards belongs to the one participant that admits it can.
/// </remarks>
public interface ITransactionParticipant
{
    /// <summary>
    /// Whether this participant's <see cref="CommitAsync"/> can still fail. A transaction
    /// admits at most one such participant, because a commit that already succeeded cannot be
    /// taken back except by a compensating change that can itself fail.
    /// </summary>
    bool CommitCanFail { get; }

    /// <summary>
    /// Does everything that can fail. Nothing this participant shows to the rest of the
    /// system may change here.
    /// </summary>
    ValueTask PrepareAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Makes the prepared change visible. May fail only when <see cref="CommitCanFail"/>.
    /// </summary>
    ValueTask CommitAsync(CancellationToken cancellationToken);

    /// <summary>Discards the prepared change. Must not fail.</summary>
    ValueTask RollbackAsync(CancellationToken cancellationToken);
}

/// <summary>One use case's transaction. Participants join it; it decides their common outcome.</summary>
public interface IDomainTransaction
{
    /// <summary>
    /// The participant registered under <paramref name="identity"/>, creating and enlisting one
    /// on first use.
    /// </summary>
    /// <remarks>
    /// Registration is keyed on identity, so a store asked repeatedly during one use case takes
    /// part exactly once and its reads see its own uncommitted writes. A participant that is
    /// created and then rejected is simply dropped, so <paramref name="create"/> must not
    /// acquire a resource that would need releasing.
    /// </remarks>
    TParticipant Enlist<TParticipant>(object identity, Func<TParticipant> create)
        where TParticipant : ITransactionParticipant;
}

/// <summary>The transaction of the use case currently executing, when one is open.</summary>
public interface IDomainTransactionAccessor
{
    /// <summary>The current use case's transaction, or <see langword="null"/> outside one.</summary>
    IDomainTransaction? Current { get; }
}
