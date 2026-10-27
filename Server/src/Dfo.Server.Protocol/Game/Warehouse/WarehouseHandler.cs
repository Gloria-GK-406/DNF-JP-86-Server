using Dfo.Server.Application.Dispatching;
using Dfo.Server.Abstractions.DomainEvents;
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

public sealed class WarehouseHandler(ICommandBus commands) : ClientMessageHandler<WarehouseRequest>, IProtocolCommandMessageHandler<GameWarehouseCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, WarehouseRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameWarehouseCommand(context, message), ct).ConfigureAwait(false);
}
public sealed record WarehouseChanged(ushort Type, InventoryExpansionReceipt Receipt) : INotification<WarehouseChanged>;
public sealed class WarehouseChangedHandler(ISelectedSessionDomainService sessions) : INotificationHandler<WarehouseChanged>
{
    public ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(NotificationRecipient recipient, WarehouseChanged notification, CancellationToken ct)
    {
        var receipt = notification.Receipt;
        if (recipient.CharacterId != receipt.Session.CharacterId || recipient.ConnectionId != receipt.Session.ConnectionId
            || !sessions.IsInWorld(receipt.Session))
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
