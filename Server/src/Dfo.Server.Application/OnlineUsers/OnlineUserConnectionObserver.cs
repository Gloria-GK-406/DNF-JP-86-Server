using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Application.OnlineUsers;

internal sealed class OnlineUserConnectionObserver(
    IOnlineUserService onlineUsers,
    OnlineUserExecutionGate executionGate) : INetworkConnectionObserver
{
    public ValueTask ConnectedAsync(
        NetworkConnectionContext connection,
        CancellationToken cancellationToken)
    {
        if (connection.EndpointKind == NetworkEndpointKind.Game)
        {
            onlineUsers.RegisterConnection(connection);
        }

        return ValueTask.CompletedTask;
    }

    public async ValueTask DisconnectedAsync(
        NetworkConnectionContext connection,
        CancellationToken cancellationToken)
    {
        if (connection.EndpointKind != NetworkEndpointKind.Game
            || !onlineUsers.TryGetByConnection(connection.ConnectionId, out var observed)
            || observed is null)
        {
            return;
        }

        await using var lease = await executionGate.AcquireAsync(
            observed.OnlineUserId,
            OnlineUserAccessMode.Exclusive,
            cancellationToken).ConfigureAwait(false);
        if (onlineUsers.TryGetByConnection(connection.ConnectionId, out var current)
            && current is not null
            && current.OnlineUserId == observed.OnlineUserId
            && current.Generation == observed.Generation)
        {
            onlineUsers.TryDisconnect(
                current.OnlineUserId,
                connection.ConnectionId,
                observed.Generation);
        }
    }
}
