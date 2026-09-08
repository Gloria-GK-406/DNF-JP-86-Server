using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.BuyItem;

public sealed record NpcShopPurchaseCompleted(NpcShopPurchaseReceipt Receipt) : INotification<NpcShopPurchaseCompleted>;

public sealed class NpcShopPurchaseCompletedHandler(ISelectedSessionDomainService sessions)
    : INotificationHandler<NpcShopPurchaseCompleted>
{
    public ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(
        NotificationRecipient recipient, NpcShopPurchaseCompleted notification, CancellationToken cancellationToken)
    {
        var receipt = notification.Receipt;
        if (recipient.CharacterId != receipt.Session.CharacterId || recipient.ConnectionId != receipt.Session.ConnectionId
            || sessions.Find(receipt.Session.AccountId) != receipt.Session)
            return ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>([]);
        var messages = new List<IServerNetworkMessage>();
        foreach (var change in receipt.MaterialChanges)
            messages.Add(change.Core is { } core
                ? GameInventoryDelivery.Notification(new(InventoryContainerType.Main,
                    [new InventoryItemState(null, change.SlotIndex, core)], receipt.PreparedAt))
                : GameInventoryDelivery.ClearedSlotNotification(InventoryContainerType.Main, change.SlotIndex, receipt.PreparedAt));
        messages.Add(new BuyItemResponse(receipt));
        if (receipt.GoldSpent)
            messages.Add(GameInventoryDelivery.CarriedGoldNotification(receipt.Gold, receipt.PreparedAt));
        messages.Add(GameInventoryDelivery.Notification(new CommittedInventoryUpdateState(InventoryContainerType.Main,
            receipt.GrantedSlots.Select(slot => slot.Item).ToArray(), receipt.PreparedAt)));
        return ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>(messages);
    }
}
