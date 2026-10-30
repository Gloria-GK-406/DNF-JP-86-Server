using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Quests;
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

public sealed record GameDeleteItemCommand(NetworkDispatchContext Dispatch, DeleteItemRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

public sealed class GameDeleteItemCommandHandler(
    IOnlineUserQueryDomainService onlineUsers,
    ISelectedSessionDomainService sessions,
    IItemDiscardDomainService discards,
    IMaterialConsumptionDomainService materials,
    IQuestProgressDomainService quests,
    TimeProvider clock,
    ICommandWorkUnits units,
    ICommandProjections notifications,
    IGameCommandOutbox outbox,
    ILogger<GameDeleteItemCommandHandler> logger) : ICommandHandler<GameDeleteItemCommand, bool>
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext context,
        GameDeleteItemCommand command, CancellationToken cancellationToken)
    {
        var message = command.Input;
        // Business intent selects the command; storage location is resolved only afterwards.
        if (message.OperationType is 2 or 3)
        {
            return await ConsumeMaterialsAsync(context, command.Dispatch, message, cancellationToken);
        }
        if (message.OperationType != 1)
        {
            await outbox.RegisterCmdAsync(command.Dispatch.Connection.ConnectionId,
                new DeleteItemResponse(message.Slot, ErrorCode: DeleteItemResponse.CannotDelete), cancellationToken);
            return false;
        }
        BusinessSlot slot;
        try { slot = A21InventoryLayout.ToBusinessSlot(message.Slot); }
        catch (InventorySlotMappingException)
        {
            logger.LogInformation("Discard rejected unmapped slot {Slot}", message.Slot);
            await outbox.RegisterCmdAsync(command.Dispatch.Connection.ConnectionId,
                new DeleteItemResponse(message.Slot, ErrorCode: DeleteItemResponse.CannotDelete), cancellationToken);
            return false;
        }
        var responseSlot = A21InventoryLayout.ToGameSlot(slot);
        var session = sessions.FindByConnection(command.Dispatch.Connection.ConnectionId);
        Outcome result;
        if (session is null
            || !onlineUsers.TryGetByConnection(command.Dispatch.Connection.ConnectionId, out var online)
            || online is null)
        {
            result = new(new ItemDiscardResult(ItemDiscardRejection.NoSession), null);
        }
        else
        {
            result = await units.WriteAsync(context, new("DiscardItem", []), async (_, token) =>
            {
                var discard = await discards.DiscardAsync(session, slot, message.EntryCount,
                    message.OperationType, message.ItemId, message.DeleteCount,
                    clock.GetUtcNow(), token).ConfigureAwait(false);
                if (discard.Receipt is null || !slot.Space.IsCarriedItemSpace())
                    return new Outcome(discard, null);
                var progress = await quests.ReportAsync(session.AccountId, session.CharacterId,
                    [new QuestHoldingsChanged()], token).ConfigureAwait(false);
                return new Outcome(discard, progress.NotificationSnapshot);
            }, cancellationToken).ConfigureAwait(false);
        }

        if (result.Receipt is { } receipt)
        {
            logger.LogInformation(
                "Discard character={CharacterId} container={Container} slot={Slot} item={ItemId} count={Count}",
                receipt.Session.CharacterId.Value, receipt.Slot.Space, receipt.Slot.Index,
                receipt.ItemId, receipt.AppliedCount);
            await notifications.ProjectAsync(
                new NotificationAudience.TheseCharacters([receipt.Session.CharacterId]),
                new ItemDiscardCompleted(receipt, result.QuestProgress), cancellationToken).ConfigureAwait(false);
            return true;
        }

        logger.LogInformation(
            "Discard {ConnectionId} container={Container} slot={Slot} item={ItemId} count={Count} refused={Reason}",
            command.Dispatch.Connection.ConnectionId, message.Slot.Space, message.Slot.Index,
            message.ItemId, message.DeleteCount, result.Rejection);
        await outbox.RegisterCmdAsync(command.Dispatch.Connection.ConnectionId,
            new DeleteItemResponse(responseSlot, ErrorCode: DeleteItemResponse.CannotDelete),
            cancellationToken).ConfigureAwait(false);
        return false;
    }

    private async ValueTask<bool> ConsumeMaterialsAsync(CommandExecutionContext context, NetworkDispatchContext dispatch, DeleteItemRequest message,
        CancellationToken ct)
    {
        var session = sessions.FindByConnection(dispatch.Connection.ConnectionId);
        MaterialConsumptionResult result = new("InvalidRequest", []);
        if (session is not null && message.EntryCount == 1 && message.ReservedZero == 0
            && onlineUsers.TryGetByConnection(dispatch.Connection.ConnectionId, out var online) && online is not null)
        {
            BusinessSlot? location = null;
            try { location = A21InventoryLayout.ToBusinessSlot(message.Slot); }
            catch (InventorySlotMappingException) { result = new("UnmappedSlot", []); }
            if (location is not null)
                result = await units.WriteAsync(context,
                    new(message.OperationType == 2 ? "ConsumeSkillMaterials" : "ConsumeEffectMaterials", []),
                    (_, token) => materials.ConsumeAsync(session,
                        [new(location.Value, message.ItemId, message.DeleteCount)], token), ct);
        }
        logger.LogInformation("Material consumption operation={Operation} character={CharacterId} slot={Slot} item={Item} count={Count} success={Success} reason={Reason}",
            message.OperationType, session?.CharacterId, message.Slot, message.ItemId, message.DeleteCount, result.Success, result.Rejection);
        if (result.Success)
            await notifications.ProjectAsync(new NotificationAudience.TheseCharacters([session!.CharacterId]),
                new MaterialsConsumed(session, result.Debits.Single(), DateTimeOffset.UtcNow), ct);
        else if (session is null || sessions.IsInWorld(session))
            await outbox.RegisterCmdAsync(dispatch.Connection.ConnectionId,
                new DeleteItemResponse(message.Slot, ErrorCode: DeleteItemResponse.CannotDelete), ct);
        return result.Success;
    }

    private sealed record Outcome(ItemDiscardResult Discard, QuestProgressSnapshot? QuestProgress)
    {
        public ItemDiscardReceipt? Receipt => Discard.Receipt;
        public ItemDiscardRejection Rejection => Discard.Rejection;
    }
}
