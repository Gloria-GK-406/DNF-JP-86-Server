using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Inventory;

/// <summary>
/// What an avatar has to be given at the moment the world first takes it.
/// </summary>
/// <remarks>New avatars receive a positive instance identity and all six detail fields before creation.</remarks>
internal static class ArrivingAvatar
{
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

    /// <summary>A positive instance value for a newly granted independent item.</summary>
    internal static int NewIdentity() => Dfo.Server.Application.Inventory.EquipmentQualitySeeds.NewRandom();

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
    IItemSlotRuleService slotRules, IInventoryCapacityDomainService? capacities = null) : IItemContainer
{
    public ValueTask FlushAsync(CancellationToken cancellationToken) =>
        this.ApplyDecidedWritesAsync(cancellationToken);
    public ContainerId Id => id;

    public InventoryContainerType Type => shape.ContainerType;

    public ContainerShape Shape => shape;

    public abstract bool IsRemovable { get; }
    public abstract bool IsDefault { get; }
    public abstract bool IsPersistent { get; }

    public ValueTask<ItemReference?> GetAsync(int index, CancellationToken ct) =>
        ReadAsync(session => session.GetAsync(id, index, ct));

    public ValueTask<IReadOnlyList<ItemReference>> GetAllAsync(CancellationToken ct) =>
        ReadAsync(session => session.GetAllAsync(id, ct));

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
        if ((capacities is not null || !IsPersistent) && !virtualSlot)
        {
            var business = LegacyInventoryStorageCoordinates.ToBusiness(new(Type, reference.StorageSlotIndex), before.LegacyKind);
            if (business.Index >= await OpenSlotsAsync(business.Space, ct).ConfigureAwait(false)
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

    private ValueTask<int> OpenSlotsAsync(BusinessInventorySpace space, CancellationToken ct) =>
        IsPersistent ? capacities!.OpenSlotCountAsync(id, space, ct)
            : ValueTask.FromResult(new InventoryCapacityRuleService().OpenSlotCount(space, Shape.Parameter));

    private async ValueTask ValidatePlacementAsync(int index, ItemSnapshot snapshot, long total, CancellationToken ct)
    {
        if (Type == InventoryContainerType.Main && index == 1
            && snapshot is { LegacyKind: 0, ItemId: 1, Type: ItemTraits.None, IsPile: true }) return;
        var business = LegacyInventoryStorageCoordinates.ToBusiness(new(Type, index), snapshot.LegacyKind);
        if ((capacities is not null || !IsPersistent) && business.Index >= await OpenSlotsAsync(business.Space, ct).ConfigureAwait(false))
            throw new InvalidOperationException($"Slot {business} is not open.");
        var placement = await placementRules.PlacementForAsync(snapshot.ItemId, business.Space, ct).ConfigureAwait(false);
        var worn = business.Space is BusinessInventorySpace.WornEquipment or BusinessInventorySpace.WornEquipmentExtension
            or BusinessInventorySpace.WornAvatar or BusinessInventorySpace.WornCreature or BusinessInventorySpace.WornCreatureEquipment;
        var admitsSlot = placement is not null && (worn
            ? ItemContainerAdmission.For(business.Space).Matches(snapshot.Type)
                && await placementRules.WornSlotForAsync(snapshot.ItemId, ct).ConfigureAwait(false) == business
            : slotRules.CanOccupySlot(business, placement));
        if (IsPersistent && Type == InventoryContainerType.SharedMaterial && (!id.TryGetAccountOwner(out _, out var type) || type != Type)
            || placement is null || snapshot.LegacyKind != placement.ItemKind || snapshot.Type != placement.Traits
            || snapshot.IsPile != (placement.WorldClass == ItemWorldClass.Interchangeable)
            || snapshot.IsPile && placement.StackLimit > 0 && total > placement.StackLimit
            || !admitsSlot)
            throw new InvalidOperationException($"Item {snapshot.ItemId} cannot occupy {id}/{index}.");
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
    ContainerShape shape,
    IItemDomainStore store,
    IDomainTransactionAccessor transactions,
    IItemPlacementRuleService placementRules,
    IItemSlotRuleService slotRules, IInventoryCapacityDomainService? capacities = null)
    : StoreBackedItemContainer(
        id, shape, store, transactions, placementRules, slotRules, capacities)
{
    public override bool IsRemovable => false;
    public override bool IsDefault => true;
    public override bool IsPersistent => true;
}

internal sealed class MemoryItemContainer(ContainerId id, ContainerShape shape,
    IItemDomainStore store, IDomainTransactionAccessor transactions,
    IItemPlacementRuleService placements, IItemSlotRuleService slots)
    : StoreBackedItemContainer(id, shape, store, transactions, placements, slots)
{
    internal IItemLifecycleDomainService Lifecycle { get; } = new ItemLifecycleDomainService(store, transactions, placements);
    public override bool IsRemovable => false;
    public override bool IsDefault => true;
    public override bool IsPersistent => false;
}
