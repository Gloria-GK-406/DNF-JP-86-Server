using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.World;

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

public interface ICharacterInventoryEntryDomainService
{
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
    InventoryContainerType ContainerType,
    int SlotIndex,
    int ItemId,
    int GrantedCount,
    int CommittedCount);

public sealed record DailyRefillMutationResult(
    IReadOnlyList<DailyRefillCommittedGrant> Grants);


public interface IEntryInventoryMutationRepository
{
    /// <summary>Every occupied slot of the character's main inventory, in the order it is drawn.</summary>
    ValueTask<IReadOnlyList<InventoryItemState>> ReadMainItemsAsync(
        CharacterId characterId,
        CancellationToken cancellationToken);

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
    ValueTask SaveExistingLeaseAsync(
        SelectedSessionIdentity previousSession,
        CancellationToken cancellationToken);

    ValueTask<SelectCharacterResult> PrepareLeaseAsync(
        SelectedSessionIdentity session,
        SelectCharacterResult result,
        CancellationToken cancellationToken);

    ValueTask<EntryInventoryPreparation> ApplyDailyRefillAsync(
        SelectedSessionIdentity session,
        SelectCharacterResult result,
        CancellationToken cancellationToken);

}

/// <summary>
/// Pure in-memory ownership seam for the selected session's prepared inventory.
/// Keeping it separate prevents disconnect cleanup from resolving PVF or persistence services.
/// </summary>
public interface IEntryInventoryLeaseRegistry
{
    EntryInventoryLeaseSnapshot? Find(AccountId accountId);
    bool TryMarkDirtyExact(SelectedSessionIdentity session, CharacterItemLeaseState inventory);
    void Replace(SelectedSessionIdentity session, CharacterInventoryEntryState inventory);
    bool ReleaseExact(SelectedSessionIdentity session);
}

/// <summary>
/// The mutable selected-session lease is deliberately limited to item containers. Item
/// values, locks, rentals, virtual resources and gold limits have independent mutation
/// owners and are not implied to be persisted when this lease is marked dirty.
/// </summary>
public sealed record CharacterItemLeaseState(
    CharacterId CharacterId,
    IReadOnlyList<InventoryContainerState> Containers);

public sealed record EntryInventoryLeaseSnapshot(
    SelectedSessionIdentity Session,
    CharacterItemLeaseState Inventory,
    long Version,
    bool IsDirty);

public interface IEntryInventoryLeasePersistence
{
    ValueTask SaveAsync(EntryInventoryLeaseSnapshot lease, CancellationToken cancellationToken);
}

