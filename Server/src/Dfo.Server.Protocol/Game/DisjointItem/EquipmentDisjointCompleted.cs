using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.DisjointItem;

public sealed record EquipmentDisjointCompleted(EquipmentDisjointReceipt Receipt)
    : INotification<EquipmentDisjointCompleted>;

public sealed class EquipmentDisjointCompletedHandler(ISelectedSessionDomainService sessions)
    : INotificationHandler<EquipmentDisjointCompleted>
{
    public ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(
        NotificationRecipient recipient,
        EquipmentDisjointCompleted notification,
        CancellationToken cancellationToken)
    {
        var receipt = notification.Receipt;
        return ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>(
            recipient.CharacterId == receipt.Session.CharacterId
            && recipient.ConnectionId == receipt.Session.ConnectionId
            && sessions.Find(receipt.Session.AccountId) == receipt.Session
                ? [new DisjointItemResponse(receipt)] : []);
    }
}
