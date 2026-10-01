using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.DisjointItem;

public sealed record EquipmentDisjointCompleted(EquipmentDisjointReceipt Receipt)
    : INotification<EquipmentDisjointCompleted>;

public sealed class EquipmentDisjointCompletedHandler(
    ISelectedSessionDomainService sessions,
    IInventorySlotProjectionDomainService slotProjection)
    : INotificationHandler<EquipmentDisjointCompleted>
{
    public ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(
        NotificationRecipient recipient,
        EquipmentDisjointCompleted notification,
        CancellationToken cancellationToken)
    {
        var receipt = notification.Receipt;
        if (recipient.CharacterId != receipt.Session.CharacterId
            || recipient.ConnectionId != receipt.Session.ConnectionId
            || sessions.Find(receipt.Session.AccountId) != receipt.Session)
            return ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>([]);

        var messages = new List<IServerNetworkMessage>();
        // A21 checks the portable tool when processing the success ACK. Clear the exhausted
        // tool first; target removal and reward balances must still follow the ACK's own changes.
        var exhaustedTool = receipt.InventoryChanges.SingleOrDefault(change =>
            receipt.PortableToolSlot is { } toolSlot && change.Slot == toolSlot && change.Core is null);
        if (exhaustedTool is not null)
        {
            var tool = slotProjection.Project(exhaustedTool.Slot);
            messages.Add(GameInventoryDelivery.ClearedSlotNotification(tool.Space, tool.Index, receipt.PreparedAt));
        }
        messages.Add(new DisjointItemResponse(
            slotProjection.Project(receipt.TargetSlot),
            [
                .. receipt.Materials.Select(material => new DisjointItemMaterial(
                    slotProjection.Project(material.Slot), material.ItemId, material.Count)),
                .. receipt.AccountMaterials.Select(material => new DisjointItemMaterial(
                    new GameSlot(InventoryContainerType.Main, material.VirtualSlotIndex),
                    material.ItemId,
                    material.AddedCount)),
            ]));

        var projected = receipt.InventoryChanges
            .Where(change => change != exhaustedTool)
            .Select(change => (Slot: slotProjection.Project(change.Slot), change.Core))
            .GroupBy(change => change.Slot.Space)
            .OrderBy(group => (byte)group.Key);
        foreach (var container in projected)
        {
            foreach (var cleared in container.Where(change => change.Core is null)
                         .OrderBy(change => change.Slot.Index))
            {
                messages.Add(GameInventoryDelivery.ClearedSlotNotification(
                    container.Key, cleared.Slot.Index, receipt.PreparedAt));
            }

            var occupied = container
                .Where(change => change.Core is not null)
                .OrderBy(change => change.Slot.Index)
                .Select(change => new InventoryItemState(
                    null, change.Slot.Index, change.Core!))
                .ToArray();
            if (occupied.Length > 0)
            {
                messages.Add(GameInventoryDelivery.Notification(
                    new CommittedInventoryUpdateState(
                        container.Key, occupied, receipt.PreparedAt)));
            }
        }

        if (receipt.AccountMaterials.Count > 0)
        {
            messages.Add(GameInventoryDelivery.VirtualResourceNotification(
                [.. receipt.AccountMaterials.Select(material => new InventoryRuntimeValueState(
                    material.VirtualSlotIndex,
                    material.ItemId,
                    material.FinalCount))],
                receipt.PreparedAt));
        }

        return ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>(messages);
    }
}
