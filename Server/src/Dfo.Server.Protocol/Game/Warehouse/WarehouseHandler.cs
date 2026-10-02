using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.Warehouse;

public sealed class WarehouseHandler(IOnlineUserQueryDomainService users, ISelectedSessionDomainService sessions,
    ICommandBus commands, INotificationBus notifications, IGameMessageSender sender, ILogger<WarehouseHandler> logger,
    IInventorySlotMappingRuleService mappings)
    : ClientMessageHandler<WarehouseRequest>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, WarehouseRequest message, CancellationToken ct)
    {
        var session = sessions.FindByConnection(context.Connection.ConnectionId);
        var result = new InventoryExpansionResult(InventoryExpansionRejection.NoSession);
        if (session is not null && users.TryGetByConnection(context.Connection.ConnectionId, out var user) && user is not null)
        {
            try
            {
                BusinessSlot? slot = message.Metadata.Type is 0x0198 or 0x01AF
                    ? mappings.ToBusinessSlot(new(InventoryContainerType.Main, message.Value)) : null;
                result = await commands.SendAsync(new WarehouseCommand(user.OnlineUserId, session, message.Metadata.Type, message.Value, slot), ct).ConfigureAwait(false);
            }
            catch (InventorySlotMappingException) { result = new(InventoryExpansionRejection.InvalidRequest); }
            catch (InventoryExpansionException error) { result = new(error.Rejection); }
            catch (Exception error) when (error is not OperationCanceledException
                && !Dfo.Server.Abstractions.Transactions.TransactionOutcomeUncertainException.Contains(error))
            { logger.LogError(error, "Warehouse {Type:X4} failed for character {Character}", message.Metadata.Type, session.CharacterId); result = new(InventoryExpansionRejection.StateChanged); }
        }
        if (result.Receipt is { } receipt)
            await notifications.PublishAsync(new NotificationAudience.TheseCharacters([receipt.Session.CharacterId]),
                new WarehouseChanged(message.Metadata.Type, receipt), ct).ConfigureAwait(false);
        else
        {
            logger.LogInformation("Warehouse {Type:X4} refused: {Reason}", message.Metadata.Type, result.Rejection);
            // A21 has no CMD response handler for 0x01AF; capacity is refreshed by NOTI 0x000D.
            if (message.Metadata.Type != 0x01AF)
                await sender.SendCmdAsync(context.Connection.ConnectionId,
                    new WarehouseResponse(message.Metadata.Type, false, ErrorCode(message.Metadata.Type, result.Rejection)), ct).ConfigureAwait(false);
        }
    }
    internal static byte ErrorCode(ushort type, InventoryExpansionRejection reason) => type switch
    {
        0x0133 => reason == InventoryExpansionRejection.GoldLimit ? (byte)95 : (byte)10,
        0x0134 => reason == InventoryExpansionRejection.GoldLimit ? (byte)94 : (byte)10,
        0x0131 or 0x0132 => reason == InventoryExpansionRejection.InsufficientAssets ? (byte)22 : (byte)19,
        0x0198 => 19,
        _ => 1,
    };
}
public sealed record WarehouseChanged(ushort Type, InventoryExpansionReceipt Receipt) : INotification<WarehouseChanged>;
public sealed class WarehouseChangedHandler(ISelectedSessionDomainService sessions) : INotificationHandler<WarehouseChanged>
{
    public ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(NotificationRecipient recipient, WarehouseChanged notification, CancellationToken ct)
    {
        var receipt = notification.Receipt;
        if (recipient.CharacterId != receipt.Session.CharacterId || recipient.ConnectionId != receipt.Session.ConnectionId
            || sessions.Find(receipt.Session.AccountId) != receipt.Session)
            return ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>([]);
        var result = new List<IServerNetworkMessage>();
        if (notification.Type != 0x01AF)
            result.Add(new WarehouseResponse(notification.Type, true, Gold: receipt.Account.Cargo.Value32));
        result.Add(new InventoryFullSnapshotMessage(receipt.Character.Containers.Single(c => c.ContainerType == InventoryContainerType.Main),
            receipt.Character, receipt.Account, receipt.At));
        if (notification.Type != 0x01AF)
        {
            var container = notification.Type == 0x0198
                ? receipt.Character.Containers.Single(c => c.ContainerType == InventoryContainerType.PersonalCargo)
                : receipt.Account.Cargo.Container;
            result.Add(new InventoryFullSnapshotMessage(container, receipt.Character, receipt.Account, receipt.At));
        }
        return ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>(result);
    }
}
