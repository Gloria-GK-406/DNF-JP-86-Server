using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Execution;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.Authentication;

internal sealed class LoginSessionConnectionObserver(LoginSessionRegistry sessions) : Dfo.Server.Application.Commands.INetworkConnectionLifecycleHandler
{
    public ValueTask ConnectedAsync(Dfo.Server.Application.Commands.CommandExecutionContext context, NetworkConnectionContext connection, CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;

    public ValueTask DisconnectedAsync(Dfo.Server.Application.Commands.CommandExecutionContext context, NetworkConnectionContext connection, CancellationToken cancellationToken)
    {
        if (connection.EndpointKind == NetworkEndpointKind.Game) sessions.Disconnected(connection.ConnectionId);
        return ValueTask.CompletedTask;
    }
}

internal sealed class LoginSessionMaintenanceDomainService(LoginSessionRegistry sessions)
    : Dfo.Server.Abstractions.Authentication.ILoginSessionMaintenanceDomainService
{
    public void SweepExpired() => sessions.SweepExpired();
}
internal sealed record SweepLoginSessionsCommand : Dfo.Server.Application.Commands.ICommand<bool>;
internal sealed class SweepLoginSessionsCommandHandler(Dfo.Server.Abstractions.Authentication.ILoginSessionMaintenanceDomainService sessions)
    : Dfo.Server.Application.Commands.ICommandHandler<SweepLoginSessionsCommand, bool>
{
    public ValueTask<bool> HandleAsync(Dfo.Server.Application.Commands.CommandExecutionContext context, SweepLoginSessionsCommand command, CancellationToken token)
    { sessions.SweepExpired(); return ValueTask.FromResult(true); }
}
internal sealed class LoginSessionMaintenanceService(Dfo.Server.Application.Commands.ICommandBus commands,
    TimeProvider clock, ILogger<LoginSessionMaintenanceService> logger) : TypedGameplayTimerSource(commands, logger)
{
    internal TimeSpan SweepInterval { get; init; } = TimeSpan.FromSeconds(30);
    protected override string SourceName => "timer:LoginSessionMaintenanceService";
    protected override async Task RunRoundAsync(GameplayCallChain root, CancellationToken token) =>
        _ = await SubmitAsync(new SweepLoginSessionsCommand(), root, SourceName, token).ConfigureAwait(false);
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(SweepInterval, clock);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false)) await HostedAsync(stoppingToken).ConfigureAwait(false);
    }
}
