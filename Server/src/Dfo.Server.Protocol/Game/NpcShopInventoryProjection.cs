using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Protocol.Game;

internal static class NpcShopInventoryProjection
{
    internal static IEnumerable<IGameNotiMessage> SpentItems(NpcShopPurchaseReceipt receipt)
    {
        foreach (var change in receipt.MaterialChanges)
        {
            var wire = A21InventoryLayout.ToGameSlot(change.Slot);
            yield return change.Core is { } core
                ? GameInventoryDelivery.Notification(new(wire.Space,
                    [new InventoryItemState(null, wire.Index, core.ToCore())], receipt.PreparedAt))
                : GameInventoryDelivery.ClearedSlotNotification(wire.Space, wire.Index, receipt.PreparedAt);
        }
    }

    internal static IReadOnlyList<IGameNotiMessage> GrantedItems(NpcShopPurchaseReceipt receipt) =>
        receipt.GrantedSlots.GroupBy(grant => A21InventoryLayout.ToGameSlot(grant.Slot).Space)
            .Select(group => GameInventoryDelivery.Notification(new CommittedInventoryUpdateState(group.Key,
                group.Select(grant => new InventoryItemState(null, A21InventoryLayout.ToGameSlot(grant.Slot).Index,
                    grant.Core.ToCore())).ToArray(), receipt.PreparedAt))).ToArray();
}
