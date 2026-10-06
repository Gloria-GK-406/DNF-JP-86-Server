using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.DeleteItem;

public sealed class DeleteItemHandler(
    IOnlineUserQueryDomainService onlineUsers,
    ISelectedSessionDomainService sessions,
    IInventorySlotMappingRuleService slotMappings,
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
        // Business intent selects the command; storage location is resolved only afterwards.
        if (message.OperationType is 2 or 3)
        {
            await ConsumeMaterialsAsync(context, message, cancellationToken);
            return;
        }
        if (message.OperationType != 1)
        {
            await sender.SendCmdAsync(context.Connection.ConnectionId,
                new DeleteItemResponse(message.Slot, ErrorCode: DeleteItemResponse.CannotDelete), cancellationToken);
            return;
        }
        BusinessSlot slot;
        try { slot = slotMappings.ToBusinessSlot(message.Slot); }
        catch (InventorySlotMappingException)
        {
            logger.LogInformation("Discard rejected unmapped slot {Slot}", message.Slot);
            await sender.SendCmdAsync(context.Connection.ConnectionId,
                new DeleteItemResponse(message.Slot, ErrorCode: DeleteItemResponse.CannotDelete), cancellationToken);
            return;
        }
        var responseSlot = slotMappings.ToGameSlot(slot);
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
                new DiscardItemCommand(online.OnlineUserId, session,
                    slot,
                    message.EntryCount, message.OperationType,
                    message.ItemId, message.DeleteCount),
                cancellationToken).ConfigureAwait(false);
        }

        if (result.Receipt is { } receipt)
        {
            logger.LogInformation(
                "Discard character={CharacterId} container={Container} slot={Slot} item={ItemId} count={Count}",
                receipt.Session.CharacterId.Value, receipt.Slot.Space, receipt.Slot.Index,
                receipt.ItemId, receipt.AppliedCount);
            await notifications.PublishAsync(
                new NotificationAudience.TheseCharacters([receipt.Session.CharacterId]),
                new ItemDiscardCompleted(receipt, result.QuestProgress), cancellationToken).ConfigureAwait(false);
            return;
        }

        logger.LogInformation(
            "Discard {ConnectionId} container={Container} slot={Slot} item={ItemId} count={Count} refused={Reason}",
            context.Connection.ConnectionId, message.Slot.Space, message.Slot.Index,
            message.ItemId, message.DeleteCount, result.Rejection);
        await sender.SendCmdAsync(context.Connection.ConnectionId,
            new DeleteItemResponse(responseSlot, ErrorCode: DeleteItemResponse.CannotDelete),
            cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask ConsumeMaterialsAsync(NetworkDispatchContext context, DeleteItemRequest message,
        CancellationToken ct)
    {
        var session = sessions.FindByConnection(context.Connection.ConnectionId);
        MaterialConsumptionResult result = new("InvalidRequest", []);
        if (session is not null && message.EntryCount == 1 && message.ReservedZero == 0
            && onlineUsers.TryGetByConnection(context.Connection.ConnectionId, out var online) && online is not null)
        {
            BusinessSlot? location = null;
            try { location = slotMappings.ToBusinessSlot(message.Slot); }
            catch (InventorySlotMappingException) { result = new("UnmappedSlot", []); }
            if (location is not null)
                result = message.OperationType == 2
                    ? await commands.SendAsync(new ConsumeSkillMaterialsCommand(online.OnlineUserId, session,
                        [new(location.Value, message.ItemId, message.DeleteCount)]), ct)
                    : await commands.SendAsync(new ConsumeEffectMaterialsCommand(online.OnlineUserId, session,
                        [new(location.Value, message.ItemId, message.DeleteCount)]), ct);
        }
        logger.LogInformation("Material consumption operation={Operation} character={CharacterId} slot={Slot} item={Item} count={Count} success={Success} reason={Reason}",
            message.OperationType, session?.CharacterId, message.Slot, message.ItemId, message.DeleteCount, result.Success, result.Rejection);
        if (result.Success)
            await notifications.PublishAsync(new NotificationAudience.TheseCharacters([session!.CharacterId]),
                new MaterialsConsumed(session, result.Debits.Single(), DateTimeOffset.UtcNow), ct);
        else if (session is null || sessions.Find(session.AccountId) == session)
            await sender.SendCmdAsync(context.Connection.ConnectionId,
                new DeleteItemResponse(message.Slot, ErrorCode: DeleteItemResponse.CannotDelete), ct);
    }
}
