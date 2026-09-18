using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.DeleteItem;

public sealed class DeleteItemHandler(
    IOnlineUserQueryDomainService onlineUsers,
    ISelectedSessionDomainService sessions,
    ICommandBus commands,
    INotificationBus notifications,
    IGameMessageSender sender,
    ILogger<DeleteItemHandler> logger) : ClientMessageHandler<DeleteItemRequest>
{
    protected override async ValueTask HandleMessageAsync(
        NetworkDispatchContext context,
        DeleteItemRequest message,
        CancellationToken cancellationToken)
    {
        var session = sessions.FindByConnection(context.Connection.ConnectionId);
        DeleteItemOutcome result;
        if (session is null
            || !onlineUsers.TryGetByConnection(context.Connection.ConnectionId, out var online)
            || online is null)
        {
            result = new(new ItemDiscardResult(ItemDiscardRejection.NoSession), null);
        }
        else
        {
            result = await commands.SendAsync(
                new DeleteItemCommand(online.OnlineUserId, session, message.Container,
                    message.EntryCount, message.OperationType, message.SlotIndex,
                    message.ItemId, message.DeleteCount),
                cancellationToken).ConfigureAwait(false);
        }

        if (result.Receipt is { } receipt)
        {
            logger.LogInformation(
                "Discard character={CharacterId} container={Container} slot={Slot} item={ItemId} count={Count}",
                receipt.Session.CharacterId.Value, receipt.Container, receipt.SlotIndex,
                receipt.ItemId, receipt.AppliedCount);
            await notifications.PublishAsync(
                new NotificationAudience.TheseCharacters([receipt.Session.CharacterId]),
                new ItemDiscardCompleted(receipt, result.QuestProgress), cancellationToken).ConfigureAwait(false);
            return;
        }

        logger.LogInformation(
            "Discard {ConnectionId} container={Container} slot={Slot} item={ItemId} count={Count} refused={Reason}",
            context.Connection.ConnectionId, message.Container, message.SlotIndex,
            message.ItemId, message.DeleteCount, result.Rejection);
        await sender.SendCmdAsync(context.Connection.ConnectionId,
            new DeleteItemResponse(null, DeleteItemResponse.CannotDelete, message.Container),
            cancellationToken).ConfigureAwait(false);
    }
}
