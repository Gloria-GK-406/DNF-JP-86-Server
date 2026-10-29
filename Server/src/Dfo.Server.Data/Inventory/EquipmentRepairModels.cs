using Dfo.Server.Data.World;

namespace Dfo.Server.Data.Inventory;

public enum EquipmentRepairRejection { None, NoSession, InvalidRequest, ItemUnavailable, CannotRepair, InsufficientGold, MissingEntitlement, DailyLimitReached, NoBrokenEquipment }

public sealed record EquipmentRepairReceipt(
    SelectedSessionIdentity Session, BusinessSlot? Slot, int Gold)
{
    public EquipmentRepairCouponChange? Coupon { get; init; }
}

public sealed record EquipmentRepairCouponChange(BusinessSlot Slot, InventoryItemCoreState? Remaining);

public sealed record EquipmentRepairResult(EquipmentRepairRejection Rejection,
    EquipmentRepairReceipt? Receipt = null)
{
    public Dfo.Server.Data.Accounts.AccountEntitlementEntryState? Entitlements { get; init; }
    public IReadOnlyList<Dfo.Server.Data.Characters.CharacterPremiumUsageSlotState> Usage { get; init; } = [];
}
