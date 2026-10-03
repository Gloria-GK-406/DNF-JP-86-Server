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

/// <summary>Consumption ends the custody of an explicitly detached item.</summary>
public static class ItemContainerOperations
{
    public static async ValueTask<int> ConsumeAsync(IBusinessItemContainer container,
        ItemReference item, int count, IItemLifecycleDomainService lifecycle, CancellationToken ct)
    {
        if (count <= 0 || count > item.Count) throw new ArgumentOutOfRangeException(nameof(count));
        var record = await container.TakeAsync(item, count, ct).ConfigureAwait(false);
        await lifecycle.DeleteItemAsync(record, ct).ConfigureAwait(false);
        return (await container.GetAsync(item.SlotIndex, ct).ConfigureAwait(false))?.Count ?? 0;
    }
}

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

    public ValueTask<ItemReference?> GetAsync(int index, CancellationToken ct) =>
        ReadAsync(session => session.GetAsync(id, index, ct));

    public ValueTask<IReadOnlyList<ItemReference>> GetAllAsync(CancellationToken ct) =>
        ReadAsync(session => session.GetAllAsync(id, ct));

    public abstract ValueTask<IReadOnlyList<InventoryItemState>> SnapshotAsync(CancellationToken ct);

    private IItemDomainStoreSession RequireSession() => AmbientSession()
        ?? throw new InvalidOperationException("Item lifecycle changes require an open domain transaction.");

    private void CheckReference(ItemReference reference)
    {
        if (reference.ContainerId != id) throw new InvalidOperationException("Reference belongs to another container.");
    }

    public ValueTask<ItemRecord> TakeAsync(ItemReference reference, int? count, CancellationToken ct)
    {
        CheckReference(reference);
        return RequireSession().TakeAsync(reference, count, ct);
    }

    public async ValueTask<ItemReference> UpdateAsync(ItemReference reference,
        Func<ItemSnapshot, ItemSnapshot> change, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(change);
        CheckReference(reference);
        var session = RequireSession();
        var current = await session.GetAsync(id, reference.StorageSlotIndex, ct).ConfigureAwait(false);
        if (current is null || !reference.SameOccupancyAs(current))
            throw new InvalidOperationException("The item reference no longer identifies this occupancy.");
        var before = current.Snapshot;
        var next = change(before.DeepCopy()).DeepCopy();
        var virtualSlot = Type == InventoryContainerType.Main && reference.StorageSlotIndex is >= 0 and <= 2;
        if (capacities is not null && !virtualSlot)
        {
            var business = LegacyInventoryStorageCoordinates.ToBusiness(new(Type, reference.StorageSlotIndex), before.LegacyKind);
            if (business.Index >= await capacities.OpenSlotCountAsync(id, business.Space, ct).ConfigureAwait(false)
                && !(next.Count > 0 && next.Count < before.Count && before.CanStackWith(next)))
                throw new InvalidOperationException($"Slot {business} is not open.");
        }
        if (next.ItemId != before.ItemId || next.Count > before.Count
            && (Type == InventoryContainerType.SharedMaterial || AccountMaterialCatalog.ContainsItem(next.ItemId)))
            await ValidatePlacementAsync(reference.StorageSlotIndex, next, next.Count, ct).ConfigureAwait(false);
        return await session.UpdateAsync(reference, _ => next, ct).ConfigureAwait(false);
    }

    public async ValueTask<ItemReference> PutAsync(int index, ItemRecord record, CancellationToken ct)
    {
        var session = RequireSession();
        var occupied = await session.GetAsync(id, index, ct).ConfigureAwait(false);
        await ValidatePlacementAsync(index, record.Snapshot, (long)record.Count + (occupied?.Count ?? 0), ct)
            .ConfigureAwait(false);
        return await session.PutAsync(id, index, record, ct).ConfigureAwait(false);
    }

    private async ValueTask ValidatePlacementAsync(int index, ItemSnapshot snapshot, long total, CancellationToken ct,
        bool enforceStackLimit = true)
    {
        if (Type == InventoryContainerType.Main && index == 1
            && snapshot is { LegacyKind: 0, ItemId: 1, Type: ItemTraits.None, IsPile: true }) return;
        var business = LegacyInventoryStorageCoordinates.ToBusiness(new(Type, index), snapshot.LegacyKind);
        if (capacities is not null && business.Index >= await capacities.OpenSlotCountAsync(id, business.Space, ct).ConfigureAwait(false))
            throw new InvalidOperationException($"Slot {business} is not open.");
        var placement = await placementRules.PlacementForAsync(snapshot.ItemId, business.Space, ct).ConfigureAwait(false);
        var worn = business.Space is BusinessInventorySpace.WornEquipment or BusinessInventorySpace.WornEquipmentExtension
            or BusinessInventorySpace.WornAvatar or BusinessInventorySpace.WornCreature or BusinessInventorySpace.WornCreatureEquipment;
        var admitsSlot = placement is not null && (worn
            ? ItemContainerAdmission.Accepts(ItemContainerAdmission.AcceptedBy(business.Space), snapshot.Type)
                && await placementRules.WornSlotForAsync(snapshot.ItemId, ct).ConfigureAwait(false) == business
            : slotRules.CanOccupySlot(business, placement));
        if (Type == InventoryContainerType.SharedMaterial && (!id.TryGetAccountOwner(out _, out var type) || type != Type)
            || placement is null || snapshot.LegacyKind != placement.ItemKind || snapshot.Type != placement.Traits
            || snapshot.IsPile != (placement.WorldClass == ItemWorldClass.Interchangeable)
            || enforceStackLimit && snapshot.IsPile && placement.StackLimit > 0 && total > placement.StackLimit
            || !admitsSlot)
            throw new InvalidOperationException($"Item {snapshot.ItemId} cannot occupy {id}/{index}.");
    }

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
        var held = await this.GetAllAsync(cancellationToken).ConfigureAwait(false);
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
        var held = await this.GetAllAsync(cancellationToken).ConfigureAwait(false);
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
            arrival, held, refill.ItemId, refill.ItemKind, cancellationToken, isRefill: true).ConfigureAwait(false);
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
        IReadOnlyList<ItemReference> held,
        int itemId,
        byte itemKind,
        CancellationToken cancellationToken, bool isRefill = false)
    {
        switch (arrival)
        {
            case ItemArrival.OntoExistingPile topped:
                var toppedIndex = this.StorageIndex(topped.Slot);
                var pile = held.Single(item => item.SlotIndex == toppedIndex);
                await this.UpdateAsync(pile, current => current with { Count = topped.Total },
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
                var classification = await placementRules.ClassifyAsync(itemId, cancellationToken).ConfigureAwait(false);
                var isPile = classification == ItemWorldClass.Interchangeable;
                var snapshot = new ItemSnapshot
                {
                    ItemId = itemId, Count = isPile ? opened.Count : 1, IsPile = isPile,
                    Type = ItemTraitClassification.For(itemKind, itemId), LegacyKind = itemKind,
                    InstanceValue = isPile ? 0 : ArrivingAvatar.NewIdentity(),
                    AvatarDetail = classification == ItemWorldClass.Avatar ? ArrivingAvatar.Facts() : null,
                };
                var record = await RequireSession().CreateAsync(snapshot, cancellationToken).ConfigureAwait(false);
                if (isRefill && Type != InventoryContainerType.SharedMaterial && !AccountMaterialCatalog.ContainsItem(itemId))
                {
                    // Refill quantities are bounded by the refill rule, independently of ordinary grants.
                    await ValidatePlacementAsync(openedIndex, record.Snapshot, record.Count, cancellationToken,
                        enforceStackLimit: false).ConfigureAwait(false);
                    await RequireSession().PutAsync(id, openedIndex, record, cancellationToken).ConfigureAwait(false);
                }
                else await PutAsync(openedIndex, record, cancellationToken).ConfigureAwait(false);
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
        IReadOnlyList<ItemReference> held,
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
        IReadOnlyList<ItemReference> held,
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

    private async ValueTask ApplyDecidedWritesAsync(CancellationToken cancellationToken)
    {
        if (this.AmbientSession() is { } session)
        {
            await session.ApplyDecidedWritesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private protected IItemDomainStoreSession? AmbientSession() =>
        transactions.Current is { } ambient ? store.SessionIn(ambient) : null;


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

}
