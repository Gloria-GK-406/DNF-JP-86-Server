using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Inventory;

public sealed record InventoryContainerParameter(
    CharacterId CharacterId,
    InventoryContainerType ContainerType,
    ushort Value);

/// <param name="Value32">
/// The gold held in the account's cargo, named after the column it is read from. Not the gold a
/// character carries.
/// </param>
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
    IReadOnlyList<ItemReference> CharacterItems,
    IReadOnlyList<InventoryContainerParameter> CharacterContainerParameters,
    IReadOnlyList<InventoryRuntimeValueState> CharacterVirtualResources,
    IReadOnlyList<InventoryItemValueStorageRecord> CharacterItemValues,
    IReadOnlyList<InventoryEquipmentLockStorageRecord> EquipmentLocks,
    IReadOnlyList<CharacterRentalStorageRecord> CharacterRentals,
    InventoryGoldLimitState GoldLimits);

public sealed record AccountInventoryEntryStorageRecord(
    AccountId AccountId,
    CharacterId CharacterId,
    IReadOnlyList<ItemReference> AccountCargoItems,
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

public interface ICharacterInventoryEntryDomainService
{
    /// <summary>Reads current rows without selection-time maintenance or normalization.</summary>
    ValueTask<CharacterInventoryEntryState> ReadCurrentAsync(
        AccountId accountId, CharacterId characterId, CancellationToken cancellationToken);

    ValueTask<CharacterInventoryEntryState> LoadAsync(
        AccountId accountId,
        CharacterId characterId,
        CancellationToken cancellationToken);
}

public interface IAccountInventoryEntryDomainService
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

public sealed record DailyRefillCommittedGrant(
    BusinessSlot Slot,
    int ItemId,
    int GrantedCount,
    int CommittedCount);

public sealed record DailyRefillMutationResult(
    IReadOnlyList<DailyRefillCommittedGrant> Grants);

public interface IDailyRefillDomainService
{
    /// <summary>Refresh independent game-day rules without claiming the first-login batch.</summary>
    ValueTask<DailyRefillMutationResult> ApplyIndependentRefillAsync(
        AccountId accountId, CharacterId characterId,
        IReadOnlyList<Dfo.Server.Abstractions.Content.DailyRefillRule> rules,
        DateTimeOffset at, CancellationToken cancellationToken) => throw new NotSupportedException();

    ValueTask<DailyRefillMutationResult> ApplyDailyRefillAsync(
        AccountId accountId,
        CharacterId characterId,
        IReadOnlyList<Dfo.Server.Abstractions.Content.DailyRefillRule> rules,
        DateTimeOffset preparedAt,
        CancellationToken cancellationToken);
}

public sealed record EntryInventoryPreparation(
    SelectCharacterResult Result,
    IReadOnlyList<CommittedInventoryUpdateState> CommittedUpdates);

public interface ISelectCharacterEntryInventoryDomainService
{
    ValueTask<SelectCharacterResult> PrepareLeaseAsync(
        SelectedSessionIdentity session,
        SelectCharacterResult result,
        CancellationToken cancellationToken);

    ValueTask<EntryInventoryPreparation> ApplyDailyRefillAsync(
        SelectedSessionIdentity session,
        SelectCharacterResult result,
        CancellationToken cancellationToken);

}
