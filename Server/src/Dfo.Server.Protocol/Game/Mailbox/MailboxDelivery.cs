using Dfo.Server.Application.Dispatching;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Mail;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Mail;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Game;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Microsoft.Extensions.Logging;
namespace Dfo.Server.Protocol.Game.Mailbox;

public sealed record MailboxCompleted(SelectedSessionIdentity Session, IGameCmdMessage Reply, MailboxResult Result) : INotification<MailboxCompleted>;
public sealed class MailboxCompletedHandler(ISelectedSessionDomainService sessions) : INotificationHandler<MailboxCompleted>
{
    public ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(NotificationRecipient recipient, MailboxCompleted notice, CancellationToken token)
    {
        if (recipient.CharacterId != notice.Session.CharacterId || recipient.ConnectionId != notice.Session.ConnectionId
            || !sessions.IsInWorld(notice.Session))
            return ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>([]);
        var frames = new List<IServerNetworkMessage>();
        // Send ACK updates local send state; apply authoritative assets afterward.
        var sending = notice.Reply is MailboxSendResponse;
        if (sending) frames.Add(notice.Reply);
        if (notice.Result.Assets is { } assets)
        {
            frames.AddRange(assets.Updates.GroupBy(x => A21InventoryLayout.ToGameSlot(x.Slot).Space)
                .Select(group => GameInventoryDelivery.Notification(new CommittedInventoryUpdateState(group.Key,
                    group.Select(x => new InventoryItemState(x.Identity,
                        A21InventoryLayout.ToGameSlot(x.Slot).Index, x.Core, AvatarDetail: x.AvatarDetail)).ToArray(), assets.PreparedAt))));
            frames.AddRange(assets.Cleared.Select(x =>
            {
                var wire = A21InventoryLayout.ToGameSlot(x);
                return GameInventoryDelivery.ClearedSlotNotification(wire.Space, wire.Index, assets.PreparedAt);
            }));
            if (assets.Gold is { } gold) frames.Add(GameInventoryDelivery.CarriedGoldNotification(gold, assets.PreparedAt));
            if (assets.Pets is { } pets) frames.Add(GameInstanceDelivery.Creatures(pets));
        }
        if (!sending) frames.Add(notice.Reply);
        if (notice.Result.Inbox is { } inbox) frames.Add(new MailboxStateMessage(inbox));
        return ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>(frames);
    }
}
public sealed class MailboxClaimHandler(ICommandBus commands) : ClientMessageHandler<MailboxClaimRequest>, IProtocolCommandMessageHandler<GameMailboxCommand>
{ protected override async ValueTask HandleMessageAsync(NetworkDispatchContext c, MailboxClaimRequest m, CancellationToken t) =>
    _ = await commands.SendAsync(new GameMailboxCommand(c, m), t); }
public sealed class MailboxActionHandler(ICommandBus commands) : ClientMessageHandler<MailboxActionRequest>, IProtocolCommandMessageHandler<GameMailboxCommand>
{ protected override async ValueTask HandleMessageAsync(NetworkDispatchContext c, MailboxActionRequest m, CancellationToken t) =>
    _ = await commands.SendAsync(new GameMailboxCommand(c, m), t); }
public sealed class MailboxOpenHandler(ICommandBus commands) : ClientMessageHandler<MailboxOpenRequest>, IProtocolCommandMessageHandler<GameMailboxCommand>
{ protected override async ValueTask HandleMessageAsync(NetworkDispatchContext c, MailboxOpenRequest m, CancellationToken t) =>
    _ = await commands.SendAsync(new GameMailboxCommand(c, m), t); }
public sealed class MailboxQueryHandler(ICommandBus commands) : ClientMessageHandler<MailboxQueryRequest>, IProtocolCommandMessageHandler<GameMailboxCommand>
{ protected override async ValueTask HandleMessageAsync(NetworkDispatchContext c, MailboxQueryRequest m, CancellationToken t) =>
    _ = await commands.SendAsync(new GameMailboxCommand(c, m), t); }
