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
        if (message.OperationType == 2)
        {
            await ConsumeSkillMaterialsAsync(context, message, cancellationToken);
            return;
        }
        if (message.OperationType != 1)
        {
            await sender.SendCmdAsync(context.Connection.ConnectionId,
                new DeleteItemResponse(message.Slot, ErrorCode: DeleteItemResponse.CannotDelete), cancellationToken);
            return;
        }
        // Account-wide virtual materials deliberately have no ordinary character-bag mapping.
        var material = message.Slot.Space == InventoryContainerType.Main
            ? AccountMaterialCatalog.Definitions.SingleOrDefault(d => d.VirtualSlotIndex == message.Slot.Index) : null;
        if (material is not null)
        {
            logger.LogInformation("Material request slot={Slot} item={Item} count={Count} entries={Entries} operation={Operation} reserved={Reserved} safeMode={SafeMode}",
                message.Slot.Index, message.ItemId, message.DeleteCount, message.EntryCount, message.OperationType, message.ReservedZero, message.SafeModeSkipIndex);
            var selected = sessions.FindByConnection(context.Connection.ConnectionId);
            AccountMaterialChange? change = null;
            if (selected is not null && message.ReservedZero == 0
                && onlineUsers.TryGetByConnection(context.Connection.ConnectionId, out var owner) && owner is not null)
                change = (await commands.SendAsync(new DiscardAccountMaterialCommand(owner.OnlineUserId, selected,
                    material.Kind, message.ItemId, message.DeleteCount, message.EntryCount, message.OperationType), cancellationToken)).Change;
            if (selected is not null && sessions.Find(selected.AccountId) != selected) return;
            logger.LogInformation("Material consumption slot={Slot} item={ItemId} count={Count} success={Success} remaining={Remaining}",
                message.Slot.Index, message.ItemId, message.DeleteCount, change is not null, change?.FinalCount);
            // Keep the native ACK, then reconcile with the committed absolute balance.
            await sender.SendCmdAsync(context.Connection.ConnectionId,
                new DeleteItemResponse(message.Slot, change is null ? 0 : message.DeleteCount,
                    Success: change is not null, ErrorCode: DeleteItemResponse.CannotDelete), cancellationToken);
            if (change is not null)
                await sender.SendExplicitAsync(context.Connection.ConnectionId,
                    new GameExplicitMessageDelivery([GameInventoryDelivery.VirtualResourceNotification(
                        [new InventoryRuntimeValueState(change.VirtualSlotIndex, change.ItemId, change.FinalCount)],
                        DateTimeOffset.UtcNow)], []), cancellationToken);
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

    private async ValueTask ConsumeSkillMaterialsAsync(NetworkDispatchContext context, DeleteItemRequest message,
        CancellationToken ct)
    {
        var session = sessions.FindByConnection(context.Connection.ConnectionId);
        SkillMaterialConsumptionResult result = new("InvalidRequest", []);
        if (session is not null && message.EntryCount == 1 && message.ReservedZero == 0
            && onlineUsers.TryGetByConnection(context.Connection.ConnectionId, out var online) && online is not null)
        {
            var material = message.Slot.Space == InventoryContainerType.Main
                ? AccountMaterialCatalog.Definitions.SingleOrDefault(x => x.VirtualSlotIndex == message.Slot.Index) : null;
            SkillMaterialLocation? location = null;
            if (material is not null) location = new SkillMaterialLocation.Account(material.Kind);
            else
            {
                try { location = new SkillMaterialLocation.Carried(slotMappings.ToBusinessSlot(message.Slot)); }
                catch (InventorySlotMappingException) { result = new("UnmappedSlot", []); }
            }
            if (location is not null)
                result = await commands.SendAsync(new ConsumeSkillMaterialsCommand(online.OnlineUserId, session,
                    [new(location, message.ItemId, message.DeleteCount)]), ct);
        }
        logger.LogInformation("Skill material consumption character={CharacterId} slot={Slot} item={Item} count={Count} success={Success} reason={Reason}",
            session?.CharacterId, message.Slot, message.ItemId, message.DeleteCount, result.Success, result.Rejection);
        if (result.Success)
            await notifications.PublishAsync(new NotificationAudience.TheseCharacters([session!.CharacterId]),
                new SkillMaterialsConsumed(session, result.Debits.Single(), DateTimeOffset.UtcNow), ct);
        else if (session is null || sessions.Find(session.AccountId) == session)
            await sender.SendCmdAsync(context.Connection.ConnectionId,
                new DeleteItemResponse(message.Slot, ErrorCode: DeleteItemResponse.CannotDelete), ct);
    }
}
