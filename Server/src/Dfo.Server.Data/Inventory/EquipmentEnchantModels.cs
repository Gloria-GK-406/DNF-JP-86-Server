using Dfo.Server.Data.World;

namespace Dfo.Server.Data.Inventory;

public enum EquipmentEnchantRejection
{
    None,
    NoSession,
    InvalidRequest,
    ItemUnavailable,
    MaterialUnavailable,
    Locked,
    UnsupportedContent,
}

/// <summary>Values staged within the command transaction; publish only after commit.</summary>
public sealed record EquipmentEnchantReceipt(
    SelectedSessionIdentity Session,
    BusinessSlot BeadSlot,
    BusinessSlot TargetSlot,
    int CardItemId,
    InventoryItemCoreState Target,
    InventoryItemCoreState? BeadRemaining,
    DateTimeOffset PreparedAt);

public sealed record EquipmentEnchantResult(
    EquipmentEnchantRejection Rejection,
    EquipmentEnchantReceipt? Receipt = null);
