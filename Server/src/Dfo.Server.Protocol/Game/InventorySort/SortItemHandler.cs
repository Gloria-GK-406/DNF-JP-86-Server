using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.InventorySort;

public sealed class SortItemHandler(IOnlineUserQueryDomainService onlineUsers,
    ISelectedSessionDomainService sessions, ICommandBus commands, INotificationBus notifications,
    IGameMessageSender sender) : ClientMessageHandler<SortItemRequest>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context,
        SortItemRequest message, CancellationToken ct)
    {
        var session = sessions.FindByConnection(context.Connection.ConnectionId);
        SortItemReceipt? receipt = null;
        if (session is not null && onlineUsers.TryGetByConnection(context.Connection.ConnectionId, out var online)
            && online is not null && SortItemMapping.TryMap(message, out var space, out var order))
            receipt = (await commands.SendAsync(new SortItemCommand(online.OnlineUserId, session, space, order), ct)
                .ConfigureAwait(false)).Receipt;
        if (receipt is null)
            await sender.SendCmdAsync(context.Connection.ConnectionId, new SortItemResponse(message.ListType, false, 1), ct)
                .ConfigureAwait(false);
        else
            await notifications.PublishAsync(new NotificationAudience.TheseCharacters([receipt.Session.CharacterId]),
                new InventorySorted(receipt, (InventoryContainerType)message.ListType), ct).ConfigureAwait(false);
    }
}

public sealed record InventorySorted(SortItemReceipt Receipt, InventoryContainerType ContainerType)
    : INotification<InventorySorted>;
public sealed class InventorySortedHandler(ISelectedSessionDomainService sessions) : INotificationHandler<InventorySorted>
{
    public ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(NotificationRecipient recipient,
        InventorySorted notification, CancellationToken ct)
    {
        var receipt = notification.Receipt;
        if (recipient.CharacterId != receipt.Session.CharacterId || recipient.ConnectionId != receipt.Session.ConnectionId
            || sessions.Find(receipt.Session.AccountId) != receipt.Session)
            return ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>([]);
        var container = notification.ContainerType == InventoryContainerType.AccountCargo ? receipt.Account.Cargo.Container
            : receipt.Character.Containers.Single(value => value.ContainerType == notification.ContainerType);
        return ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>([
            new SortItemResponse((byte)notification.ContainerType, true),
            new InventoryFullSnapshotMessage(container, receipt.Character, receipt.Account, receipt.PreparedAt),
            new InventoryLocksMessage(receipt.Character.EquipmentLocks, receipt.Account.Cargo.EquipmentLocks)]);
    }
}
