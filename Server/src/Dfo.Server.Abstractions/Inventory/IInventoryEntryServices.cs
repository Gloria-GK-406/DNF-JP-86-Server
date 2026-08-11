using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Abstractions.Inventory;

public sealed record InventoryStorageItem(
    long StorageEntryId,
    InventoryOwnerType OwnerType,
    int OwnerId,
    CharacterId? CharacterId,
    InventoryContainerType ContainerType,
    int SlotIndex,
    InventoryItemCoreState Core);

public sealed record InventoryContainerParameter(
    CharacterId CharacterId,
    InventoryContainerType ContainerType,
    ushort Value);

public sealed record AccountCargoStorageState(
    AccountId AccountId,
    ushort SelectionKey,
    int Value32,
    ushort StoredItemCount);

public sealed record InventoryItemValueStorageRecord(
    CharacterId CharacterId,
    InventoryItemValueListType ListType,
    int SortOrder,
    int ItemId,
    int Value);

public sealed record InventoryEquipmentLockStorageRecord(
    CharacterId CharacterId,
    int EquipmentLockId,
    InventoryContainerType ContainerType,
    int SlotIndex,
    int State,
    int RemainingSeconds);

public sealed record CharacterRentalStorageRecord(
    CharacterId CharacterId,
    long ItemId,
    long InventoryTemplateId,
    long ExpireTime);

public sealed record InventoryEntryStorageRecord(
    AccountId AccountId,
    CharacterId CharacterId,
    IReadOnlyList<InventoryStorageItem> CharacterItems,
    IReadOnlyList<InventoryStorageItem> AccountCargoItems,
    IReadOnlyList<InventoryContainerParameter> CharacterContainerParameters,
    AccountCargoStorageState AccountCargoState,
    IReadOnlyList<InventoryRuntimeValueState> CharacterVirtualResources,
    IReadOnlyList<InventoryRuntimeValueState> AccountVirtualResources,
    IReadOnlyList<InventoryItemValueStorageRecord> CharacterItemValues,
    IReadOnlyList<InventoryEquipmentLockStorageRecord> EquipmentLocks,
    IReadOnlyList<CharacterRentalStorageRecord> CharacterRentals,
    InventoryGoldLimitState GoldLimits);

public interface IInventoryEntryRepository
{
    ValueTask<InventoryEntryStorageRecord> LoadAsync(
        AccountId accountId,
        CharacterId characterId,
        CancellationToken cancellationToken);
}

public interface IInventoryEntryService
{
    ValueTask<InventoryEntryState> LoadAsync(
        AccountId accountId,
        CharacterId characterId,
        CancellationToken cancellationToken);
}
