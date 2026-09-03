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
///
/// Items are one kind of state among several, so the store contract itself is
/// <see cref="IDomainStore{TSession}"/>; what is particular to items is only the session's
/// vocabulary below.
/// </remarks>
public interface IItemDomainStore : IDomainStore<IItemDomainStoreSession>;

/// <summary>
/// One use case's reads and writes against one store of items.
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
public interface IItemDomainStoreSession : IDomainStoreSession<ContainerId, int, StoredItem>
{
    /// <summary>
    /// Turns everything this session has decided into the writes it calls for, still inside the
    /// open use case.
    /// </summary>
    /// <remarks>
    /// Ordinarily nothing needs this: a session's own reads already see what it has decided, and
    /// nobody outside the use case may see it at all. It exists for the one shape that falls
    /// between those two -- a use case that writes through this store and then reads the same rows
    /// through the tables, which do not share this session and would otherwise be shown the state
    /// before the writes.
    ///
    /// It does not settle anything and it does not widen what anyone outside the use case can see:
    /// the rows land inside the same open transaction, and a rollback still takes them all back. It
    /// is not a substitute for the boundary and grants no permission to write without one.
    /// </remarks>
    ValueTask ApplyDecidedWritesAsync(CancellationToken cancellationToken);
}

/// <summary>The whole of what one slot of a store holds.</summary>
public sealed record StoredItem(int SlotIndex, InventoryItemCoreState Core);
