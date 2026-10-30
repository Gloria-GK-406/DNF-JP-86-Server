using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Protocol.Game.StateSynchronization;

namespace Dfo.Server.Protocol.Game.RepairItem;

public sealed record EquipmentRepairCompleted(
    EquipmentRepairReceipt Receipt,
    InventoryContainerType RepairAllSpace = InventoryContainerType.Main,
    AccountEntitlementEntryState? Entitlements = null,
    IReadOnlyList<CharacterPremiumUsageSlotState>? Usage = null, DateTimeOffset PreparedAt = default)
    : INotification<EquipmentRepairCompleted>;

public sealed class EquipmentRepairCompletedHandler(
    ISelectedSessionDomainService sessions)
    : INotificationHandler<EquipmentRepairCompleted>
{
    public ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(NotificationRecipient recipient,
        EquipmentRepairCompleted notification, CancellationToken cancellationToken)
    {
        var receipt = notification.Receipt;
        if (recipient.CharacterId != receipt.Session.CharacterId
            || recipient.ConnectionId != receipt.Session.ConnectionId
            || !sessions.IsInWorld(receipt.Session))
            return ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>([]);
        List<IServerNetworkMessage> messages = [];
        if (receipt.Coupon is { } coupon)
        {
            var source = A21InventoryLayout.ToGameSlot(coupon.Slot);
            messages.Add(coupon.Remaining is null
                ? GameInventoryDelivery.ClearedSlotNotification(source.Space, source.Index, notification.PreparedAt)
                : GameInventoryDelivery.Notification(new(source.Space,
                    [new InventoryItemState(null, source.Index, coupon.Remaining)], notification.PreparedAt)));
        }
        messages.Add(new RepairItemResponse(
            receipt.Slot is { } slot ? A21InventoryLayout.ToGameSlot(slot) : null, receipt.Gold,
            RepairAllSpace: notification.RepairAllSpace));
        if (notification.Entitlements is { } entitlements)
            messages.Add(new PremiumServiceStateMessage(entitlements, notification.Usage ?? [], notification.PreparedAt));
        return ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>(messages);
    }
}
