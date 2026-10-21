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

public sealed class SortLockHandler(ISelectedSessionDomainService sessions, IOnlineUserQueryDomainService users,
    IInventorySlotMappingRuleService slots, ICommandBus commands, IEventService notifications)
    : ClientMessageHandler<SortLockRequest>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, SortLockRequest message,
        CancellationToken ct)
    {
        var session = sessions.FindByConnection(context.Connection.ConnectionId);
        if (session is null || !users.TryGetByConnection(context.Connection.ConnectionId, out var online)
            || online is null) return;
        BusinessSlot slot;
        try { slot = slots.ToBusinessSlot(message.Slot); }
        catch (InventorySlotMappingException) { return; }
        var result = await commands.SendAsync(new SortLockCommand(online.OnlineUserId, session, slot, message.Action), ct)
            .ConfigureAwait(false);
        if (result.Receipt is { } receipt)
            await notifications.ProjectAsync(new NotificationAudience.TheseCharacters([session.CharacterId]),
                new SortLockChanged(receipt), ct).ConfigureAwait(false);
    }
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
