using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.DeleteItem;

public sealed record ItemDiscardCompleted(ItemDiscardReceipt Receipt) : INotification<ItemDiscardCompleted>;

public sealed class ItemDiscardCompletedHandler(ISelectedSessionDomainService sessions)
    : INotificationHandler<ItemDiscardCompleted>
{
    public ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(
        NotificationRecipient recipient,
        ItemDiscardCompleted notification,
        CancellationToken cancellationToken)
    {
        var receipt = notification.Receipt;
        if (recipient.CharacterId != receipt.Session.CharacterId
            || recipient.ConnectionId != receipt.Session.ConnectionId
            || sessions.Find(receipt.Session.AccountId) != receipt.Session)
            return ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>([]);

        // The A21 client applies this deletion from the success acknowledgement; never replay it.
        return ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>([new DeleteItemResponse(receipt)]);
    }
}
