using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Abstractions.Inventory;

/// <summary>Detached value. Only the issuing session's exact registered object may be received.</summary>
public sealed class ItemRecord(ItemSnapshot snapshot)
{
    private readonly ItemSnapshot value = snapshot.DeepCopy();
    public ItemSnapshot Snapshot => value.DeepCopy();
    public int ItemId => value.ItemId;
    public int Count => value.Count;
    public ItemTraits Type => value.Type;
    public bool IsPile => value.IsPile;
    public ItemIdentity? ItemUid => value.ItemUid;
}

/// <summary>A session capability for one uninterrupted occupancy of a storage slot.</summary>
public sealed class ItemReference(object session, ContainerId container, int storageSlotIndex,
    long generation, ItemSnapshot snapshot, BusinessSlot? businessSlot = null)
{
    private readonly ItemSnapshot value = snapshot.DeepCopy();
    public ContainerId ContainerId { get; } = container;
    public int StorageSlotIndex { get; } = storageSlotIndex;
    public long Generation { get; } = generation;
    public BusinessSlot? BusinessSlot { get; } = businessSlot;
    public int SlotIndex => BusinessSlot?.Index ?? StorageSlotIndex;
    public ItemSnapshot Snapshot => value.DeepCopy();
    public int ItemId => value.ItemId;
    public int Count => value.Count;
    public ItemTraits Type => value.Type;
    public bool IsPile => value.IsPile;
    public ItemIdentity? ItemUid => value.ItemUid;
    public bool SameOccupancyAs(ItemReference other) => other.BelongsTo(session)
        && ContainerId == other.ContainerId && StorageSlotIndex == other.StorageSlotIndex
        && Generation == other.Generation;
    public bool BelongsTo(object candidate) => ReferenceEquals(session, candidate);
    public ItemReference WithBusinessSlot(BusinessSlot slot) =>
        new(session, ContainerId, StorageSlotIndex, Generation, value, slot);
}
