using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.ServerBroadcast;

/// <summary>One announcement line, built once, handed to every recipient as it is.</summary>
/// <param name="Line">A <see cref="ServerBroadcastMessage"/>, or a NOTI <c>0x01F7</c> system line.</param>
public sealed record ServerBroadcastPosted(IGameNotiMessage Line) : INotification<ServerBroadcastPosted>;

/// <summary>Reads nothing: who receives the line was decided when it was posted.</summary>
public sealed class ServerBroadcastPostedHandler : INotificationHandler<ServerBroadcastPosted>
{
    public ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(NotificationRecipient recipient,
        ServerBroadcastPosted notification, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>([notification.Line]);
}
