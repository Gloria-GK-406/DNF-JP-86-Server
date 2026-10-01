using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Abstractions.Inventory;

public sealed record EquipmentTransferItem(
    ItemIdentity Identity,
    InventoryContainerType ContainerType,
    int SlotIndex,
    InventoryItemCoreState Core);
public sealed record EquipmentTransferOwnedItem(ItemIdentity Identity, BusinessSlot Slot,
    InventoryItemCoreState Core);

public enum EquipmentTransferFailure
{
    Missing,
    NotSendable,
    NotDetached,
    NoRoom,
    StalePosition,
}

public sealed class EquipmentTransferException(EquipmentTransferFailure reason)
    : InvalidOperationException($"Equipment transfer refused: {reason}.")
{
    public EquipmentTransferFailure Reason { get; } = reason;
}

/// <summary>Moves an existing ordinary equipment instance between a character slot and detached custody.</summary>
public interface IEquipmentTransferDomainService
{
    ValueTask<EquipmentTransferOwnedItem?> ReadOwnedAsync(
        CharacterId characterId, ItemIdentity identity, CancellationToken cancellationToken);

    ValueTask<EquipmentTransferOwnedItem> TakeAsync(
        CharacterId characterId, ItemIdentity identity, CancellationToken cancellationToken);

    ValueTask<InventoryItemCoreState?> ReadDetachedAsync(
        ItemIdentity identity, CancellationToken cancellationToken);

    ValueTask<BusinessSlot> GrantAsync(
        CharacterId characterId, ItemIdentity identity, CancellationToken cancellationToken);
}

/// <summary>Transactional position handoff; the existing items row is never reconstructed.</summary>
public interface IEquipmentTransferDomainStore : IDomainStore<IEquipmentTransferDomainStoreSession>;

public interface IEquipmentTransferDomainStoreSession
{
    ValueTask<EquipmentTransferItem?> ReadOwnedAsync(
        CharacterId characterId, ItemIdentity identity, CancellationToken cancellationToken);

    ValueTask<InventoryItemCoreState?> ReadDetachedAsync(
        ItemIdentity identity, CancellationToken cancellationToken);

    ValueTask<bool> TakeAsync(
        CharacterId characterId, EquipmentTransferItem item, CancellationToken cancellationToken);

    ValueTask<bool> GrantAsync(
        CharacterId characterId, ItemIdentity identity, LegacyInventoryStorageCoordinate slot, CancellationToken cancellationToken);
}
