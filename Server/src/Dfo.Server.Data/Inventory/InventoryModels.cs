using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Data.Inventory;

public enum InventoryContainerType : byte
{
    Main = 0,
    Avatar = 1,
    PersonalCargo = 2,
    Equipment = 3,
    Pet = 7,
    AccountCargo = 12,
    TitleBookGeneral = 19,
    TitleBookSpecific = 20,
    TitleBookPvp = 21,
    TitleBookDespair = 22,
    TitleBookEvent = 23,
    QuickSlot = 29,
    KnightShieldEquipped = 33,
    KnightShieldCatalog = 34,
}

public enum InventoryOwnerType : byte
{
    Character = 0,
    Account = 1,
}

public sealed record ChronicleOptionState(
    int Id,
    byte CharacterJob,
    byte FirstGrowType,
    byte EquipmentType,
    byte OptionNumber);

public sealed record RandomOptionState(byte Type, byte Value1, byte Value2);

public sealed record InventoryItemCoreState(
    byte ItemKind,
    int ItemId,
    int Value,
    byte Attribute,
    ushort Durability,
    byte SealFlag,
    int EnchantCardId,
    byte EnchantUpgradeCount,
    byte AmplifyType,
    ushort AmplifyValue,
    int Marker16,
    ChronicleOptionState ChronicleOption0,
    ChronicleOptionState ChronicleOption1,
    int ExpireTime,
    byte EmblemSocketCount,
    int EmblemId1,
    int EmblemId2,
    ushort Rune,
    RandomOptionState RandomOption0,
    RandomOptionState RandomOption1,
    RandomOptionState RandomOption2,
    byte RandomOptionState,
    byte RandomOptionChangedIndex,
    byte RandomOptionChangeState,
    RandomOptionState RandomOptionChange,
    byte GenuineUpgrade,
    byte EmancipateEquipmentLevel,
    byte TradeRestriction,
    ushort TailUnknown0,
    byte TailUnknown1,
    byte TailUnknown2,
    byte TailUnknown3,
    byte RemainUseCount,
    byte SortLockFlag,
    byte EquipmentLockId);

public sealed record InventoryItemState(
    long StorageEntryId,
    int SlotIndex,
    InventoryItemCoreState Core,
    InventoryAvatarDetailState? AvatarDetail = null,
    uint? Noti2CreatureMarker = null);

/// <summary>
/// Avatar-instance facts stored beside an inventory item. A null value means the item is
/// not an avatar; an avatar item always carries a value, including an all-zero loaded row.
/// </summary>
public sealed record InventoryAvatarDetailState(
    int ExpireDate,
    int ClearAvatarId,
    byte[] JewelSocket,
    ushort Color1,
    ushort Color2,
    int DeleteDate);

public sealed record InventoryContainerState(
    InventoryContainerType ContainerType,
    ushort Parameter,
    IReadOnlyList<InventoryItemState> Items);

public sealed record InventoryRuntimeValueState(
    int SlotIndex,
    int ItemId,
    int Value);

public enum InventoryItemValueListType : byte
{
    Cooldown = 0,
    Effect = 1,
}

public sealed record InventoryItemValueState(
    int SortOrder,
    int ItemId,
    int Value);

public sealed record CharacterItemValueEntryState(
    IReadOnlyList<InventoryItemValueState> Cooldowns,
    IReadOnlyList<InventoryItemValueState> Effects);

public sealed record InventoryEquipmentLockState(
    byte EquipmentLockId,
    InventoryContainerType ContainerType,
    int SlotIndex,
    byte State,
    int RemainingSeconds);

public sealed record CharacterRentalItemState(
    uint ItemId,
    uint InventoryTemplateId,
    uint ExpireTime);

public sealed record CharacterRentalEntryState(
    IReadOnlyList<CharacterRentalItemState> Items);

public sealed record InventoryGoldLimitState(
    int CarryLimit,
    int AuctionLimit);

public sealed record CharacterInventoryEntryState(
    CharacterId CharacterId,
    IReadOnlyList<InventoryContainerState> Containers,
    IReadOnlyList<InventoryRuntimeValueState> VirtualResources,
    CharacterItemValueEntryState ItemValues,
    IReadOnlyList<InventoryEquipmentLockState> EquipmentLocks,
    CharacterRentalEntryState Rentals,
    InventoryGoldLimitState GoldLimits);

public sealed record AccountCargoEntryState(
    AccountId AccountId,
    InventoryContainerState Container,
    IReadOnlyList<InventoryEquipmentLockState> EquipmentLocks,
    ushort SelectionKey,
    int Value32,
    ushort StoredItemCount);

public sealed record AccountInventoryEntryState(
    AccountId AccountId,
    AccountCargoEntryState Cargo,
    IReadOnlyList<InventoryRuntimeValueState> VirtualResources);

/// <summary>
/// The intrinsic character facts equipment rules may inspect without gaining access to
/// character mutation or entry orchestration.
/// </summary>
public sealed record EquipmentEligibilityCharacterFacts(
    CharacterId CharacterId,
    byte Level,
    byte Job,
    byte GrowType);
