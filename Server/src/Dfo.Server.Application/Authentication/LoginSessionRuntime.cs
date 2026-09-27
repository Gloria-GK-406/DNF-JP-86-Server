using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.Authentication;

internal sealed class LoginSessionConnectionObserver(LoginSessionRegistry sessions) : INetworkConnectionObserver
{
    public ValueTask ConnectedAsync(NetworkConnectionContext connection, CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;

    public ValueTask DisconnectedAsync(NetworkConnectionContext connection, CancellationToken cancellationToken)
    {
        if (connection.EndpointKind == NetworkEndpointKind.Game) sessions.Disconnected(connection.ConnectionId);
        return ValueTask.CompletedTask;
    }
}

/// <summary>One expiry sweep per process, plus immediate close requests after committed handoffs.</summary>
internal sealed class LoginSessionMaintenanceService(
    LoginSessionRegistry sessions,
    IOnlineUserDomainService onlineUsers,
    INetworkConnectionCloser closer,
    TimeProvider clock,
    ILogger<LoginSessionMaintenanceService> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(SweepAsync(stoppingToken), CloseSupersededAsync(stoppingToken));

    private async Task SweepAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30), clock);
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            sessions.SweepExpired();
    }

    private async Task CloseSupersededAsync(CancellationToken cancellationToken)
    {
        await foreach (var connection in sessions.CloseRequests.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            // Selected-session cleanup remains in the existing disconnect observer, including its
            // orphan path. Removing the online identity prevents further character operations now.
            onlineUsers.TryDisconnectConnection(connection);
            try
            {
                await closer.RequestCloseAsync(connection, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return; }
            catch (Exception error)
            {
                logger.LogWarning(error, "Could not close a superseded game connection {ConnectionId}", connection);
            }
        }
    }
}
