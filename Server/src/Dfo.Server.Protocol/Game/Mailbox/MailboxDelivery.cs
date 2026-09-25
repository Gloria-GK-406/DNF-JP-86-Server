using Dfo.Server.Abstractions.Mail;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.OnlineUsers;
using Dfo.Server.Data.Mail;
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
            || sessions.FindByCharacter(notice.Session.CharacterId) != notice.Session)
            return ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>([]);
        var frames = new List<IServerNetworkMessage>();
        // Send ACK updates local send state; apply authoritative assets afterward.
        var sending = notice.Reply is MailboxSendResponse;
        if (sending) frames.Add(notice.Reply);
        if (notice.Result.Assets is { } assets)
        {
            frames.AddRange(assets.Updates.Select(GameInventoryDelivery.Notification));
            frames.AddRange(assets.Cleared.Select(x => GameInventoryDelivery.ClearedSlotNotification(x.Container, x.Slot, assets.PreparedAt)));
            if (assets.Materials.Count > 0) frames.Add(GameInventoryDelivery.VirtualResourceNotification(assets.Materials, assets.PreparedAt));
            if (assets.Gold is { } gold) frames.Add(GameInventoryDelivery.CarriedGoldNotification(gold, assets.PreparedAt));
        }
        if (!sending) frames.Add(notice.Reply);
        if (notice.Result.Inbox is { } inbox) frames.Add(new MailboxStateMessage(inbox));
        return ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>(frames);
    }
}
public sealed class MailboxDelivery(ISelectedSessionDomainService sessions, IOnlineUserQueryDomainService users,
    SelectedSessionTransitionGate transitions, ICommandBus commands, INotificationBus notifications,
    IGameMessageSender sender, ILogger<MailboxDelivery> logger)
{
    public async ValueTask HandleAsync(NetworkDispatchContext context, MailboxOperation operation, uint wireId, ushort action, byte[]? name, CancellationToken token, MailboxSendRequest? send = null)
    {
        var connection = context.Connection.ConnectionId;
        var session = sessions.FindByConnection(connection);
        if (session is null) return;
        await using var lease = await transitions.AcquireAsync(session.AccountId, UserAccessMode.Exclusive, token);
        if (sessions.FindByConnection(connection) != session || !users.TryGetByConnection(connection, out var user) || user is null) return;
        MailboxResult result = new(); byte error = 0;
        try
        {
            if (operation is MailboxOperation.Read && action is not (0 or 2)) throw new MailException(MailFailure.InvalidMail, "Unsupported mail state action.");
            var actual = operation == MailboxOperation.Read && action == 0 ? MailboxOperation.Delete : operation;
            var id = operation == MailboxOperation.Claim ? MailboxIds.DecodeAttachment(wireId) : wireId;
            result = await commands.SendAsync(new MailboxCommand(user.OnlineUserId, session, actual, id, name, send?.Mail), token);
            if (operation == MailboxOperation.Query && result.Recipient is null) error = 21;
        }
        catch (MailException e)
        {
            error = (byte)(operation == MailboxOperation.Send ? e.Reason switch
            { MailFailure.CharacterNotFound => 8, MailFailure.InsufficientAssets => 10, MailFailure.InvalidMail => 7, _ => 3 } : e.Reason switch { MailFailure.NoCapacity => 4, MailFailure.GoldLimit => 10, MailFailure.InvalidMail when operation == MailboxOperation.Query => 7, _ => 21 });
            logger.LogInformation("Mailbox rejected character={Character} operation={Operation} id={Id} reason={Reason}", session.CharacterId, operation, wireId, e.Reason);
        }
        catch (InvalidDataException) { error = operation == MailboxOperation.Send ? (byte)3 : (byte)21; }
        catch (Exception e) when (e is not OperationCanceledException)
        { error = operation == MailboxOperation.Send ? (byte)3 : (byte)21; logger.LogError(e, "Mailbox failed character={Character} operation={Operation}", session.CharacterId, operation); }
        IGameCmdMessage reply = operation switch
        {
            MailboxOperation.Send => new MailboxSendResponse(send?.Multi ?? false, error),
            MailboxOperation.Claim => new MailboxClaimResponse(wireId, error),
            MailboxOperation.Read => new MailboxActionResponse(wireId, action, error),
            MailboxOperation.Query => new MailboxQueryResponse(result.Recipient, error),
            _ => new MailboxOpenResponse(checked((ushort)Math.Min(result.Inbox?.NotLoadedCount ?? 0, short.MaxValue)), error)
        };
        logger.LogInformation("Mailbox character={Character} operation={Operation} id={Id} action={Action} error={Error}", session.CharacterId, operation, wireId, action, error);
        if (error != 0) await sender.SendCmdAsync(connection, reply, token);
        else
        {
            await notifications.PublishAsync(new NotificationAudience.TheseCharacters([session.CharacterId]), new MailboxCompleted(session, reply, result), token);
            if (result.SentTo is { } target)
                await notifications.PublishAsync(new NotificationAudience.TheseCharacters([target]), new MailboxArrived(target, result.MailCount), token);
        }
    }
}
public sealed class MailboxClaimHandler(MailboxDelivery delivery) : ClientMessageHandler<MailboxClaimRequest>
{ protected override ValueTask HandleMessageAsync(NetworkDispatchContext c, MailboxClaimRequest m, CancellationToken t) => delivery.HandleAsync(c, MailboxOperation.Claim, m.AttachmentId, 0, null, t); }
public sealed class MailboxActionHandler(MailboxDelivery delivery) : ClientMessageHandler<MailboxActionRequest>
{ protected override ValueTask HandleMessageAsync(NetworkDispatchContext c, MailboxActionRequest m, CancellationToken t) => delivery.HandleAsync(c, MailboxOperation.Read, m.MailId, m.Action, null, t); }
public sealed class MailboxOpenHandler(MailboxDelivery delivery) : ClientMessageHandler<MailboxOpenRequest>
{ protected override ValueTask HandleMessageAsync(NetworkDispatchContext c, MailboxOpenRequest m, CancellationToken t) => delivery.HandleAsync(c, MailboxOperation.Open, 0, 0, null, t); }
public sealed class MailboxQueryHandler(MailboxDelivery delivery) : ClientMessageHandler<MailboxQueryRequest>
{ protected override ValueTask HandleMessageAsync(NetworkDispatchContext c, MailboxQueryRequest m, CancellationToken t) => delivery.HandleAsync(c, MailboxOperation.Query, 0, 0, m.Name, t); }

public sealed record MailboxArrived(Dfo.Server.Data.Characters.CharacterId Character, int Count) : INotification<MailboxArrived>;
public sealed class MailboxArrivedHandler : INotificationHandler<MailboxArrived>
{
    public ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(NotificationRecipient recipient, MailboxArrived notice, CancellationToken token) =>
        ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>(recipient.CharacterId == notice.Character
            ? [new MailboxAlarm((ushort)Math.Clamp(notice.Count, 0, ushort.MaxValue))] : []);
}
