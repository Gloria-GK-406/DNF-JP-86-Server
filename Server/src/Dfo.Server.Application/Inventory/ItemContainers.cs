using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Inventory;

/// <summary>
/// The stack shape a container writes when it has to produce an item row of its own.
/// </summary>
/// <remarks>
/// A split makes a second pile out of one. The pile it makes carries only what a pile is -- kind,
/// identifier, count -- because everything else a core can hold describes an individual piece of
/// gear, and gear does not split. Persistent storage writes exactly this shape, so a transient
/// container that wrote a copy of the original instead would answer differently for the same move.
/// </remarks>
public static class StackableItemCore
{
    private static readonly ChronicleOptionState NoChronicle = new(0, 0, 0, 0, 0);

    private static readonly RandomOptionState NoRandom = new(0, 0, 0);

    private static readonly InventoryItemCoreState Empty = new(
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        NoChronicle, NoChronicle, 0, 0, 0, 0, 0,
        NoRandom, NoRandom, NoRandom, 0, 0, 0, NoRandom,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    public static InventoryItemCoreState Create(byte itemKind, int itemId, int count) =>
        Empty with
        {
            ItemKind = itemKind,
            ItemId = itemId,
            Value = count,
            Marker16 = -1,
            RandomOptionChangedIndex = byte.MaxValue,
        };
}

/// <summary>
/// The compound item operations, each said as the reads and writes it is made of.
/// </summary>
/// <remarks>
/// This is where "move" lives now. It used to live in persistent storage, which meant storage had
/// to know that a swap is not two relocations and that a merge deletes one of its two operands; the
/// container that could not store had to know the same things a second time, and the two agreed
/// only as long as somebody kept checking. Written here as clears and writes, there is one
/// description and both carriers run it.
///
/// Nothing here decides whether an operation is allowed. Whether the item may occupy the slot, and
/// whether a merge would burst the stack, are the protocol's questions and are answered before any
/// of this is called.
/// </remarks>
public static class ItemContainerOperations
{
    /// <summary>The whole pile goes to an empty slot.</summary>
    public static async ValueTask RelocateAsync(
        IItemContainer container,
        ContainerSlotItem moving,
        int targetSlotIndex,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(container);
        ArgumentNullException.ThrowIfNull(moving);
        await container.ClearAsync(moving.SlotIndex, cancellationToken).ConfigureAwait(false);
        await container.PutAsync(targetSlotIndex, moving.Core, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Part of the pile goes to an empty slot; the rest stays where it was.</summary>
    public static async ValueTask SplitAsync(
        IItemContainer container,
        ContainerSlotItem moving,
        int targetSlotIndex,
        int moveCount,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(container);
        ArgumentNullException.ThrowIfNull(moving);
        await container.PutAsync(
            moving.SlotIndex,
            moving.Core with { Value = moving.Count - moveCount },
            cancellationToken).ConfigureAwait(false);
        await container.PutAsync(
            targetSlotIndex,
            StackableItemCore.Create(moving.ItemKind, moving.ItemId, moveCount),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The two slots trade what they hold.</summary>
    public static async ValueTask SwapAsync(
        IItemContainer container,
        ContainerSlotItem first,
        ContainerSlotItem second,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(container);
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        await container.PutAsync(second.SlotIndex, first.Core, cancellationToken).ConfigureAwait(false);
        await container.PutAsync(first.SlotIndex, second.Core, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The pile is poured into another of the same item.</summary>
    public static async ValueTask MergeAsync(
        IItemContainer container,
        ContainerSlotItem moving,
        ContainerSlotItem target,
        int moveCount,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(container);
        ArgumentNullException.ThrowIfNull(moving);
        ArgumentNullException.ThrowIfNull(target);
        await container.PutAsync(
            target.SlotIndex,
            target.Core with { Value = checked(target.Count + moveCount) },
            cancellationToken).ConfigureAwait(false);

        if (moveCount == moving.Count)
        {
            await container.ClearAsync(moving.SlotIndex, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await container.PutAsync(
                moving.SlotIndex,
                moving.Core with { Value = moving.Count - moveCount },
                cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Takes some off the pile and answers what is left of it.</summary>
    /// <remarks>
    /// An emptied slot is cleared rather than left holding a pile of nothing, which the client
    /// would draw as occupied and the server would answer as empty.
    /// </remarks>
    public static async ValueTask<int> ConsumeAsync(
        IItemContainer container,
        ContainerSlotItem item,
        int count,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(container);
        ArgumentNullException.ThrowIfNull(item);
        if (count <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "A consumed count must be positive.");
        }

        var remaining = item.Count - count;
        if (remaining < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(count), count, "A pile cannot give up more than it holds.");
        }

        if (remaining == 0)
        {
            await container.ClearAsync(item.SlotIndex, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await container.PutAsync(
                item.SlotIndex, item.Core with { Value = remaining }, cancellationToken)
                .ConfigureAwait(false);
        }

        return remaining;
    }
}

/// <summary>
/// A container whose items are whatever one store holds under its identifier.
/// </summary>
/// <remarks>
/// The container knows nothing about where its items are kept. Both of the ones below are this
/// class with a different store behind them and a different answer to what happens when there is no
/// use case boundary open -- which is the only place persistence is still visible from here.
/// </remarks>
internal abstract class StoreBackedItemContainer(
    ContainerId id,
    ContainerShape shape,
    IItemStore store,
    IDomainTransactionAccessor transactions) : IItemContainer
{
    public ContainerId Id => id;

    public InventoryContainerType Type => shape.ContainerType;

    public ContainerShape Shape => shape;

    public abstract bool IsRemovable { get; }

    protected IItemStore Store => store;

    public async ValueTask<ContainerSlotItem?> ReadSlotAsync(
        int slotIndex, CancellationToken cancellationToken)
    {
        var stored = await this.ReadAsync(
            session => session.ReadAsync(id, slotIndex, cancellationToken)).ConfigureAwait(false);
        return ToSlotItem(stored);
    }

    public async ValueTask<ContainerSlotPair> ReadSlotsAsync(
        int firstSlotIndex, int secondSlotIndex, CancellationToken cancellationToken)
    {
        return await this.ReadAsync(async session => new ContainerSlotPair(
            ToSlotItem(await session.ReadAsync(id, firstSlotIndex, cancellationToken)
                .ConfigureAwait(false)),
            ToSlotItem(await session.ReadAsync(id, secondSlotIndex, cancellationToken)
                .ConfigureAwait(false)))).ConfigureAwait(false);
    }

    public abstract ValueTask<IReadOnlyList<InventoryItemState>> SnapshotAsync(
        CancellationToken cancellationToken);

    public async ValueTask<IReadOnlyList<ContainerSlotItem>> ReadAllAsync(
        CancellationToken cancellationToken)
    {
        var stored = await this.ReadAsync(
            session => session.ReadAllAsync(id, cancellationToken)).ConfigureAwait(false);
        return [.. stored.Select(item => ToSlotItem(item)!)];
    }

    public ValueTask PutAsync(
        int slotIndex, InventoryItemCoreState core, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(core);
        return this.WriteAsync(session => session.Write(id, new StoredItem(slotIndex, core)));
    }

    public ValueTask ClearAsync(int slotIndex, CancellationToken cancellationToken) =>
        this.WriteAsync(session => session.Clear(id, slotIndex));

    /// <summary>
    /// Reads through this use case's session when there is one, and through a session of its own
    /// when there is not.
    /// </summary>
    /// <remarks>
    /// A read outside a use case is allowed -- it is how a container is looked at without anything
    /// being changed -- and the session it opens is discarded rather than settled, because a read
    /// has nothing to settle.
    /// </remarks>
    private protected async ValueTask<TResult> ReadAsync<TResult>(
        Func<IItemStoreSession, ValueTask<TResult>> read)
    {
        if (transactions.Current is { } ambient)
        {
            return await read(store.SessionIn(ambient)).ConfigureAwait(false);
        }

        var alone = new DomainTransaction();
        try
        {
            return await read(store.SessionIn(alone)).ConfigureAwait(false);
        }
        finally
        {
            await alone.RollbackAsync().ConfigureAwait(false);
        }
    }

    /// <summary>What this container does with a change when no use case boundary is open.</summary>
    private protected abstract ValueTask WriteAsync(Action<IItemStoreSession> write);

    private protected IItemStoreSession? AmbientSession() =>
        transactions.Current is { } ambient ? store.SessionIn(ambient) : null;

    private protected static ContainerSlotItem? ToSlotItem(StoredItem? stored) =>
        stored is null ? null : new ContainerSlotItem(stored.SlotIndex, stored.Core);
}

/// <summary>
/// A container whose items live in the character's persistent storage.
/// </summary>
/// <remarks>
/// The character's own container, and therefore the one everything falls back to: it is the only
/// one whose contents outlive the session, so a character left bound to anything else would come
/// back to an inventory that no longer exists.
///
/// Changing it outside a use case boundary is refused. Such a change would be one nothing could
/// take back -- there is no transaction to unwind it and no failure for anyone to see -- which is
/// exactly the silent non-atomic write this whole boundary exists to make impossible.
/// </remarks>
internal sealed class PersistentItemContainer(
    ContainerId id,
    CharacterId characterId,
    ContainerShape shape,
    IItemStore store,
    IDomainTransactionAccessor transactions,
    IEntryInventoryMutationRepository snapshots)
    : StoreBackedItemContainer(id, shape, store, transactions)
{
    public override bool IsRemovable => false;

    /// <remarks>
    /// Taken from the loader rather than from the store, because the snapshot the client is shown
    /// carries facts the store's vocabulary does not name -- an avatar's details among them. What
    /// the entry snapshot is built from is not this refactor's subject.
    /// </remarks>
    public override ValueTask<IReadOnlyList<InventoryItemState>> SnapshotAsync(
        CancellationToken cancellationToken) =>
        snapshots.ReadMainItemsAsync(characterId, cancellationToken);

    private protected override ValueTask WriteAsync(Action<IItemStoreSession> write)
    {
        var session = this.AmbientSession()
            ?? throw new InvalidOperationException(
                $"Container '{this.Id.Value}' keeps its items past this process, so it is only "
                + "changed inside a use case. There is no transaction open on this flow to settle "
                + "the change, and one applied without a boundary could not be taken back.");
        write(session);
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// A container that holds nothing beyond this run.
/// </summary>
/// <remarks>
/// What a dungeon hands the player is this: an inventory the client draws exactly like the real
/// one, filled from what the run produced, and gone when the run is. Nothing about it reaches
/// storage, so leaving one behind loses only what was never meant to be kept.
///
/// It is built from a shape rather than from a character, so nothing in its own signature holds it
/// to one of them.
/// </remarks>
internal sealed class TransientItemContainer(
    ContainerId id,
    ContainerShape shape,
    IItemStore store,
    IDomainTransactionAccessor transactions)
    : StoreBackedItemContainer(id, shape, store, transactions)
{
    public override bool IsRemovable => true;

    public override async ValueTask<IReadOnlyList<InventoryItemState>> SnapshotAsync(
        CancellationToken cancellationToken)
    {
        var items = await this.ReadAllAsync(cancellationToken).ConfigureAwait(false);
        return [.. items.Select(item => new InventoryItemState(0, item.SlotIndex, item.Core))];
    }

    /// <remarks>
    /// A change outside a use case boundary is carried out at once rather than refused. There is
    /// nothing here that could half-happen: the store this writes to declares that its commit
    /// cannot fail, so a transaction of one is settled the moment it is filled.
    /// </remarks>
    private protected override async ValueTask WriteAsync(Action<IItemStoreSession> write)
    {
        if (this.AmbientSession() is { } ambient)
        {
            write(ambient);
            return;
        }

        var alone = new DomainTransaction();
        write(this.Store.SessionIn(alone));
        await alone.CommitAsync(CancellationToken.None).ConfigureAwait(false);
    }
}

internal sealed class ItemContainerFactory(
    MemoryItemStore store, IDomainTransactionAccessor transactions) : IItemContainerFactory
{
    public IItemContainer CreateTransient(ContainerId id, ContainerShape shape) =>
        new TransientItemContainer(id, shape, store, transactions);
}
