using Dfo.Server.Data.World;

namespace Dfo.Server.Data.Inventory;

public enum EquipmentUpgradeKind : byte
{
    Reinforcement = 0,
    Amplification = 1,
}

/// <summary>Reinforcement and amplification share settlement, with separate archive rules.</summary>
public enum EquipmentReinforcementRejection
{
    None,
    NoSession,
    InvalidRequest,
    ItemUnavailable,
    WrongItem,
    CannotReinforce,
    Locked,
    Durability,
    InsufficientMaterial,
    InsufficientGold,
    NoRoom,
    UnsupportedContent,
    UnsupportedOutcome,
}

public enum EquipmentReinforcementOutcome : byte
{
    Succeeded = 0,
    Retained = 1,
    Downgraded = 2,
    Destroyed = 3,
}

/// <summary>A final slot state from the committed operation; null means empty.</summary>
public sealed record EquipmentReinforcementSlotChange(
    BusinessSlot Slot,
    InventoryItemCoreState? Core);

public sealed record EquipmentReinforcementReward(BusinessSlot? Slot, int ItemId, int Count,
    int? VirtualSlotIndex = null);

/// <summary>Authoritative post-settlement values projected by the protocol after commit.</summary>
public sealed record EquipmentReinforcementReceipt(
    SelectedSessionIdentity Session,
    BusinessSlot TargetSlot,
    BusinessSlot? MaterialSlot,
    BusinessSlot? ProtectionSlot,
    byte OldLevel,
    byte NewLevel,
    EquipmentReinforcementOutcome Outcome,
    int MaterialRemaining,
    int Gold,
    IReadOnlyList<EquipmentReinforcementSlotChange> Changes,
    IReadOnlyList<EquipmentReinforcementReward> DestroyRewards,
    IReadOnlyList<AccountMaterialChange> AccountMaterials,
    DateTimeOffset PreparedAt);

public sealed record EquipmentReinforcementResult(
    EquipmentReinforcementRejection Rejection,
    EquipmentReinforcementReceipt? Receipt = null);
