using Dfo.Server.Data.World;

namespace Dfo.Server.Data.Inventory;

public enum NpcShopRejection
{
    None,
    NoSession,
    ShopUnavailable,
    ItemNotSold,
    InvalidCount,
    UnsupportedItem,
    InsufficientGold,
    InsufficientMaterials,
    NoRoom,
    PurchaseLimitReached,
}

public sealed record NpcShopSlotChange(BusinessSlot Slot, InventoryItemCoreState? Core);

public sealed record NpcShopGrantedSlot(BusinessSlot Slot, InventoryItemCoreState Core, int AddedCount);

public sealed record NpcShopPurchaseReceipt(
    SelectedSessionIdentity Session,
    int ItemId,
    int Count,
    int Gold,
    int SkillPoints,
    int Cera,
    bool GoldSpent,
    IReadOnlyList<NpcShopSlotChange> MaterialChanges,
    IReadOnlyList<NpcShopGrantedSlot> GrantedSlots,
    DateTimeOffset PreparedAt);

public sealed record NpcShopPurchaseResult(NpcShopRejection Rejection, NpcShopPurchaseReceipt? Receipt)
{
    public static NpcShopPurchaseResult Refused(NpcShopRejection reason) => new(reason, null);
}

public sealed record NpcShopPurchaseCount(int ItemId, int Count);

public sealed record NpcShopPurchaseCountsResult(
    NpcShopRejection Rejection, IReadOnlyList<NpcShopPurchaseCount> Counts);
