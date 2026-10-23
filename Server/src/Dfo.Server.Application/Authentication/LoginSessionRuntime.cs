using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Execution;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Hosting;

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

/// <summary>One expiry sweep per process.</summary>
/// <remarks>
/// The loop is a producer: each sweep is a timer CMD on the gameplay executor (spec appendix A row 4); the
/// loop only reads its trigger and waits. A superseded connection is closed by the CMD that superseded it
/// (<see cref="LoginSessionRegistry"/>).
/// </remarks>
internal sealed class LoginSessionMaintenanceService(
    LoginSessionRegistry sessions,
    TimeProvider clock,
    IGameplayQueue queue) : BackgroundService
{
    private readonly CoalescingGameplaySource sweeps = new(queue);

    internal TimeSpan SweepInterval { get; init; } = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(SweepInterval, clock);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            await sweeps.TryRun(new GameplayWork(
                GameplayShardKey.World,
                GameplayCallChain.NewRoot(GameplayCallSource.Timer),
                "timer:LoginSessionMaintenanceService",
                _ =>
                {
                    sessions.SweepExpired();
                    return ValueTask.CompletedTask;
                }), stoppingToken).Completion.ConfigureAwait(false);
        }
    }
}
