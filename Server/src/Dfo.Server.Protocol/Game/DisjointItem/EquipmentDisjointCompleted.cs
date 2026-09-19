using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Sessions;
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
        return ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>(
            recipient.CharacterId == receipt.Session.CharacterId
            && recipient.ConnectionId == receipt.Session.ConnectionId
            && sessions.Find(receipt.Session.AccountId) == receipt.Session
                ? [new DisjointItemResponse(
                    slotProjection.Project(receipt.TargetSlot),
                    [.. receipt.Materials.Select(material => new DisjointItemMaterial(
                        slotProjection.Project(material.Slot), material.ItemId, material.Count))])]
                : []);
    }
}
