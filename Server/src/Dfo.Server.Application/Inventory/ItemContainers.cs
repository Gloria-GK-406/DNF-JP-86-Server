using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Inventory;

/// <summary>
/// What an avatar has to be given at the moment the world first takes it.
/// </summary>
/// <remarks>
/// An avatar is not an avatar until it carries two things beyond its kind: an identity of its own,
/// and the six facts a row of that kind holds. Both are demanded of a standing avatar in three
/// places -- the entry load will not serve one whose identity is not above zero or whose facts are
/// missing or whose jewel socket is not exactly thirty bytes, the entry save refuses to write one
/// back without them, and the two projections the client is served refuse to encode one. A delivery
/// that opened a slot for an avatar and gave it neither would put down a row nothing in this server
/// can pick up again, and it would do so silently, because the write itself has nothing to check.
/// So the shape is made here, once, on the one path that opens a slot.
/// </remarks>
internal static class ArrivingAvatar
{
    /// <summary>The kind byte an avatar carries, which is what storage judges one by.</summary>
    /// <remarks>
    /// The same 8 the archive now gives an avatar template, and the same 8 the load, the save and
    /// both projections look for. It is compared against here rather than the world class, because
    /// the kind is what every one of those readers uses and a second question would be a second
    /// answer to keep in step.
    /// </remarks>
    internal const byte ItemKind = 8;

    /// <summary>How many bytes an avatar's jewel socket is, exactly.</summary>
    private const int JewelSocketLength = 30;

    /// <summary>
    /// What an avatar's expiry is set to when nothing has said it expires.
    /// </summary>
    /// <remarks>
    /// Zero is a present avatar fact with no expiry; SQL NULL still means no avatar facts.
    /// A21's zero remaining-time value displays unlimited. The former int.MaxValue
    /// placeholder is recognized at the wire boundary for existing stored items.
    /// See Document/Avatar-Permanent-20260909.md for the client comparison branches.
    /// </remarks>
    private const int NeverExpires = 0;

    /// <summary>
    /// The identity a newly delivered avatar is known by, which is above zero and its own.
    /// </summary>
    /// <remarks>
    /// Drawn rather than sequenced, which is the same reading
    /// <c>CharacterRosterService.EquipmentCore</c> took for the instance value it gives a freshly
    /// created piece: what the number must be is distinct, and a sequence would need a row of its
    /// own to stay distinct across restarts.
    /// <para>
    /// Above zero because three separate places refuse an avatar whose identity is not, and
    /// <see cref="Random.Shared"/> drawing from one rather than from zero is what keeps them
    /// unreachable rather than merely unlikely.
    /// </para>
    /// </remarks>
    internal static int NewIdentity() => Random.Shared.Next(1, int.MaxValue);

    /// <summary>
    /// The six facts an avatar arrives carrying, none of them yet anything but a default.
    /// </summary>
    /// <remarks>
    /// A fresh avatar has had no jewel set in it, no colour dyed on it, clears nothing and is not
    /// scheduled for deletion, so five of the six are the zero that says exactly that. The socket
    /// is thirty bytes because the column and the load both refuse any other length, and a fresh
    /// one is thirty zeroes rather than nothing: null would be "this row carries no facts", which
    /// is the one thing an avatar may not say.
    /// </remarks>
    internal static InventoryAvatarDetailState Facts() => new(
        ExpireDate: NeverExpires,
        ClearAvatarId: 0,
        JewelSocket: new byte[JewelSocketLength],
        Color1: 0,
        Color2: 0,
        DeleteDate: 0);
}

