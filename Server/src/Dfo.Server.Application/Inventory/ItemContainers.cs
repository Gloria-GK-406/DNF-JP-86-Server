using Dfo.Server.Abstractions.Content;
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

/// <summary>How a decided move rearranges the two slots.</summary>
public enum ContainerMoveKind
{
    /// <summary>The whole stack goes to an empty slot.</summary>
    Relocate,

    /// <summary>Part of the stack goes to an empty slot; the rest stays behind.</summary>
    Split,

    /// <summary>The two slots trade places.</summary>
    Swap,

    /// <summary>The stack is poured into a pile of the same item.</summary>
    Merge,
}

/// <summary>
/// What the move was decided to be, before it is said as reads and writes.
/// </summary>
/// <remarks>
/// It names no item and no storage row, only where the move is going and how much of the pile goes.
/// The two items are the ones already read, which is why they do not need naming twice.
/// </remarks>
public sealed record DecidedMove(int TargetSlotIndex, int MoveCount, ContainerMoveKind Kind);

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
/// Nothing here decides whether an operation is allowed, and nothing here ever will. Whether the
/// item may occupy the slot, and whether a merge would burst the stack, are answered by the
/// container before any of this is called -- they used to be answered by the protocol handler, and
/// moving them did not change what this file is for.
///
/// That division is what this file demonstrates: a move is composed here out of clears and writes
/// without the store below ever being told what a move is. Composition never needed the store's
/// help, which is why bringing the judgement down into the container cost the store nothing.
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
    IDomainTransactionAccessor transactions,
    IItemPlacementRuleService placementRules,
    IItemArrivalRuleService arrivals,
    IItemSlotRuleService slotRules) : IItemContainer
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

    public async ValueTask<ItemPlacementOutcome> AddAsync(
        int itemId, int count, CancellationToken cancellationToken)
    {
        var placement = await placementRules.PlacementForAsync(itemId, cancellationToken)
            .ConfigureAwait(false);
        if (placement is null)
        {
            return new ItemPlacementOutcome.UnknownItem();
        }

        var held = await this.ReadAllAsync(cancellationToken).ConfigureAwait(false);
        var arrival = arrivals.ForGrant(
            PilesOf(held, itemId), count, placement, OccupiedSlots(held));
        return await this.LandAsync(
            arrival, held, itemId, placement.ItemKind, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<ItemPlacementOutcome> RefillAsync(
        ItemRefill refill, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(refill);
        var held = await this.ReadAllAsync(cancellationToken).ConfigureAwait(false);
        var arrival = arrivals.ForDailyRefill(
            PilesOf(held, refill.ItemId),
            refill.Count,
            refill.SlotStart,
            refill.SlotEnd,
            OccupiedSlots(held));
        return await this.LandAsync(
            arrival, held, refill.ItemId, refill.ItemKind, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Turns a decided arrival into the write it calls for.
    /// </summary>
    /// <remarks>
    /// The one place that does. Both verbs above reach it, and before they existed each of the two
    /// callers had written this switch out for itself -- which is how the two came to disagree
    /// about nothing in particular for as long as nobody compared them.
    ///
    /// Opening a slot needs a kind, because a kind is part of what is written; a refill whose rule
    /// declares none is refused here rather than stored, since an item kind of zero is a row the
    /// entry path cannot read back. Joining a pile needs no kind: the pile already carries one.
    /// </remarks>
    private async ValueTask<ItemPlacementOutcome> LandAsync(
        ItemArrival arrival,
        IReadOnlyList<ContainerSlotItem> held,
        int itemId,
        byte itemKind,
        CancellationToken cancellationToken)
    {
        switch (arrival)
        {
            case ItemArrival.OntoExistingPile topped:
                var pile = held.Single(item => item.SlotIndex == topped.SlotIndex);
                await this.PutAsync(
                    topped.SlotIndex,
                    pile.Core with { Value = topped.Total },
                    cancellationToken).ConfigureAwait(false);
                await this.ApplyDecidedWritesAsync(cancellationToken).ConfigureAwait(false);
                return new ItemPlacementOutcome.AddedToPile(
                    topped.SlotIndex, topped.Added, topped.Total);

            case ItemArrival.IntoEmptySlot opened:
                if (itemKind == 0)
                {
                    throw new InvalidDataException(
                        $"Item {itemId} has no authoritative PVF item kind, so no slot can be "
                            + "opened for it.");
                }

                await this.PutAsync(
                    opened.SlotIndex,
                    StackableItemCore.Create(itemKind, itemId, opened.Count),
                    cancellationToken).ConfigureAwait(false);
                await this.ApplyDecidedWritesAsync(cancellationToken).ConfigureAwait(false);
                return new ItemPlacementOutcome.PutInEmptySlot(opened.SlotIndex, opened.Count);

            case ItemArrival.NoRoom:
                return new ItemPlacementOutcome.NoRoom();

            default:
                // Listed rather than defaulted: an arrival nobody landed would silently become
                // "nothing happened", and the caller that reports what it placed would say
                // truthfully that it placed nothing while the player was owed something.
                throw new ArgumentOutOfRangeException(
                    nameof(arrival), arrival, "No landing was decided for this arrival.");
        }
    }

    public async ValueTask<ItemMoveOutcome> MoveToAsync(
        int fromSlotIndex, int toSlotIndex, int count, CancellationToken cancellationToken)
    {
        // Naming one slot twice is not a move. A client settling a drag that ended where it started
        // sends exactly this and expects to be told it worked.
        if (fromSlotIndex == toSlotIndex)
        {
            return new ItemMoveOutcome.Unchanged();
        }

        var slots = await this.ReadSlotsAsync(fromSlotIndex, toSlotIndex, cancellationToken)
            .ConfigureAwait(false);
        var (moving, displaced, targetSlot) = slots.First is not null
            ? (slots.First, slots.Second, toSlotIndex)
            : (slots.Second, null, fromSlotIndex);

        if (moving is null)
        {
            return new ItemMoveOutcome.Refused(ItemMoveRefusal.BothSlotsEmpty);
        }

        var decided = await this.DecideMoveAsync(
            moving, displaced, targetSlot, count, cancellationToken).ConfigureAwait(false);
        if (decided.Refusal is { } refusal)
        {
            return new ItemMoveOutcome.Refused(refusal);
        }

        var plan = decided.Plan!;
        switch (plan.Kind)
        {
            case ContainerMoveKind.Relocate:
                await ItemContainerOperations
                    .RelocateAsync(this, moving, plan.TargetSlotIndex, cancellationToken)
                    .ConfigureAwait(false);
                break;
            case ContainerMoveKind.Split:
                await ItemContainerOperations
                    .SplitAsync(this, moving, plan.TargetSlotIndex, plan.MoveCount, cancellationToken)
                    .ConfigureAwait(false);
                break;
            case ContainerMoveKind.Swap:
                await ItemContainerOperations
                    .SwapAsync(this, moving, displaced!, cancellationToken)
                    .ConfigureAwait(false);
                break;
            case ContainerMoveKind.Merge:
                await ItemContainerOperations
                    .MergeAsync(this, moving, displaced!, plan.MoveCount, cancellationToken)
                    .ConfigureAwait(false);
                break;
            default:
                // Listed rather than defaulted: a kind nobody carried out would answer that the
                // move succeeded while the inventory stayed exactly as it was.
                throw new ArgumentOutOfRangeException(
                    nameof(plan), plan.Kind, "No move was carried out for this kind.");
        }

        await this.ApplyDecidedWritesAsync(cancellationToken).ConfigureAwait(false);
        return new ItemMoveOutcome.Moved(plan.MoveCount, moving.ItemId);
    }

    /// <summary>
    /// Decides what the move does, or which refusal applies.
    /// </summary>
    /// <remarks>
    /// The refusals are told apart rather than folded into one, because a caller with sentences to
    /// show a player has a different one for a full stack than for an item that cannot go where it
    /// was dropped.
    ///
    /// The band consulted is the main list's, which is the only list this server serves. A second
    /// served list is a second band, and it belongs in the rule service beside this one rather than
    /// as a branch here.
    /// </remarks>
    private async ValueTask<(DecidedMove? Plan, ItemMoveRefusal? Refusal)> DecideMoveAsync(
        ContainerSlotItem moving,
        ContainerSlotItem? displaced,
        int targetSlot,
        int requestedCount,
        CancellationToken cancellationToken)
    {
        var placement = await placementRules.PlacementForAsync(moving.ItemId, cancellationToken)
            .ConfigureAwait(false);
        if (placement is null || !slotRules.CanOccupyMainSlot(placement, targetSlot))
        {
            return (null, ItemMoveRefusal.ItemCannotOccupySlot);
        }

        var moveCount = requestedCount <= 0 || requestedCount > moving.Count
            ? moving.Count
            : requestedCount;

        if (displaced is null)
        {
            return (new DecidedMove(
                    targetSlot,
                    moveCount,
                    moveCount == moving.Count
                        ? ContainerMoveKind.Relocate
                        : ContainerMoveKind.Split),
                null);
        }

        // Two piles of the same item merge; anything else trades places. A merge that would burst
        // the stack limit is refused rather than silently truncated, so the client keeps drawing
        // the counts this container holds.
        if (displaced.ItemId == moving.ItemId)
        {
            return placement.StackLimit > 1 && displaced.Count + moveCount <= placement.StackLimit
                ? (new DecidedMove(targetSlot, moveCount, ContainerMoveKind.Merge), null)
                : (null, ItemMoveRefusal.MergeExceedsStackLimit);
        }

        // A swap has to satisfy both ends: the displaced pile is going to the slot this one is
        // leaving, and it has a band of its own.
        if (moveCount != moving.Count)
        {
            return (null, ItemMoveRefusal.PartialSwapNotAllowed);
        }

        var displacedPlacement = await placementRules
            .PlacementForAsync(displaced.ItemId, cancellationToken)
            .ConfigureAwait(false);
        return displacedPlacement is null
            || !slotRules.CanOccupyMainSlot(displacedPlacement, moving.SlotIndex)
            ? (null, ItemMoveRefusal.DisplacedCannotOccupySlot)
            : (new DecidedMove(targetSlot, moveCount, ContainerMoveKind.Swap), null);
    }

    private static IReadOnlyList<ItemPile> PilesOf(
        IReadOnlyList<ContainerSlotItem> held, int itemId) =>
        [.. held.Where(item => item.ItemId == itemId)
            .Select(item => new ItemPile(item.SlotIndex, item.Count))];

    private static IReadOnlySet<int> OccupiedSlots(IReadOnlyList<ContainerSlotItem> held) =>
        held.Select(item => item.SlotIndex).ToHashSet();

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

    /// <summary>
    /// Makes what a verb has just decided readable by everyone inside this use case, not only by
    /// this container.
    /// </summary>
    /// <remarks>
    /// A store holds its writes until the use case settles. That is right for a caller who reads
    /// back through the container, and wrong for everything else in the same use case -- the entry
    /// loader that reads the same rows through the tables, and this container's own snapshot, which
    /// is taken from that loader because the client is shown facts the store's vocabulary does not
    /// name. Before the verbs existed, the one caller that wrote and then read this way wrote
    /// through the tables and so never met the difference; the verbs would have handed it the
    /// inventory as it stood before.
    ///
    /// Done once per verb rather than once per write, so a move is still one batch. Nothing outside
    /// the use case sees anything sooner, and a rollback still takes it all back.
    /// </remarks>
    private async ValueTask ApplyDecidedWritesAsync(CancellationToken cancellationToken)
    {
        if (this.AmbientSession() is { } session)
        {
            await session.ApplyDecidedWritesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

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
    IEntryInventoryMutationRepository snapshots,
    IItemPlacementRuleService placementRules,
    IItemArrivalRuleService arrivals,
    IItemSlotRuleService slotRules)
    : StoreBackedItemContainer(id, shape, store, transactions, placementRules, arrivals, slotRules)
{
    public override bool IsRemovable => false;

    /// <remarks>
    /// Taken from the loader rather than from the store, because the snapshot the client is shown
    /// carries facts the store's vocabulary does not name -- an avatar's details among them. What
    /// the entry snapshot is built from is not this refactor's subject.
    /// <para>
    /// Only the main list has such a loader. The equipment container is bound so that a piece can
    /// be put on and taken off, and what the client is shown of it comes from the entry projection
    /// rather than from here. Refused rather than answered out of the store, because a snapshot
    /// assembled without those facts is one with an avatar quietly stripped of its colours -- and a
    /// caller would have no way to tell that from a container that simply holds none.
    /// </para>
    /// </remarks>
    public override ValueTask<IReadOnlyList<InventoryItemState>> SnapshotAsync(
        CancellationToken cancellationToken) =>
        this.Type == InventoryContainerType.Main
            ? snapshots.ReadMainItemsAsync(characterId, cancellationToken)
            : throw new NotSupportedException(
                $"Container '{this.Id.Value}' has no snapshot source; only the main list does.");

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
