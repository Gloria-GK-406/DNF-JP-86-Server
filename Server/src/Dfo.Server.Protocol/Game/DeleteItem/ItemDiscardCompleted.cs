using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.StateSynchronization;

namespace Dfo.Server.Protocol.Game.DeleteItem;

public sealed record ItemDiscardCompleted(
    ItemDiscardReceipt Receipt,
    QuestProgressSnapshot? QuestProgress = null) : INotification<ItemDiscardCompleted>;

public sealed class ItemDiscardCompletedHandler(
    ISelectedSessionDomainService sessions,
    IInventorySlotProjectionDomainService slotProjection)
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
            || !sessions.IsInWorld(receipt.Session))
            return ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>([]);

        // The A21 client applies this deletion from the success acknowledgement; never replay it.
        var response = new DeleteItemResponse(
            slotProjection.Project(receipt.Slot), receipt.AppliedCount, Success: true);
        var resources = receipt.Slot.Space == BusinessInventorySpace.SharedMaterial && receipt.RemainingCore is { } remaining
            ? AccountMaterialNotifications.Compose([new(slotProjection.Project(receipt.Slot).Index, remaining.ItemId, remaining.Value)], receipt.PreparedAt)
            : [];
        if (notification.QuestProgress is not { } progress)
            return ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>([response, .. resources]);

        return ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>([
            response,
            .. resources,
            new AcceptedQuestProgressMessage(
                [.. progress.Quests.OrderBy(quest => quest.Slot)
                    .Select(quest => new AcceptedQuestProgressState(
                        checked((ushort)quest.QuestId), unchecked((uint)quest.TriggerValue)))])
        ]);
    }
}
