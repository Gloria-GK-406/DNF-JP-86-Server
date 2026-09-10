using Dfo.Server.Data.World;

namespace Dfo.Server.Data.Inventory;

public enum ItemDiscardRejection
{
    None,
    NoSession,
    InvalidRequest,
    ItemUnavailable,
    Locked,
    CannotDiscard,
}

public sealed record ItemDiscardReceipt(
    SelectedSessionIdentity Session,
    InventoryContainerType Container,
    int SlotIndex,
    int ItemId,
    int AppliedCount,
    InventoryItemCoreState? RemainingCore,
    DateTimeOffset PreparedAt);

public sealed record ItemDiscardResult(
    ItemDiscardRejection Rejection,
    ItemDiscardReceipt? Receipt = null);
