using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Sessions;
using Dfo.Server.Protocol.Game.InitialConnection;
using Dfo.Server.Protocol.Game.StateSynchronization;

namespace Dfo.Server.Protocol.Game;

public sealed class OnlineUserConnectionObserver(
    IOnlineUserDomainService onlineUsers,
    ISelectedSessionDomainService selectedSessions,
    ISelectedSessionCleanupDomainService cleanup,
    IChannelEntryPolicy channels,
    IServerConfigService serverConfig,
    IGameMessageSender gameSender,
    ICleanupNotificationDelivery cleanupDelivery,
    TimeProvider timeProvider, Dfo.Server.Abstractions.Accounts.IClientSettingsDomainService? clientSettings = null) : INetworkConnectionObserver
{
    public async ValueTask ConnectedAsync(
        NetworkConnectionContext connection,
        CancellationToken cancellationToken)
    {
        if (connection.EndpointKind != NetworkEndpointKind.Game)
        {
            return;
        }

        var registered = onlineUsers.RegisterConnection(connection);
        try
        {
            var channel = await channels.ResolveAsync(connection.ListenerPort, cancellationToken)
                .ConfigureAwait(false);
            if (channel.ChannelId is < 1 or > byte.MaxValue)
            {
                throw new InvalidOperationException(
                    $"Game listener '{connection.ListenerPort}' does not resolve to a wire channel ID.");
            }

            await gameSender.SendNotiAsync(
                connection.ConnectionId,
                new GameInitialConnectionNotice(
                    channel.ChannelId,
                    timeProvider.GetUtcNow(),
                    serverConfig.GetConfig<string>(GameInitialConnectionConfigKeys.EndpointHost)),
                cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            onlineUsers.TryDisconnect(
                registered.OnlineUserId,
                connection.ConnectionId,
                registered.Generation);
            throw;
        }
    }

    public async ValueTask DisconnectedAsync(
        NetworkConnectionContext connection,
        CancellationToken cancellationToken)
    {
        if (connection.EndpointKind != NetworkEndpointKind.Game)
        {
            return;
        }

        clientSettings?.ForgetConnection(connection.ConnectionId);
        if (!onlineUsers.TryGetByConnection(connection.ConnectionId, out var observed)
            || observed is null)
        {
            // Protocol rejection removes an unusable OnlineUser identity immediately. Its
            // asynchronously requested transport close can arrive here later, while the session it
            // had selected still reads as revoked and its other resources need full cleanup.
            if (selectedSessions.FindByConnection(connection.ConnectionId) is { } orphaned)
            {
                _ = await CleanupSelectedAsync(orphaned).ConfigureAwait(false);
            }
            return;
        }

        var selected = selectedSessions.FindByConnection(connection.ConnectionId);
        var cleanedSelected = selected is not null
            && await CleanupSelectedAsync(selected).ConfigureAwait(false);
        if (onlineUsers.TryGetByConnection(connection.ConnectionId, out var current)
            && current is not null && current.OnlineUserId == observed.OnlineUserId
            && (cleanedSelected || current.Generation == observed.Generation))
        {
            onlineUsers.TryDisconnectConnection(connection.ConnectionId);
            // Remove again by the observed session identity as well, and never remove a
            // replacement connection identity.
            clientSettings?.ForgetConnection(observed.SessionIdentity);
        }
    }

    private async ValueTask<bool> CleanupSelectedAsync(SelectedSessionIdentity selected)
    {
        // A command may have completed exact cleanup before this observer ran. Never clean a
        // replacement selected by the same account or a reused connection identifier.
        if (selectedSessions.StandingOf(selected) is null)
        {
            return false;
        }

        try
        {
            var notifications = await cleanup.CleanupAsync(
                selected, includeOwner: false, CancellationToken.None).ConfigureAwait(false);
            await cleanupDelivery.DeliverAsync(
                notifications, CancellationToken.None).ConfigureAwait(false);
        }
        catch (SelectedSessionCleanupFailure cleanupFailure)
        {
            try
            {
                await cleanupDelivery.DeliverAsync(
                    cleanupFailure.NotificationPlan,
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // The cleanup failure remains primary; peer delivery stays best effort.
            }
        }
        catch
        {
            // Cleanup is internally compensating and may aggregate individual failures.
            // A closed transport still must release the matching online connection.
        }

        return true;
    }
}
