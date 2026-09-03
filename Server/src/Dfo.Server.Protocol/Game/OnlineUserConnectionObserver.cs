using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.OnlineUsers;
using Dfo.Server.Data.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Sessions;
using Dfo.Server.Protocol.Game.InitialConnection;
using Dfo.Server.Protocol.Game.StateSynchronization;

namespace Dfo.Server.Protocol.Game;

public sealed class OnlineUserConnectionObserver(
    IOnlineUserDomainService onlineUsers,
    OnlineUserExecutionGate executionGate,
    SelectedSessionTransitionGate lifecycleGate,
    ISelectedSessionDirectory selectedSessions,
    ISelectedSessionCleanupDomainService cleanup,
    IChannelEntryPolicy channels,
    IServerConfigService serverConfig,
    IGameMessageSender gameSender,
    TimeProvider timeProvider) : INetworkConnectionObserver
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
                    var notifications = await cleanup.CleanupAsync(
                        selected, includeOwner: false, CancellationToken.None).ConfigureAwait(false);
                    await gameSender.SendExplicitAsync(
                        notifications.OwnerConnectionId,
                        CleanupSynchronizationDelivery.Create(notifications),
                        CancellationToken.None).ConfigureAwait(false);
                }
                catch (SelectedSessionCleanupFailure cleanupFailure)
                {
                    try
                    {
                        await gameSender.SendExplicitAsync(
                            cleanupFailure.NotificationPlan.OwnerConnectionId,
                            CleanupSynchronizationDelivery.Create(
                                cleanupFailure.NotificationPlan),
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
