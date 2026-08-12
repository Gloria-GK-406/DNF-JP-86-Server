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
    InventoryItemCoreState Core,
    InventoryAvatarDetailState? AvatarDetail = null);

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

public sealed record CharacterInventoryEntryStorageRecord(
    AccountId AccountId,
    CharacterId CharacterId,
    IReadOnlyList<InventoryStorageItem> CharacterItems,
    IReadOnlyList<InventoryContainerParameter> CharacterContainerParameters,
    IReadOnlyList<InventoryRuntimeValueState> CharacterVirtualResources,
    IReadOnlyList<InventoryItemValueStorageRecord> CharacterItemValues,
    IReadOnlyList<InventoryEquipmentLockStorageRecord> EquipmentLocks,
    IReadOnlyList<CharacterRentalStorageRecord> CharacterRentals,
    InventoryGoldLimitState GoldLimits);

public sealed record AccountInventoryEntryStorageRecord(
    AccountId AccountId,
    CharacterId CharacterId,
    IReadOnlyList<InventoryStorageItem> AccountCargoItems,
    AccountCargoStorageState AccountCargoState,
    IReadOnlyList<InventoryRuntimeValueState> AccountVirtualResources,
    IReadOnlyList<InventoryEquipmentLockStorageRecord> EquipmentLocks);

public interface ICharacterInventoryEntryRepository
{
    ValueTask<CharacterInventoryEntryStorageRecord> LoadCharacterAsync(
        AccountId accountId,
        CharacterId characterId,
        CancellationToken cancellationToken);
}

public interface IAccountInventoryEntryRepository
{
    ValueTask<AccountInventoryEntryStorageRecord> LoadAccountAsync(
        AccountId accountId,
        CharacterId characterId,
        CancellationToken cancellationToken);
}

public interface ICharacterInventoryEntryService
{
    ValueTask<CharacterInventoryEntryState> LoadAsync(
        AccountId accountId,
        CharacterId characterId,
        CancellationToken cancellationToken);
}

public interface IAccountInventoryEntryService
{
    ValueTask<AccountInventoryEntryState> LoadAsync(
        AccountId accountId,
        CharacterId characterId,
        CancellationToken cancellationToken);
}

/// <summary>Reads equipment layout facts from a loaded character inventory.</summary>
public interface IEquippedCreatureQuery
{
    int GetItemId(CharacterInventoryEntryState inventory);
}

/// <summary>
/// Purpose-limited view of character facts required by equipment eligibility rules.
/// </summary>
public interface IEquipmentEligibilityCharacterReader
{
    ValueTask<EquipmentEligibilityCharacterFacts> GetAsync(
        AccountId accountId,
        CharacterId characterId,
        CancellationToken cancellationToken);
}
