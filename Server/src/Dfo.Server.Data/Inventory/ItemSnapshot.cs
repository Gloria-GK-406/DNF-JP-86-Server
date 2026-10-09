namespace Dfo.Server.Data.Inventory;

/// <summary>Complete item value; storage and wire codecs alone interpret legacy fields.</summary>
public sealed record ItemSnapshot
{
    /// <summary>Server-only disposable equipment projection; never part of the client item core.</summary>
    public bool IsMirror { get; init; }
    public ItemIdentity? MirrorSourceUid { get; init; }
    public int ItemId { get; init; }
    /// <summary>Optional appearance override. Avatar zero means transparent; null means unbound. Never part of the item wire core.</summary>
    public int? AppearanceItemId { get; init; }
    public int DisplayItemId => AppearanceItemId ?? ItemId;
    public int Count { get; init; } = 1;
    public ItemTraits Type { get; init; }
    public bool IsPile { get; init; }
    public ItemIdentity? ItemUid { get; init; }
    public byte LegacyKind { get; init; }
    /// <summary>Client attribute seed for equipment; distinct from server ItemUid and not a quality percentage.</summary>
    public int InstanceValue { get; init; }
    public byte Attribute { get; init; }
    public ushort Durability { get; init; }
    public byte SealFlag { get; init; }
    public int EnchantCardId { get; init; }
    public byte EnchantUpgradeCount { get; init; }
    public byte AmplifyType { get; init; }
    public ushort AmplifyValue { get; init; }
    public int Marker16 { get; init; } = -1;
    public ChronicleOptionState ChronicleOption0 { get; init; } = new(0, 0, 0, 0, 0);
    public ChronicleOptionState ChronicleOption1 { get; init; } = new(0, 0, 0, 0, 0);
    public int ExpireTime { get; init; }
    public byte EmblemSocketCount { get; init; }
    public int EmblemId1 { get; init; }
    public int EmblemId2 { get; init; }
    public ushort Rune { get; init; }
    public RandomOptionState RandomOption0 { get; init; } = new(0, 0, 0);
    public RandomOptionState RandomOption1 { get; init; } = new(0, 0, 0);
    public RandomOptionState RandomOption2 { get; init; } = new(0, 0, 0);
    public byte RandomOptionState { get; init; }
    public byte RandomOptionChangedIndex { get; init; } = byte.MaxValue;
    public byte RandomOptionChangeState { get; init; }
    public RandomOptionState RandomOptionChange { get; init; } = new(0, 0, 0);
    public byte GenuineUpgrade { get; init; }
    public byte EmancipateEquipmentLevel { get; init; }
    public byte TradeRestriction { get; init; }
    public ushort TailUnknown0 { get; init; }
    public byte TailUnknown1 { get; init; }
    public byte TailUnknown2 { get; init; }
    public byte TailUnknown3 { get; init; }
    public byte RemainUseCount { get; init; }
    public byte SortLockFlag { get; init; }
    public byte EquipmentLockId { get; init; }
    public InventoryAvatarDetailState? AvatarDetail { get; init; }
    public InventoryCreatureDetailState? CreatureDetail { get; init; }
    public byte ReinforcementLevel => (byte)(Attribute & 0x1F);

    public ItemSnapshot DeepCopy() => this with
    {
        AvatarDetail = AvatarDetail is { } avatar ? avatar with { JewelSocket = [.. avatar.JewelSocket] } : null,
        CreatureDetail = CreatureDetail is { } creature ? creature with { CreatureText = [.. creature.CreatureText] } : null,
    };

    public bool CanStackWith(ItemSnapshot other) => IsPile && other.IsPile
        && (this with { Count = 1, ItemUid = null, InstanceValue = 0, AvatarDetail = null, CreatureDetail = null })
            == (other with { Count = 1, ItemUid = null, InstanceValue = 0, AvatarDetail = null, CreatureDetail = null })
        && AvatarDetail is null && other.AvatarDetail is null
        && CreatureDetail is null && other.CreatureDetail is null;

    public InventoryItemCoreState ToCore() => new(
        LegacyKind, ItemId, IsPile ? Count : InstanceValue,
        Attribute, Durability, SealFlag, EnchantCardId, EnchantUpgradeCount, AmplifyType, AmplifyValue, Marker16, ChronicleOption0, ChronicleOption1, ExpireTime, EmblemSocketCount, EmblemId1, EmblemId2, Rune, RandomOption0, RandomOption1, RandomOption2, RandomOptionState, RandomOptionChangedIndex, RandomOptionChangeState, RandomOptionChange, GenuineUpgrade, EmancipateEquipmentLevel, TradeRestriction, TailUnknown0, TailUnknown1, TailUnknown2, TailUnknown3, RemainUseCount, SortLockFlag, EquipmentLockId) { AppearanceItemId = AppearanceItemId };

    public static ItemSnapshot FromCore(InventoryItemCoreState core, bool isPile,
        ItemIdentity? itemUid = null, InventoryAvatarDetailState? avatar = null,
        InventoryCreatureDetailState? creature = null) => new ItemSnapshot
    {
        ItemId = core.ItemId, AppearanceItemId = core.AppearanceItemId, Count = isPile ? core.Value : 1,
        Type = ItemTraitClassification.For(core.ItemKind, core.ItemId),
        IsPile = isPile, ItemUid = isPile ? null : itemUid,
        LegacyKind = core.ItemKind, InstanceValue = isPile ? 0 : core.Value,
        Attribute = core.Attribute,
        Durability = core.Durability,
        SealFlag = core.SealFlag,
        EnchantCardId = core.EnchantCardId,
        EnchantUpgradeCount = core.EnchantUpgradeCount,
        AmplifyType = core.AmplifyType,
        AmplifyValue = core.AmplifyValue,
        Marker16 = core.Marker16,
        ChronicleOption0 = core.ChronicleOption0,
        ChronicleOption1 = core.ChronicleOption1,
        ExpireTime = core.ExpireTime,
        EmblemSocketCount = core.EmblemSocketCount,
        EmblemId1 = core.EmblemId1,
        EmblemId2 = core.EmblemId2,
        Rune = core.Rune,
        RandomOption0 = core.RandomOption0,
        RandomOption1 = core.RandomOption1,
        RandomOption2 = core.RandomOption2,
        RandomOptionState = core.RandomOptionState,
        RandomOptionChangedIndex = core.RandomOptionChangedIndex,
        RandomOptionChangeState = core.RandomOptionChangeState,
        RandomOptionChange = core.RandomOptionChange,
        GenuineUpgrade = core.GenuineUpgrade,
        EmancipateEquipmentLevel = core.EmancipateEquipmentLevel,
        TradeRestriction = core.TradeRestriction,
        TailUnknown0 = core.TailUnknown0,
        TailUnknown1 = core.TailUnknown1,
        TailUnknown2 = core.TailUnknown2,
        TailUnknown3 = core.TailUnknown3,
        RemainUseCount = core.RemainUseCount,
        SortLockFlag = core.SortLockFlag,
        EquipmentLockId = core.EquipmentLockId,
        AvatarDetail = avatar, CreatureDetail = creature,
    }.DeepCopy();
}
