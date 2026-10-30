using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Execution;
using Dfo.Server.Application.World;
using Dfo.Server.Protocol.Game.Dungeons;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.StateSynchronization;

internal sealed record LostLandFeverSchedule(IReadOnlyList<LostLandFeverExpiry> Due, DateTimeOffset? Next);
internal sealed record CaptureLostLandFeverExpiriesCommand : ICommand<LostLandFeverSchedule>;
internal sealed record ExpireLostLandFeverCommand(LostLandFeverExpiry Expiry) : ICommand<bool>;

internal sealed class LostLandFeverCommandHandler(IDungeonRunTimerDomainService runs,
    IDungeonRunProjectionDomainService registry, ISelectedSessionDomainService sessions,
    IOnlineUserQueryDomainService users, ICommandWorkUnits units, IGameCommandOutbox outbox,
    TimeProvider clock, ILogger<LostLandFeverCommandHandler> logger)
    : ICommandHandler<CaptureLostLandFeverExpiriesCommand, LostLandFeverSchedule>,
        ICommandHandler<ExpireLostLandFeverCommand, bool>
{
    public ValueTask<LostLandFeverSchedule> HandleAsync(CommandExecutionContext context,
        CaptureLostLandFeverExpiriesCommand command, CancellationToken token) =>
        ValueTask.FromResult(new LostLandFeverSchedule(runs.FindExpiredLostLandFevers(clock.GetUtcNow()), runs.NextLostLandFeverExpiryAt()));

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

internal sealed class LostLandFeverDeadlineSource(TimeProvider clock) : DungeonDeadlineSource
{
    // A due item its command could not settle stays due; it is retried at this pace, not at the scheduler's floor.
    private static readonly TimeSpan Retry = TimeSpan.FromMilliseconds(500);
    public override DungeonDeadlineKind Kind => DungeonDeadlineKind.LostLandFever;
    public override async ValueTask<DateTimeOffset?> RunAsync(DungeonDeadlineRound round, CancellationToken token)
    {
        var scan = await round.SubmitAsync(new CaptureLostLandFeverExpiriesCommand(), token).ConfigureAwait(false);
        foreach (var due in scan.Due)
            await round.ItemAsync(new ExpireLostLandFeverCommand(due), "run", token).ConfigureAwait(false);
        var next = (await round.SubmitAsync(new CaptureLostLandFeverExpiriesCommand(), token).ConfigureAwait(false)).Next;
        var now = clock.GetUtcNow();
        return next <= now ? now + Retry : next;
    }
}
