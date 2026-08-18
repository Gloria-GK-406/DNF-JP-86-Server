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

/// <summary>An item that reached the inventory, and the slot it went to.</summary>
public sealed record GrantedInventoryItem(int SlotIndex, int ItemId, int Count);

/// <summary>What one slot of the main inventory holds.</summary>
public sealed record MainInventoryOccupant(
    long StorageEntryId, int SlotIndex, byte ItemKind, int ItemId, int Count);

/// <summary>What the two slots named by a move request hold right now.</summary>
public sealed record MainInventorySlotPair(
    MainInventoryOccupant? Source, MainInventoryOccupant? Destination);

/// <summary>How a decided move rearranges the two slots.</summary>
public enum MainInventoryMoveKind
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
/// A move the caller has already decided on, for storage to apply as one transaction.
/// </summary>
/// <param name="DisplacedEntryId">
/// What the caller saw in the target slot, or null when it saw the slot empty. Storage rechecks it
/// and refuses the move when the slot has since changed, so a decision taken outside the
/// transaction cannot be applied against a world that has moved on.
/// </param>
public sealed record MainInventoryMovePlan(
    long MovingEntryId,
    int TargetSlotIndex,
    int MoveCount,
    long? DisplacedEntryId,
    MainInventoryMoveKind Kind);

public interface IEntryInventoryMutationRepository
{
    /// <summary>
    /// Puts an item into the character's main inventory, or answers null when there is nowhere for
    /// it to go.
    /// </summary>
    /// <param name="placement">
    /// Where this item belongs, resolved from content by the caller. Storage is handed the answer
    /// rather than looking it up: which band an item occupies is a fact about the item, and a
    /// repository that read content would be deciding as well as recording.
    /// </param>
    /// <remarks>
    /// Answers null rather than throwing when the item's band is full. A full inventory is an
    /// ordinary state of the world and the caller has to say something to the player about it;
    /// an exception would make it indistinguishable from storage being broken.
    /// </remarks>
    ValueTask<GrantedInventoryItem?> GrantItemAsync(
        CharacterId characterId,
        int itemId,
        int count,
        Dfo.Server.Abstractions.Content.ItemPlacement placement,
        CancellationToken cancellationToken);

    /// <summary>Reads what two slots of the character's main inventory hold.</summary>
    ValueTask<MainInventorySlotPair> ReadMainSlotsAsync(
        CharacterId characterId,
        int sourceSlotIndex,
        int destinationSlotIndex,
        CancellationToken cancellationToken);

    /// <summary>
    /// Applies a decided move, or answers false when the slots no longer hold what the caller read.
    /// </summary>
    /// <remarks>
    /// False is an ordinary answer, not a fault: the caller decided the move from a read taken
    /// outside this transaction, and the honest response to a slot that changed underneath is to
    /// tell the client the move did not happen rather than to force it.
    /// </remarks>
    ValueTask<bool> ApplyMainMoveAsync(
        CharacterId characterId,
        MainInventoryMovePlan plan,
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
