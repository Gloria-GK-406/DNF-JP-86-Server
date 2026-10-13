using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Abstractions.Inventory;

public enum ItemDeliveryMode { InventoryThenMail, MailOnly, InventoryOnly }
/// <summary>An optional restriction within the catalog's physical stackable band.</summary>
public sealed record ItemDeliveryPlacementConstraint(BusinessSlotRange Destination, int StackLimit);
public sealed record ItemDeliveryGrant(int ItemId, int Count, ItemSnapshot? Snapshot = null, ItemRecord? Instance = null)
{
    public ushort? Durability { get; init; }
    public ItemDeliveryPlacementConstraint? PlacementConstraint { get; init; }
}
public sealed record ItemDeliveryAddition(IBusinessItemContainer Container, BusinessSlot Slot, int Count, ItemSnapshot Snapshot);
public sealed record ItemDeliveryReceipt(IReadOnlyList<ItemDeliveryAddition> Inventory, IReadOnlyList<long> MailIds, long MailedCount)
{
    public IReadOnlyList<ItemSnapshot> MailedItems { get; init; } = [];
}
public interface IItemDeliveryDomainService
{
    /// <summary>Unclaimed, live stackable attachments, included in quest/refill holding limits.</summary>
    ValueTask<long> PendingQuantityAsync(CharacterId character, int itemId, CancellationToken ct);
    ValueTask<ItemDeliveryReceipt> DeliverAsync(AccountId account, CharacterId character,
        IReadOnlyList<ItemDeliveryGrant> grants, string source, CancellationToken ct,
        ItemDeliveryMode mode);
}
