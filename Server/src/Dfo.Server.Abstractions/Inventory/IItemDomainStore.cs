using Dfo.Server.Abstractions.Content;
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
/// Everything is addressed by container and slot. There is no identifier for an item here, which
/// stays deliberate: a move is said as a clear and a write, and a session that took identities from
/// its callers would be inviting them to hold one between a read and a write.
///
/// A write carries the whole of what a slot holds, plus what the archive makes of it. The class is
/// what settlement decides on: a thing with an identity keeps its row and is referred to from the
/// position that holds it, while a pile is counted in the position and keeps nothing. So a move no
/// longer destroys what moved -- the row survives the batch and its position changes -- and the
/// identity of the thing is settlement's to keep rather than a caller's to carry.
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
/// <remarks>
/// <see cref="WorldClass"/> is what the archive says the thing being written is. It is answered on
/// the way in and left unanswered on the way out: a write comes from a container, which asks the
/// classification before it stages anything, while a read comes out of the rows and the store has
/// no archive to ask. Null therefore means "not asked", not "a pile".
/// <para>
/// <b>Settlement decides on it, so a write must answer it.</b> Whether the thing keeps a row of its
/// own and an identity that outlives its position, or is a count in the position and nothing else,
/// is read off this and off nothing else. A write that left it unanswered is refused rather than
/// guessed at: the kind byte a core carries is the reading this classification exists to replace,
/// and taking silence for "a pile" would throw away the identity of everything it was wrong about.
/// A read may leave it unanswered because a read decides nothing.
/// </para>
/// <para>
/// <see cref="Identity"/> runs the other way: a read answers with the thing standing in the slot,
/// and a write leaves it unanswered because settlement is what decides which row a write lands on.
/// It is there for the one question a slot number cannot answer -- <b>which</b> thing this is --
/// which is what a caller needs before it can address that thing's own state anywhere else. It is
/// answered out of the rows, so a read served from this session's own staged writes has none.
/// </para>
/// <para>
/// <b>It is not a token to hold on to.</b> Nothing here accepts one back, and nothing may keep one
/// across a use case: a thing addressed by an identity read in an earlier transaction is a thing
/// whose state the caller is guessing at. Within one use case it is an address, and addresses are
/// exactly what the position table exists to hand out.
/// </para>
/// <para>
/// <see cref="AvatarDetail"/> runs the same way round as <see cref="WorldClass"/>: answered on the
/// way in and left unanswered on the way out. It is the six facts an avatar carries -- when it
/// expires, which avatar it clears, its jewel sockets, its two colours, when it is deleted. A read
/// comes out of the rows and hands back the position and the core and nothing else, so null on a
/// read means "not asked" rather than "an avatar with nothing on it": an avatar always carries the
/// six, an all-zero set included.
/// </para>
/// <para>
/// <b>Settlement writes them, so a write that brings an avatar into the world must answer them.</b>
/// The six are columns of the world row, and that row is written once -- when the world first takes
/// the thing. A write that brought an avatar in and left them unanswered would land a row whose six
/// columns are all null, which is a row this server cannot read back as an avatar at all.
/// </para>
/// <para>
/// Unlike <see cref="WorldClass"/>, silence is not refused at the write, because settlement has a
/// second reading for this one and has none for that one: an avatar the world is already carrying
/// is handed its own six back when its row is rebuilt, so a write that merely moves one has nothing
/// to add and is right to say nothing. The obligation is therefore on the write that brings an
/// avatar in, and it is an obligation rather than a check because nothing at the moment of the
/// write can tell that write apart from the one that moves an avatar already standing.
/// </para>
/// <para>
/// A thing that keeps its row keeps its own six columns, and an answer here does not overwrite
/// them: settlement puts these on the row it creates, which is the same rule that makes an avatar's
/// state follow the avatar rather than the position.
/// </para>
/// </remarks>
public sealed record StoredItem(int SlotIndex, InventoryItemCoreState Core)
{
    /// <summary>What the archive says this is. Required on a write; null on a read.</summary>
    public ItemWorldClass? WorldClass { get; init; }

    /// <summary>Which thing this is. Answered on a read from the rows; null on a write.</summary>
    public ItemIdentity? Identity { get; init; }

    /// <summary>
    /// The six facts this avatar carries. Required on a write that brings an avatar into the world;
    /// null on a read, null on the write of anything that is not an avatar, and null on the write
    /// that merely moves an avatar the world is already carrying, which has nothing to add to the
    /// six that avatar is already standing with.
    /// </summary>
    public InventoryAvatarDetailState? AvatarDetail { get; init; }
}
