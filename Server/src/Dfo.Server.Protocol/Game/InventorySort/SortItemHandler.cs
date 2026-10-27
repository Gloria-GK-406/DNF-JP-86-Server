using Dfo.Server.Application.Dispatching;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.InventorySort;

public sealed class SortItemHandler(ICommandBus commands) : ClientMessageHandler<SortItemRequest>, IProtocolCommandMessageHandler<GameSortItemCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, SortItemRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameSortItemCommand(context, message), ct).ConfigureAwait(false);
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
            || !sessions.IsInWorld(receipt.Session))
            return ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>([]);
        var container = notification.ContainerType == InventoryContainerType.AccountCargo ? receipt.Account.Cargo.Container
            : receipt.Character.Containers.Single(value => value.ContainerType == notification.ContainerType);
        return ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>([
            new SortItemResponse((byte)notification.ContainerType, true),
            new InventoryFullSnapshotMessage(container, receipt.Character, receipt.Account, receipt.PreparedAt),
            new InventoryLocksMessage(receipt.Character.EquipmentLocks, receipt.Account.Cargo.EquipmentLocks)]);
    }
}
