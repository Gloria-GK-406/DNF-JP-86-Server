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
            || sessions.Find(receipt.Session.AccountId) != receipt.Session)
            return ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>([]);

        // The A21 client applies this deletion from the success acknowledgement; never replay it.
        var response = new DeleteItemResponse(
            slotProjection.Project(receipt.Slot), receipt.AppliedCount, Success: true);
        if (notification.QuestProgress is not { } progress)
            return ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>([response]);

        return ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>([
            response,
            new AcceptedQuestProgressMessage(
                [.. progress.Quests.OrderBy(quest => quest.Slot)
                    .Select(quest => new AcceptedQuestProgressState(
                        checked((ushort)quest.QuestId), unchecked((uint)quest.TriggerValue)))])
        ]);
    }
}
