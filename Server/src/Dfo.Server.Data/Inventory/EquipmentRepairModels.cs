using Dfo.Server.Data.World;

namespace Dfo.Server.Data.Inventory;

public enum EquipmentRepairRejection { None, NoSession, InvalidRequest, ItemUnavailable, CannotRepair, InsufficientGold }

public sealed record EquipmentRepairReceipt(
    SelectedSessionIdentity Session, BusinessSlot? Slot, int Gold);

public sealed record EquipmentRepairResult(EquipmentRepairRejection Rejection,
    EquipmentRepairReceipt? Receipt = null);
