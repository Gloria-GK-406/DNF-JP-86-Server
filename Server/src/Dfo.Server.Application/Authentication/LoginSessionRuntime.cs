using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Execution;
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
/// <remarks>
/// Both loops are producers: each sweep is a timer CMD and each close request a lifecycle CMD on the
/// gameplay executor (spec appendix A row 4); the loops only read their trigger and wait.
/// </remarks>
internal sealed class LoginSessionMaintenanceService(
    LoginSessionRegistry sessions,
    IOnlineUserDomainService onlineUsers,
    INetworkConnectionCloser closer,
    TimeProvider clock,
    IGameplayQueue queue,
    ILogger<LoginSessionMaintenanceService> logger) : BackgroundService
{
    private readonly CoalescingGameplaySource sweeps = new(queue);

    internal TimeSpan SweepInterval { get; init; } = TimeSpan.FromSeconds(30);

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(SweepAsync(stoppingToken), CloseSupersededAsync(stoppingToken));

    private async Task SweepAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(SweepInterval, clock);
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            await sweeps.TryRun(new GameplayWork(
                GameplayShardKey.World,
                GameplayCallChain.NewRoot(GameplayCallSource.Timer),
                "timer:LoginSessionMaintenanceService",
                _ =>
                {
                    sessions.SweepExpired();
                    return ValueTask.CompletedTask;
                }), cancellationToken).Completion.ConfigureAwait(false);
        }
    }

    private async Task CloseSupersededAsync(CancellationToken cancellationToken)
    {
        await foreach (var connection in sessions.CloseRequests.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            try
            {
                // A new root until the login CMD's outbox carries this close (T-006, T-007).
                await queue.RunAsync(new GameplayWork(
                    GameplayShardKey.World,
                    GameplayCallChain.NewRoot(GameplayCallSource.Lifecycle, connection),
                    "lifecycle:close-superseded",
                    async token =>
                    {
                        // Selected-session cleanup remains in the existing disconnect observer, including its
                        // orphan path. Removing the online identity prevents further character operations now.
                        onlineUsers.TryDisconnectConnection(connection);
                        await closer.RequestCloseAsync(connection, token).ConfigureAwait(false);
                    }), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return; }
            catch (Exception error)
            {
                logger.LogWarning(error, "Could not close a superseded game connection {ConnectionId}", connection);
            }
        }
    }
}
