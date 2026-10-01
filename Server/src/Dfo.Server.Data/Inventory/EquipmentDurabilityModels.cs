using Dfo.Server.Data.World;

namespace Dfo.Server.Data.Inventory;

public enum EquipmentDurabilityRejection { None, NoSession, InvalidSlot, ItemUnavailable }

public sealed record EquipmentDurabilityReceipt(SelectedSessionIdentity Session, BusinessSlot Slot,
    ushort DurabilityAfter);

public sealed record EquipmentDurabilityResult(EquipmentDurabilityRejection Rejection,
    EquipmentDurabilityReceipt? Receipt = null);
