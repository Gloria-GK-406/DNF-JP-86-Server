using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.OnlineUsers;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Sessions;
using Dfo.Server.Protocol.Game.InitialConnection;
using Dfo.Server.Protocol.Game.StateSynchronization;

namespace Dfo.Server.Protocol.Game;

public sealed class OnlineUserConnectionObserver(
    IOnlineUserDomainService onlineUsers,
    OnlineUserExecutionGate executionGate,
    SelectedSessionTransitionGate lifecycleGate,
    ISelectedSessionDomainService selectedSessions,
    ISelectedSessionCleanupDomainService cleanup,
    IChannelEntryPolicy channels,
    IServerConfigService serverConfig,
    IGameMessageSender gameSender,
    ICleanupNotificationDelivery cleanupDelivery,
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
        if (connection.EndpointKind != NetworkEndpointKind.Game)
        {
            return;
        }

        if (!onlineUsers.TryGetByConnection(connection.ConnectionId, out var observed)
            || observed is null)
        {
            // Protocol rejection removes an unusable OnlineUser identity immediately. Its
            // asynchronously requested transport close can arrive here later, while the selected
            // directory still owns the exact session whose other resources need full cleanup.
            if (selectedSessions.FindByConnection(connection.ConnectionId) is { } orphaned)
            {
                _ = await CleanupSelectedAsync(
                    connection.ConnectionId, orphaned, cancellationToken).ConfigureAwait(false);
            }
            return;
        }

        while (true)
        {
            if (!onlineUsers.TryGetByConnection(connection.ConnectionId, out var before)
                || before is null || before.OnlineUserId != observed.OnlineUserId) return;
            var selectedBefore = selectedSessions.FindByConnection(connection.ConnectionId);
            var accountId = selectedBefore?.AccountId;
            // Transition first, then online. Scope commands will hold the same order. If binding
            // changed while waiting, release both and retry rather than acquire in reverse.
            await using var lifecycleLease = accountId is { } account
                ? await lifecycleGate.AcquireAsync(account, UserAccessMode.Exclusive, cancellationToken)
                    .ConfigureAwait(false)
                : null;
            await using var lease = await executionGate.AcquireAsync(
                observed.OnlineUserId, UserAccessMode.Exclusive, cancellationToken).ConfigureAwait(false);
            if (!onlineUsers.TryGetByConnection(connection.ConnectionId, out var current)
                || current is null || current.OnlineUserId != observed.OnlineUserId) return;
            var selected = selectedSessions.FindByConnection(connection.ConnectionId);
            if (selectedBefore is null && selected is not null) continue;
            var cleanedSelected = selectedBefore is not null
                && await CleanupSelectedHeldAsync(connection.ConnectionId, selectedBefore).ConfigureAwait(false);
            if (onlineUsers.TryGetByConnection(connection.ConnectionId, out current)
                && current is not null && current.OnlineUserId == observed.OnlineUserId
                && (cleanedSelected || current.Generation == observed.Generation))
                onlineUsers.TryDisconnectConnection(connection.ConnectionId);
            return;
        }
    }

    private async ValueTask<bool> CleanupSelectedAsync(
        NetworkConnectionId connectionId,
        SelectedSessionIdentity selected,
        CancellationToken cancellationToken)
    {
        await using var lifecycleLease = await lifecycleGate.AcquireAsync(
            selected.AccountId, UserAccessMode.Exclusive, cancellationToken).ConfigureAwait(false);
        return await CleanupSelectedHeldAsync(connectionId, selected).ConfigureAwait(false);
    }

    private async ValueTask<bool> CleanupSelectedHeldAsync(
        NetworkConnectionId connectionId, SelectedSessionIdentity selected)
    {
        // A command may have completed exact cleanup while this observer waited. Never clean a
        // replacement selected by the same account or a reused connection identifier.
        if (selectedSessions.FindByConnection(connectionId) != selected)
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
