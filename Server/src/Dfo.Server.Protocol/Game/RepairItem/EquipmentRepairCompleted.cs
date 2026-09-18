using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.RepairItem;

public sealed record EquipmentRepairCompleted(EquipmentRepairReceipt Receipt)
    : INotification<EquipmentRepairCompleted>;

public sealed class EquipmentRepairCompletedHandler(ISelectedSessionDomainService sessions)
    : INotificationHandler<EquipmentRepairCompleted>
{
    public ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(NotificationRecipient recipient,
        EquipmentRepairCompleted notification, CancellationToken cancellationToken)
    {
        var receipt = notification.Receipt;
        return ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>(
            recipient.CharacterId == receipt.Session.CharacterId
            && recipient.ConnectionId == receipt.Session.ConnectionId
            && sessions.Find(receipt.Session.AccountId) == receipt.Session
                ? [new RepairItemResponse(receipt)] : []);
    }
}
