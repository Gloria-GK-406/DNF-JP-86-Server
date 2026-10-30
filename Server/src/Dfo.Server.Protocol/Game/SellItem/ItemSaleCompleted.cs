using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.SellItem;

public sealed record ItemSaleCompleted(ItemSaleReceipt Receipt) : INotification<ItemSaleCompleted>;

public sealed class ItemSaleCompletedHandler(
    ISelectedSessionDomainService sessions) : INotificationHandler<ItemSaleCompleted>
{
    public ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(NotificationRecipient recipient,
        ItemSaleCompleted notification, CancellationToken cancellationToken)
    {
        var receipt = notification.Receipt;
        if (recipient.CharacterId != receipt.Session.CharacterId || recipient.ConnectionId != receipt.Session.ConnectionId
            || !sessions.IsInWorld(receipt.Session))
            return ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>([]);
        // A21's success handler updates gold and subtracts this count. It must not be replayed.
        return ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>([
            new SellItemResponse(
                A21InventoryLayout.ToGameSlot(receipt.Slot), receipt.Gold, receipt.Count)
        ]);
    }
}
