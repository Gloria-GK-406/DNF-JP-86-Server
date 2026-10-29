using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Execution;
using Dfo.Server.Protocol.Game.Dungeons;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.StateSynchronization;

internal sealed record CaptureLostLandFeverExpiriesCommand : ICommand<IReadOnlyList<LostLandFeverExpiry>>;
internal sealed record ExpireLostLandFeverCommand(LostLandFeverExpiry Expiry) : ICommand<bool>;

internal sealed class LostLandFeverCommandHandler(IDungeonRunDomainService runs,
    IDungeonRunProjectionDomainService registry, ISelectedSessionDomainService sessions,
    IOnlineUserQueryDomainService users, ICommandWorkUnits units, IGameCommandOutbox outbox,
    TimeProvider clock, ILogger<LostLandFeverCommandHandler> logger)
    : ICommandHandler<CaptureLostLandFeverExpiriesCommand, IReadOnlyList<LostLandFeverExpiry>>,
        ICommandHandler<ExpireLostLandFeverCommand, bool>
{
    public ValueTask<IReadOnlyList<LostLandFeverExpiry>> HandleAsync(CommandExecutionContext context,
        CaptureLostLandFeverExpiriesCommand command, CancellationToken token) =>
        ValueTask.FromResult(runs.FindExpiredLostLandFevers(clock.GetUtcNow()));

    public async ValueTask<bool> HandleAsync(CommandExecutionContext context, ExpireLostLandFeverCommand command,
        CancellationToken token)
    {
        var expiry = command.Expiry;
        var recipients = registry.Find(expiry.RunId).Where(session => sessions.IsInWorld(session)
            && runs.Find(session.CharacterId)?.RunId == expiry.RunId).ToArray();
        var accepted = await units.WriteAsync(context, new("lost-land.fever-expiry",
            recipients.Select(session => GameParticipantCapture.Capture(users, session)).ToArray()),
            (_, _) => ValueTask.FromResult(runs.StageLostLandFeverExpiry(expiry, clock.GetUtcNow())), token)
            .ConfigureAwait(false);
        if (!accepted) return false;
        foreach (var session in recipients)
        {
            var current = runs.Find(session.CharacterId);
            if (sessions.IsInWorld(session) && current?.RunId == expiry.RunId
                && current.ReadyParticipants.Contains(session.CharacterId))
                await outbox.RegisterNotiAsync(session.ConnectionId, new DungeonFeverGaugeMessage(0), token)
                    .ConfigureAwait(false);
        }
        logger.LogDebug("LostLand fever expired run={RunId} deadline={Deadline}", expiry.RunId, expiry.ActiveUntil);
        return true;
    }
}

internal sealed class LostLandFeverMaintenanceService(ICommandBus commands, TimeProvider clock,
    ILogger<LostLandFeverMaintenanceService> logger) : TypedGameplayTimerSource(commands, logger)
{
    protected override string SourceName => "timer:LostLandFeverMaintenanceService";
    protected override async Task RunRoundAsync(GameplayCallChain root, CancellationToken token)
    {
        var due = await SubmitAsync(new CaptureLostLandFeverExpiriesCommand(), root, SourceName, token)
            .ConfigureAwait(false);
        foreach (var expiry in due)
            await ItemAsync(new ExpireLostLandFeverCommand(expiry), root, SourceName + "/run", token)
                .ConfigureAwait(false);
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500), clock);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
                await HostedAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
