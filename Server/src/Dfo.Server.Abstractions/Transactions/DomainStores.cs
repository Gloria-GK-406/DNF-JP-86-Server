namespace Dfo.Server.Abstractions.Transactions;

/// <summary>
/// Where one kind of state is kept.
/// </summary>
/// <remarks>
/// A store is defined here rather than beside the state it holds because what makes it a store is
/// how it takes part in a use case: it is asked for a session, and the transaction settles it.
///
/// <see cref="StoreIdentity"/> is what the transaction keys enlistment on, so it decides what
/// "the same store" means. Two stores that would write the same place must answer equal
/// identities, or each would settle without knowing about the other's changes.
/// </remarks>
public interface IDomainStore
{
    /// <summary>What the transaction keys enlistment on. Equal identities are the same store.</summary>
    object StoreIdentity { get; }

    /// <summary>
    /// Whether what this store holds outlives the process, and with it whether its commit can
    /// still fail. A transaction admits at most one store for which this is true.
    /// </summary>
    bool IsDurable { get; }
}

/// <summary>A store that hands one use case a session of its own kind.</summary>
/// <remarks>
/// The session type is the store's own, not a shared one. That is deliberate: the vocabulary of
/// what a store keeps -- a container's slots, a character's sections -- is part of the state, and
/// flattening it into a single untyped session would trade away exactly the checking that makes
/// these worth having. What the stores share is how they are settled, not what they say.
/// </remarks>
public interface IDomainStore<out TSession> : IDomainStore
    where TSession : class
{
    /// <summary>This store's session in the given transaction, joining it on first use.</summary>
    TSession SessionIn(IDomainTransaction transaction);
}

/// <summary>
/// One use case's reads and writes against one store, addressed by place and key.
/// </summary>
/// <remarks>
/// Every operation names where it acts. There is no identifier for a value, which is the point: a
/// value that moves is expressed as a clear and a write, and a store that handed out identities
/// would invite callers to address the thing rather than the place.
///
/// A write carries the whole of what its place holds, so a clear plus a write loses nothing and
/// no value needs an identity of its own.
///
/// The writes are staged, not applied. They become visible to this session's own reads at once and
/// to everyone else only when the transaction settles.
/// </remarks>
public interface IStoreSession<in TPlace, in TKey, TValue>
    where TValue : class
{
    /// <summary>Everything one place holds, in key order.</summary>
    ValueTask<IReadOnlyList<TValue>> ReadAllAsync(TPlace place, CancellationToken cancellationToken);

    /// <summary>What one key of one place holds, or null when it holds nothing.</summary>
    ValueTask<TValue?> ReadAsync(TPlace place, TKey key, CancellationToken cancellationToken);

    /// <summary>Puts this exact value in its place, replacing whatever was there.</summary>
    void Write(TPlace place, TValue value);

    /// <summary>Leaves one key of one place holding nothing.</summary>
    void Clear(TPlace place, TKey key);
}
