using Dfo.Server.Data.World;

namespace Dfo.Server.Data.Inventory;

public enum ItemSaleRejection { None, NoSession, InvalidRequest, ItemUnavailable, Locked, CannotSell, GoldLimit }

public sealed record ItemSaleReceipt(
    SelectedSessionIdentity Session, BusinessSlot Slot, int ItemId,
    int Count, int Gold, InventoryItemCoreState? RemainingCore, DateTimeOffset PreparedAt);

public sealed record ItemSaleResult(ItemSaleRejection Rejection, ItemSaleReceipt? Receipt = null);