/// <summary>Composes consumption from local container reads and writes.</summary>
public static class ItemContainerOperations
{
    /// <summary>Takes some off the pile and answers what is left of it.</summary>
    /// <remarks>
    /// An emptied slot is cleared rather than left holding a pile of nothing, which the client
    /// would draw as occupied and the server would answer as empty.
    /// </remarks>
    public static async ValueTask<int> ConsumeAsync(
        IBusinessItemContainer container,
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
    IItemDomainStore store,
    IDomainTransactionAccessor transactions,
    IItemPlacementRuleService placementRules,
    IItemArrivalRuleService arrivals,
    IItemSlotRuleService slotRules, IInventoryCapacityDomainService? capacities = null) : IItemContainer
{
    public ValueTask FlushAsync(CancellationToken cancellationToken) =>
        this.ApplyDecidedWritesAsync(cancellationToken);
    public ContainerId Id => id;

    public InventoryContainerType Type => shape.ContainerType;

    public ContainerShape Shape => shape;

    public abstract bool IsRemovable { get; }

    protected IItemDomainStore Store => store;

    public async ValueTask<ContainerSlotItem?> ReadSlotAsync(
        int slotIndex, CancellationToken cancellationToken)
    {
        var stored = await this.ReadAsync(
            session => session.ReadAsync(id, slotIndex, cancellationToken)).ConfigureAwait(false);
        return await this.ToSlotItemAsync(stored, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<ContainerSlotPair> ReadSlotsAsync(
        int firstSlotIndex, int secondSlotIndex, CancellationToken cancellationToken)
    {
        var pair = await this.ReadAsync(async session => (
            First: await session.ReadAsync(id, firstSlotIndex, cancellationToken)
                .ConfigureAwait(false),
            Second: await session.ReadAsync(id, secondSlotIndex, cancellationToken)
                .ConfigureAwait(false))).ConfigureAwait(false);
        return new ContainerSlotPair(
            await this.ToSlotItemAsync(pair.First, cancellationToken).ConfigureAwait(false),
            await this.ToSlotItemAsync(pair.Second, cancellationToken).ConfigureAwait(false));
    }

    public abstract ValueTask<IReadOnlyList<InventoryItemState>> SnapshotAsync(
        CancellationToken cancellationToken);

    public async ValueTask<IReadOnlyList<ContainerSlotItem>> ReadAllAsync(
        CancellationToken cancellationToken)
    {
        var stored = await this.ReadAsync(
            session => session.ReadAllAsync(id, cancellationToken)).ConfigureAwait(false);
        var read = new List<ContainerSlotItem>(stored.Count);
        foreach (var item in stored)
        {
            read.Add((await this.ToSlotItemAsync(item, cancellationToken).ConfigureAwait(false))!);
        }

        return read;
    }

    /// <summary>
    /// The one write every container verb is made of, and therefore where a thing is classified.
    /// </summary>
    /// <remarks>
    /// Relocating, splitting, swapping, merging, consuming, landing an arrival and the card
    /// reward's direct put are all said as calls to this, so asking the archive here is asking it
    /// on every one of them. The class is asked of the template rather than read off the core,
    /// because a caller is free to hand over a core whose kind byte says something the archive
    /// does not, and the point of the classification is that the archive wins.
    /// <para>
    /// Nothing is written twice: the avatar-carrying write below is this same call with the six
    /// facts filled in, so there is one place a container writes an item and one place a container
    /// classifies one.
    /// </para>
    /// </remarks>
    public ValueTask PutAsync(
        int slotIndex, InventoryItemCoreState core, CancellationToken cancellationToken) =>
        this.WriteOneAsync(slotIndex, core, null, cancellationToken);

    public ValueTask PutTransferredAsync(int slotIndex, InventoryItemCoreState core,
        ContainerId sourceContainer, int sourceSlotIndex, CancellationToken cancellationToken) =>
        this.WriteOneAsync(slotIndex, core, null, cancellationToken,
            transferFrom: new ItemTransferSource(sourceContainer, sourceSlotIndex));

    public ValueTask PutNewAsync(int slotIndex, InventoryItemCoreState core, InventoryItemCreationFacts facts,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(facts);
        return this.WriteOneAsync(slotIndex, core, facts.Avatar, cancellationToken, facts.Creature);
    }

    /// <summary>The write itself, with whatever an avatar carries into the world on it.</summary>
    /// <remarks>
    /// The facts are null for everything that is not an avatar entering the world, which is every
    /// caller of <see cref="PutAsync"/>: a move, a split, a merge and a consume are all an item
    /// the world already holds, and settlement hands such an item its own six back rather than
    /// taking them from the write.
    /// </remarks>
    private async ValueTask WriteOneAsync(
        int slotIndex,
        InventoryItemCoreState core,
        InventoryAvatarDetailState? avatarDetail,
        CancellationToken cancellationToken,
        InventoryCreatureDetailState? creatureDetail = null,
        ItemTransferSource? transferFrom = null)
    {
        ArgumentNullException.ThrowIfNull(core);
        if (Type == InventoryContainerType.SharedMaterial || AccountMaterialCatalog.ContainsItem(core.ItemId))
        {
            var destination = LegacyInventoryStorageCoordinates.ToBusiness(new(Type, slotIndex), core.ItemKind);
            var placement = await placementRules.PlacementForAsync(core.ItemId, destination.Space,
                cancellationToken).ConfigureAwait(false);
            if (Type == InventoryContainerType.SharedMaterial
                    && (!id.TryGetAccountOwner(out _, out var type) || type != Type)
                || core.Value <= 0
                || placement is null || core.ItemKind != placement.ItemKind
                || placement.StackLimit > 0 && core.Value > placement.StackLimit
                || !slotRules.CanOccupySlot(destination, placement))
                throw new InvalidOperationException($"Item {core.ItemId} cannot occupy container {id}/{slotIndex}.");
        }
        // Main 0..2 are virtual currency counters, not expandable item slots.
        // Their grant effects validate balances and overflow before this write.
        if (capacities is not null && !(Type == InventoryContainerType.Main && slotIndex is >= 0 and <= 2))
        {
            var business = LegacyInventoryStorageCoordinates.ToBusiness(new(Type, slotIndex), core.ItemKind);
            var open = await capacities.OpenSlotCountAsync(id, business.Space, cancellationToken).ConfigureAwait(false);
            if (business.Index >= open)
            {
                var existing = await ReadSlotAsync(slotIndex, cancellationToken).ConfigureAwait(false);
                if (!(existing is { IsPile: true } && core.Value > 0 && core.Value < existing.Count
                    && core with { Value = 0 } == existing.Core with { Value = 0 }))
                    throw new InvalidOperationException($"Slot {business} is not open.");
            }
        }
        var worldClass = await placementRules.ClassifyAsync(core.ItemId, cancellationToken)
            .ConfigureAwait(false);
        if (avatarDetail is not null && (worldClass != ItemWorldClass.Avatar || core.ItemKind != 8)
            || creatureDetail is not null && (worldClass != ItemWorldClass.Creature || core.ItemKind != 5))
            throw new InvalidDataException("Creation facts do not match the item's declared kind.");
        await this.WriteAsync(session => session.Write(
                id,
                new StoredItem(slotIndex, core)
                {
                    WorldClass = worldClass,
                    AvatarDetail = avatarDetail,
                    CreatureDetail = creatureDetail,
                    TransferFrom = transferFrom,
                }))
            .ConfigureAwait(false);
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

        if (capacities is not null)
        {
            placement = InventoryCapacityPlacement.Limit(placement,
                await capacities.OpenSlotCountAsync(id, placement.Destination.Space, cancellationToken).ConfigureAwait(false));
            if (placement is null) return new ItemPlacementOutcome.NoRoom();
        }
        var held = await this.ReadAllAsync(cancellationToken).ConfigureAwait(false);
        var arrival = arrivals.ForGrant(
            this.PilesOf(held, itemId, Candidates(placement)), count, placement,
            this.OccupiedSlots(held, Candidates(placement)));
        if (arrival is ItemArrival.IntoEmptySlot opening
            && !slotRules.CanOccupySlot(opening.Slot, placement))
            throw new InvalidDataException($"Arrival chose a slot {opening.Slot} that rejects item {itemId}.");
        return await this.LandAsync(
            arrival, held, itemId, placement.ItemKind, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<ItemPlacementOutcome> RefillAsync(
        ItemRefill refill, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(refill);
        var held = await this.ReadAllAsync(cancellationToken).ConfigureAwait(false);
        if (capacities is not null)
        {
            var open = await capacities.OpenSlotCountAsync(id, refill.Destination.Space, cancellationToken).ConfigureAwait(false);
            if (open <= refill.Destination.FirstIndex) return new ItemPlacementOutcome.NoRoom();
            refill = refill with { Destination = new(refill.Destination.Space, refill.Destination.FirstIndex,
                Math.Min(refill.Destination.LastIndex, open - 1)) };
        }
        var candidates = SlotsIn(refill.Destination);
        var arrival = arrivals.ForDailyRefill(
            this.PilesOf(held, refill.ItemId, candidates),
            refill.Count,
            refill.Destination,
            this.OccupiedSlots(held, candidates));
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
    ///
    /// Opening a slot for an avatar needs more than a kind, and this is where it is given: an
    /// avatar row that carries no identity of its own and no facts is a row the load refuses and
    /// the two projections refuse, so an avatar delivered without them would be a thing the server
    /// could put down and then never pick up again. The shape is <see cref="ArrivingAvatar"/>'s;
    /// what is here is only the one branch that asks for it. Joining a pile cannot reach it,
    /// because an avatar is never a pile.
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
                var toppedIndex = this.StorageIndex(topped.Slot);
                var pile = held.Single(item => item.SlotIndex == toppedIndex);
                await this.PutAsync(
                    toppedIndex,
                    pile.Core with { Value = topped.Total },
                    cancellationToken).ConfigureAwait(false);
                await this.ApplyDecidedWritesAsync(cancellationToken).ConfigureAwait(false);
                return new ItemPlacementOutcome.AddedToPile(
                    toppedIndex, topped.Added, topped.Total);

            case ItemArrival.IntoEmptySlot opened:
                if (itemKind == 0)
                {
                    throw new InvalidDataException(
                        $"Item {itemId} has no authoritative PVF item kind, so no slot can be "
                            + "opened for it.");
                }

                var openedIndex = this.StorageIndex(opened.Slot);
                var landing = StackableItemCore.Create(itemKind, itemId, opened.Count);
                var isAvatar = itemKind == ArrivingAvatar.ItemKind;
                await this.WriteOneAsync(
                    openedIndex,
                    isAvatar ? landing with { Value = ArrivingAvatar.NewIdentity() } : landing,
                    isAvatar ? ArrivingAvatar.Facts() : null,
                    cancellationToken).ConfigureAwait(false);
                await this.ApplyDecidedWritesAsync(cancellationToken).ConfigureAwait(false);
                return new ItemPlacementOutcome.PutInEmptySlot(openedIndex, opened.Count);

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

    private IReadOnlyList<ItemPile> PilesOf(
        IReadOnlyList<ContainerSlotItem> held,
        int itemId,
        IEnumerable<BusinessSlot> candidates)
    {
        var byIndex = held.ToDictionary(item => item.SlotIndex);
        return [.. candidates
            .Select(slot => (Slot: slot, Index: this.StorageIndex(slot)))
            .Where(candidate => byIndex.TryGetValue(candidate.Index, out var item)
                && item.ItemId == itemId)
            .Select(candidate => new ItemPile(candidate.Slot, byIndex[candidate.Index].Count))];
    }

    private IReadOnlySet<BusinessSlot> OccupiedSlots(
        IReadOnlyList<ContainerSlotItem> held,
        IEnumerable<BusinessSlot> candidates)
    {
        var occupied = held.Select(item => item.SlotIndex).ToHashSet();
        return candidates.Where(slot => occupied.Contains(this.StorageIndex(slot))).ToHashSet();
    }

    private static IEnumerable<BusinessSlot> Candidates(ItemPlacement placement)
    {
        var own = SlotsIn(placement.Destination);
        return placement.Destination.Space != BusinessInventorySpace.SharedMaterial && ItemSlotRuleService.AllowsQuickSlot(placement)
            ? own.Concat(SlotsIn(new(BusinessInventorySpace.QuickSlot, 0, 5)))
            : own;
    }

    private static IEnumerable<BusinessSlot> SlotsIn(BusinessSlotRange range) =>
        Enumerable.Range(range.FirstIndex, range.LastIndex - range.FirstIndex + 1)
            .Select(index => new BusinessSlot(range.Space, index));

    private int StorageIndex(BusinessSlot slot)
    {
        var storage = LegacyInventoryStorageCoordinates.ToStorage(slot);
        if (storage.List != this.Type)
            throw new InvalidDataException(
                $"Business slot {slot.Space}/{slot.Index} does not belong to container {this.Type}.");
        return storage.Index;
    }

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
        Func<IItemDomainStoreSession, ValueTask<TResult>> read)
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
    private protected abstract ValueTask WriteAsync(Action<IItemDomainStoreSession> write);

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

    private protected IItemDomainStoreSession? AmbientSession() =>
        transactions.Current is { } ambient ? store.SessionIn(ambient) : null;

    /// <summary>
    /// What a slot holds, said the same way whichever of the two kinds it is.
    /// </summary>
    /// <remarks>
    /// The sort of thing it is comes from the archive, out of the one place that classifies -- the
    /// same question and the same answer a write of this slot would get. Reading it off the kind
    /// byte the core carries would be the reading the classification exists to replace, and a
    /// caller would then be told one thing on the way out and another on the way in.
    ///
    /// Identity is a read-only address; writing still lets the store preserve its own row.
    /// </remarks>
    private protected async ValueTask<ContainerSlotItem?> ToSlotItemAsync(
        StoredItem? stored, CancellationToken cancellationToken)
    {
        if (stored is null) return null;
        var worldClass = await placementRules.ClassifyAsync(stored.Core.ItemId, cancellationToken)
            .ConfigureAwait(false);
        return new(stored.SlotIndex, stored.Core, worldClass)
        {
            Identity = worldClass == ItemWorldClass.Interchangeable ? null : stored.Identity,
        };
    }
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
    IItemDomainStore store,
    IDomainTransactionAccessor transactions,
    IEntryInventoryMutationRepository? snapshots,
    IItemPlacementRuleService placementRules,
    IItemArrivalRuleService arrivals,
    IItemSlotRuleService slotRules, IInventoryCapacityDomainService? capacities = null)
    : StoreBackedItemContainer(
        id, shape, store, transactions, placementRules, arrivals, slotRules, capacities)
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
        this.Type == InventoryContainerType.Main && snapshots is not null
            ? snapshots.ReadMainItemsAsync(characterId, cancellationToken)
            : throw new NotSupportedException(
                $"Container '{this.Id.Value}' has no snapshot source; only the main list does.");

    private protected override ValueTask WriteAsync(Action<IItemDomainStoreSession> write)
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
