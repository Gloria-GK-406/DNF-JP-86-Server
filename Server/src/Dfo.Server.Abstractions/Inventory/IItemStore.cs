using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Abstractions.Inventory;

/// <summary>
/// Where a set of containers' items are kept.
/// </summary>
/// <remarks>
/// The store is the thing a transaction settles, not the container. Two containers backed by the
/// same store share one outcome because they share one session, and a store asked repeatedly
/// during one use case takes part exactly once -- which is also what lets its reads see its own
/// uncommitted writes.
/// </remarks>
public interface IItemStore
{
    /// <summary>What the transaction keys enlistment on. Equal identities are the same store.</summary>
    object StoreIdentity { get; }

    /// <summary>
    /// Whether what this store holds outlives the process, and with it whether its commit can
    /// still fail. A transaction admits at most one store for which this is true.
    /// </summary>
    bool IsDurable { get; }

    /// <summary>
    /// This store's session in the given transaction, joining it on first use.
    /// </summary>
    IItemStoreSession SessionIn(IDomainTransaction transaction);
}

/// <summary>
/// One use case's reads and writes against one store.
/// </summary>
/// <remarks>
/// Everything is addressed by container and slot. There is no identifier for an item, which is
/// deliberate: a move is expressed as a clear and a write, and a store that handed out identities
/// would be inviting callers to address the thing rather than the place.
///
/// A write carries the whole of what a slot holds. Enchantments and rolled options are inside the
/// core, and so is the one fact kept in another table: an avatar's details hang off the avatar UID
/// the core carries, not off the inventory row, so they follow the core without this session ever
/// naming them. That is what makes a clear plus a write lossless, and with it the reason no item
/// needs an identity of its own.
///
/// The writes are staged, not applied. They become visible to this session's own reads at once and
/// to everyone else only when the transaction settles.
/// </remarks>
public interface IItemStoreSession
{
    /// <summary>Every occupied slot of one container, in slot order.</summary>
    ValueTask<IReadOnlyList<StoredItem>> ReadAllAsync(
        ContainerId containerId, CancellationToken cancellationToken);

    /// <summary>What one slot holds, or null when it is empty.</summary>
    ValueTask<StoredItem?> ReadAsync(
        ContainerId containerId, int slotIndex, CancellationToken cancellationToken);

    /// <summary>Puts this exact item in its slot, replacing whatever was there.</summary>
    void Write(ContainerId containerId, StoredItem item);

    /// <summary>Leaves the slot empty.</summary>
    void Clear(ContainerId containerId, int slotIndex);
}

/// <summary>The whole of what one slot of a store holds.</summary>
public sealed record StoredItem(int SlotIndex, InventoryItemCoreState Core);
