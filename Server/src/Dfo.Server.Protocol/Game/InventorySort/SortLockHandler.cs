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

namespace Dfo.Server.Protocol.Game.InventorySort;

public sealed class SortLockHandler(ICommandBus commands) : ClientMessageHandler<SortLockRequest>, IProtocolCommandMessageHandler<GameSortLockCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, SortLockRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameSortLockCommand(context, message), ct).ConfigureAwait(false);
}

public sealed record SortLockChanged(SortLockReceipt Receipt) : INotification<SortLockChanged>;
public sealed class SortLockChangedHandler(ISelectedSessionDomainService sessions) : INotificationHandler<SortLockChanged>
{
    public ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(NotificationRecipient recipient,
        SortLockChanged notification, CancellationToken ct)
    {
        var receipt = notification.Receipt;
        return ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>(
            recipient.CharacterId == receipt.Session.CharacterId && recipient.ConnectionId == receipt.Session.ConnectionId
            && sessions.IsInWorld(receipt.Session)
                ? [new SortLockResponse(receipt.Slot, receipt.Locked)] : []);
    }
}
