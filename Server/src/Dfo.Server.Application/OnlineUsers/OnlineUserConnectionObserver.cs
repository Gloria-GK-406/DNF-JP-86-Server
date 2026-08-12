using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Sessions;

namespace Dfo.Server.Application.OnlineUsers;

internal sealed class OnlineUserConnectionObserver(
    IOnlineUserService onlineUsers,
    OnlineUserExecutionGate executionGate,
    SelectedSessionTransitionGate lifecycleGate,
    ISelectedSessionDirectory selectedSessions,
    ISelectedSessionCleanupService cleanup) : INetworkConnectionObserver
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
            UserAccessMode.Exclusive,
            cancellationToken).ConfigureAwait(false);
        var selected = selectedSessions.FindByConnection(connection.ConnectionId);
        var cleanedSelected = false;
        if (selected is not null)
        {
            await using var lifecycleLease = await lifecycleGate.AcquireAsync(
                selected.AccountId, UserAccessMode.Exclusive, cancellationToken).ConfigureAwait(false);
            // The command may have completed exact cleanup while this observer waited.
            // Never clean a replacement or a connection that no longer owns this identity.
            if (selectedSessions.FindByConnection(connection.ConnectionId) == selected)
            {
                cleanedSelected = true;
                try
                {
                    await cleanup.CleanupAsync(selected, includeOwner: false, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch
                {
                    // Cleanup is internally compensating and may aggregate individual failures.
                    // A closed transport still must release the matching online connection.
                }
            }
        }
        if (onlineUsers.TryGetByConnection(connection.ConnectionId, out var current)
            && current is not null && current.OnlineUserId == observed.OnlineUserId
            && (cleanedSelected || current.Generation == observed.Generation))
        {
            onlineUsers.TryDisconnectConnection(connection.ConnectionId);
        }
    }
}
