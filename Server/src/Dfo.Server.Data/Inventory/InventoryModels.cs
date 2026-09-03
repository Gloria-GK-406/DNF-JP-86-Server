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
    GuildMedal = 38,
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

/// <summary>
/// One of the main list's virtual slots: a count the client draws, not a piece it can move.
/// </summary>
/// <remarks>
/// The main list's first three slots hold currencies rather than items, and the item id is fixed
/// per slot: <b>slot 0 is the gold the character carries</b> (item id 0), slot 1 the revival coin,
/// slot 2 the PvP point. Real items begin at slot 3, so the two never collide. Six cube fragments
/// follow at 354..359 and five more counts at 360..364, each likewise a fixed id.
/// <para>
/// <b>Slot 0 is a projection, not a stored row.</b> What a character carries lives on the
/// character itself, in <c>character_stats.carried_gold</c> beside the cap that governs it, and is
/// written into this slot when a frame is built. It is stored that way so that a change to it can
/// be a single statement: through the item store it could only be read, adjusted and written back,
/// and two changes answered at once would then race. Slots 1 and 2 are still read from item rows,
/// and nothing writes them yet.
/// </para>
/// <para>
/// <see cref="Value"/> is the count. It is what the storage row's item core carries in its own
/// value field, which is why a currency needs no row shape of its own.
/// </para>
/// </remarks>
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

/// <summary>
/// What this character may carry and bid in gold. Both cap the gold on the player's bag, which
/// this server does not keep yet.
/// </summary>
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

/// <param name="Value32">
/// The gold held in this account's cargo, named after the column it is read from. Shared by every
/// character on the account. It is not the gold a character carries -- that one lives in the main
/// list's first virtual slot, see <see cref="InventoryRuntimeValueState"/>.
/// </param>
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
