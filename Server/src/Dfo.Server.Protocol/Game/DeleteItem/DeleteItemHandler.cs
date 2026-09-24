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
        // Account-wide virtual materials deliberately have no ordinary character-bag mapping.
        var material = message.Slot.Space == InventoryContainerType.Main
            ? AccountMaterialCatalog.Definitions.SingleOrDefault(d => d.VirtualSlotIndex == message.Slot.Index) : null;
        if (material is not null)
        {
            var selected = sessions.FindByConnection(context.Connection.ConnectionId);
            AccountMaterialChange? change = null;
            if (selected is not null && message.ReservedZero == 0
                && onlineUsers.TryGetByConnection(context.Connection.ConnectionId, out var owner) && owner is not null)
                change = (await commands.SendAsync(new ConsumeAccountMaterialCommand(owner.OnlineUserId, selected,
                    material.Kind, message.ItemId, message.DeleteCount, message.EntryCount, message.OperationType), cancellationToken)).Change;
            if (selected is not null && sessions.Find(selected.AccountId) != selected) return;
            logger.LogInformation("Material consumption slot={Slot} item={ItemId} count={Count} success={Success} remaining={Remaining}",
                message.Slot.Index, message.ItemId, message.DeleteCount, change is not null, change?.FinalCount);
            // The existing DELETE_ITEM ACK applies the decrement locally; never send a second decrement.
            await sender.SendCmdAsync(context.Connection.ConnectionId,
                new DeleteItemResponse(message.Slot, change is null ? 0 : message.DeleteCount,
                    Success: change is not null, ErrorCode: DeleteItemResponse.CannotDelete), cancellationToken);
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
                new DeleteItemCommand(online.OnlineUserId, session,
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
}
