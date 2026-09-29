using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.ReinforceItem;

public sealed record EquipmentReinforcementCompleted(
    EquipmentReinforcementReceipt Receipt, ReinforceItemResponse Response)
    : INotification<EquipmentReinforcementCompleted>;

public sealed class EquipmentReinforcementCompletedHandler(
    ISelectedSessionDomainService sessions,
    IInventorySlotProjectionDomainService slots)
    : INotificationHandler<EquipmentReinforcementCompleted>
{
    public ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(
        NotificationRecipient recipient, EquipmentReinforcementCompleted notification,
        CancellationToken cancellationToken)
    {
        var receipt = notification.Receipt;
        if (recipient.CharacterId != receipt.Session.CharacterId
            || recipient.ConnectionId != receipt.Session.ConnectionId
            || sessions.Find(receipt.Session.AccountId) != receipt.Session)
            return ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>([]);

        var messages = new List<IServerNetworkMessage> { notification.Response };
        var finalSlots = receipt.Changes.GroupBy(change => change.Slot)
            .Select(group => group.Last())
            .Select(change => (Slot: slots.Project(change.Slot), change.Core))
            .GroupBy(change => change.Slot.Space)
            .OrderBy(group => (byte)group.Key);
        foreach (var container in finalSlots)
        {
            foreach (var cleared in container.Where(change => change.Core is null)
                         .OrderBy(change => change.Slot.Index))
                messages.Add(GameInventoryDelivery.ClearedSlotNotification(
                    container.Key, cleared.Slot.Index, receipt.PreparedAt));
            var occupied = container.Where(change => change.Core is not null)
                .OrderBy(change => change.Slot.Index)
                .Select(change => new InventoryItemState(
                    null, change.Slot.Index, change.Core!)).ToArray();
            if (occupied.Length > 0)
                messages.Add(GameInventoryDelivery.Notification(
                    new CommittedInventoryUpdateState(container.Key, occupied,
                        receipt.PreparedAt)));
        }
        if (receipt.AccountMaterials.Count > 0)
            messages.Add(GameInventoryDelivery.VirtualResourceNotification(
                [.. receipt.AccountMaterials.Select(material => new InventoryRuntimeValueState(
                    material.VirtualSlotIndex, material.ItemId, material.FinalCount))],
                receipt.PreparedAt));
        messages.Add(GameInventoryDelivery.CarriedGoldNotification(
            receipt.Gold, receipt.PreparedAt));
        return ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>(messages);
    }
}
