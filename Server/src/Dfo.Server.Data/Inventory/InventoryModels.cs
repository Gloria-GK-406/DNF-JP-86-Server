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

/// <summary>An indivisible slot address as the A21 game protocol expresses it.</summary>
/// <remarks>
/// The list and its index are one coordinate.  Keeping them in one value prevents a caller from
/// carrying one without the other or combining the list from one request with the index from
/// another.  This type belongs at protocol and persistence-adapter boundaries; commands and
/// domain behavior use <see cref="BusinessSlot"/> instead.
/// </remarks>
public readonly record struct GameSlot(InventoryContainerType Space, int Index);

/// <summary>The logical inventory spaces understood by game business behavior.</summary>
public enum BusinessInventorySpace
{
    /// <summary>A client-drawn counter which is not backed by an ordinary item slot.</summary>
    VirtualResource,
    QuickSlot,
    CarriedEquipment,
    Consumable,
    Material,
    Quest,
    ExpertMaterial,
    AvatarEmblem,
    Avatar,
    PersonalCargo,
    WornAvatar,
    WornEquipment,
    WornCreature,
    WornCreatureEquipment,
    WornEquipmentExtension,
    CreatureInventory,
    CreatureEquipment,
    CreatureConsumable,
    AccountCargo,
    GuildMedal,
    GuardianGem,
}

public static class BusinessInventorySpaceRules
{
    public static bool IsCarriedItemSpace(this BusinessInventorySpace space) => space is
        BusinessInventorySpace.QuickSlot
        or BusinessInventorySpace.CarriedEquipment
        or BusinessInventorySpace.Consumable
        or BusinessInventorySpace.Material
        or BusinessInventorySpace.Quest
        or BusinessInventorySpace.ExpertMaterial
        or BusinessInventorySpace.AvatarEmblem;

    public static bool IsWornEquipmentSpace(this BusinessInventorySpace space) => space is
        BusinessInventorySpace.WornEquipment
        or BusinessInventorySpace.WornEquipmentExtension;
}

/// <summary>An indivisible slot address used by commands and domain behavior.</summary>
public readonly record struct BusinessSlot(BusinessInventorySpace Space, int Index);

/// <summary>A contiguous range inside exactly one business inventory space.</summary>
/// <remarks>
/// The range uses local business indexes.  It can therefore cross neither protocol lists nor
/// persistence containers, and an item placement cannot accidentally carry an A21 absolute slot
/// offset into domain behavior.
/// </remarks>
public readonly record struct BusinessSlotRange
{
    public BusinessSlotRange(BusinessInventorySpace space, int firstIndex, int lastIndex)
    {
        if (firstIndex < 0 || lastIndex < firstIndex)
            throw new ArgumentOutOfRangeException(nameof(firstIndex),
                $"Business slot range {space}/{firstIndex}..{lastIndex} is invalid.");
        Space = space;
        FirstIndex = firstIndex;
        LastIndex = lastIndex;
    }

    public BusinessInventorySpace Space { get; }

    public int FirstIndex { get; }

    public int LastIndex { get; }

    public bool Contains(BusinessSlot slot) =>
        slot.Space == Space && slot.Index >= FirstIndex && slot.Index <= LastIndex;
}

/// <summary>A game/business inventory coordinate cannot be represented on the other side.</summary>
public sealed class InventorySlotMappingException : Exception
{
    public InventorySlotMappingException(GameSlot source)
        : base($"Game inventory slot {(byte)source.Space}/{source.Index} has no business mapping.")
    {
        GameSlot = source;
    }

    public InventorySlotMappingException(BusinessSlot source)
        : base($"Business inventory slot {source.Space}/{source.Index} has no game mapping.")
    {
        BusinessSlot = source;
    }

    public GameSlot? GameSlot { get; }

    public BusinessSlot? BusinessSlot { get; }
}

public enum InventoryOwnerType : byte
{
    Character = 0,
    Account = 1,
}

/// <summary>What a thing with an identity is called, for as long as it exists.</summary>
/// <remarks>
/// It is the thing's own name, not the name of a row that happens to hold it right now: the row
/// survives a move, so this value survives a move, and two reads either side of a write of the
/// same thing answer the same number.
/// <para>
/// A pile has none. Two of a pile are the same thing, so there is nothing for a name to tell
/// apart, and every shape that can describe a pile therefore says <c>ItemIdentity?</c> rather than
/// <c>ItemIdentity</c>. That is not a convenience: a shape that could name a pile would be handing
/// out exactly the mark somebody would go on to tell two identical piles apart by.
/// </para>
/// </remarks>
public readonly record struct ItemIdentity(int Value);

/// <summary>What kind of thing a slot's id names.</summary>
public enum ItemSlotKind
{
    /// <summary>One thing in the world, of which there is exactly one.</summary>
    World,

    /// <summary>An archive template: a pile of interchangeable things, of which there are several.</summary>
    Template,
}

/// <summary>
/// What one position holds, said the same way whichever of the two it is.
/// </summary>
/// <remarks>
/// Three facts and no branch: which kind the id names, the id, and how many. A position holding a
/// sword and one holding a stack of potions differ only in what those three say -- the sword's id
/// is its identity and there is one of it, the potions' id is their template and there are as many
/// as the pile holds. It is the same three the stored position carries, so nothing has to be
/// translated between what a slot is and what a slot row says.
/// </remarks>
public sealed record InventorySlotContent(ItemSlotKind Kind, int Id, int Count)
{
    /// <summary>The name of the thing here, or nothing when this position holds a pile.</summary>
    public ItemIdentity? Identity =>
        this.Kind is ItemSlotKind.World ? new ItemIdentity(this.Id) : null;
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
    byte EquipmentLockId)
{
    public byte ReinforcementLevel => (byte)(Attribute & 0x1F);
}

/// <summary>What one position of a container holds, as the client is shown it.</summary>
/// <remarks>
/// <see cref="Identity"/> is the thing's own name and survives every write of it. It replaces the
/// storage key this projection used to carry, which named the row rather than the thing and so
/// changed whenever the row did.
/// <para>
/// It is absent for a pile, which has no name to carry. Nothing may therefore tell two piles of
/// one template apart by anything here: what is left of a pile is its template and its count, and
/// two piles that agree on both are the same thing.
/// </para>
/// </remarks>
public sealed record InventoryItemState(
    ItemIdentity? Identity,
    int SlotIndex,
    InventoryItemCoreState Core,
    InventoryAvatarDetailState? AvatarDetail = null,
    uint? Noti2CreatureMarker = null)
{
    /// <summary>This position said in the one shape both kinds of content use.</summary>
    /// <remarks>
    /// Derived rather than stored, so it cannot drift from the identity beside it. A pile's count
    /// is what its core carries; a thing with a name is one of itself.
    /// </remarks>
    public InventorySlotContent Holds => this.Identity is { } identity
        ? new InventorySlotContent(ItemSlotKind.World, identity.Value, 1)
        : new InventorySlotContent(
            ItemSlotKind.Template, this.Core.ItemId, Math.Max(this.Core.Value, 1));
}

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
