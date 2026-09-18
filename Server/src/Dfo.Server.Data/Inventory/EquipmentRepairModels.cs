using Dfo.Server.Data.World;

namespace Dfo.Server.Data.Inventory;

public enum EquipmentRepairRejection { None, NoSession, InvalidRequest, ItemUnavailable, CannotRepair, InsufficientGold }

public sealed record EquipmentRepairReceipt(SelectedSessionIdentity Session, InventoryContainerType Container,
    int SlotIndex, int Gold);

public sealed record EquipmentRepairResult(EquipmentRepairRejection Rejection,
    EquipmentRepairReceipt? Receipt = null);
