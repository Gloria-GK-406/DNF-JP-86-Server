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

internal sealed record LostLandCompletionSchedule(IReadOnlyList<LostLandCompletion> Due, DateTimeOffset? Next);
internal sealed record CaptureLostLandCompletionsCommand : ICommand<LostLandCompletionSchedule>;
internal sealed record CompleteLostLandCommand(LostLandCompletion Completion) : ICommand<bool>;

internal sealed class LostLandCompletionCommandHandler(IDungeonRunTimerDomainService runs,
    IDungeonRunProjectionDomainService registry, ISelectedSessionDomainService sessions,
    IOnlineUserQueryDomainService users, ICommandWorkUnits units, IGameCommandOutbox outbox,
    TimeProvider clock, ILogger<LostLandCompletionCommandHandler> logger)
    : ICommandHandler<CaptureLostLandCompletionsCommand, LostLandCompletionSchedule>,
        ICommandHandler<CompleteLostLandCommand, bool>
{
    public ValueTask<LostLandCompletionSchedule> HandleAsync(CommandExecutionContext context,
        CaptureLostLandCompletionsCommand command, CancellationToken token) =>
        ValueTask.FromResult(new LostLandCompletionSchedule(runs.FindDueLostLandCompletions(clock.GetUtcNow()), runs.NextLostLandCompletionAt()));

    public async ValueTask<bool> HandleAsync(CommandExecutionContext context, CompleteLostLandCommand command,
        CancellationToken token)
    {
        var completion = command.Completion;
        var recipients = registry.Find(completion.RunId).Where(session => sessions.IsInWorld(session)
            && runs.Find(session.CharacterId) is { } current && current.RunId == completion.RunId
            && current.ReadyParticipants.Contains(session.CharacterId)).ToArray();
        var accepted = await units.WriteAsync(context, new("lost-land.completion",
            recipients.Select(session => GameParticipantCapture.Capture(users, session)).ToArray()),
            (_, _) => ValueTask.FromResult(runs.StageLostLandCompletion(completion, clock.GetUtcNow(),
                notifyReady: recipients.Length > 0)), token).ConfigureAwait(false);
        if (!accepted) return false;
        // A room still loading is closed now; its summary notification waits for FINISH_LOADING.
        foreach (var session in recipients)
            if (sessions.IsInWorld(session) && runs.Find(session.CharacterId)?.RunId == completion.RunId)
                await outbox.RegisterNotiAsync(session.ConnectionId, DungeonRoomTransitionProjection.CompleteLostLand(), token)
                    .ConfigureAwait(false);
        logger.LogInformation("LostLand exploration completed run={RunId} deadline={Deadline} notified={Recipients}",
            completion.RunId, completion.EndsAt, recipients.Length);
        return true;
    }
}

internal sealed class LostLandCompletionDeadlineSource(TimeProvider clock) : DungeonDeadlineSource
{
    // A due item its command could not settle stays due; it is retried at this pace, not at the scheduler's floor.
    private static readonly TimeSpan Retry = TimeSpan.FromMilliseconds(250);
    public override DungeonDeadlineKind Kind => DungeonDeadlineKind.LostLandCompletion;
    public override async ValueTask<DateTimeOffset?> RunAsync(DungeonDeadlineRound round, CancellationToken token)
    {
        var scan = await round.SubmitAsync(new CaptureLostLandCompletionsCommand(), token).ConfigureAwait(false);
        foreach (var due in scan.Due)
            await round.ItemAsync(new CompleteLostLandCommand(due), "run", token).ConfigureAwait(false);
        var next = (await round.SubmitAsync(new CaptureLostLandCompletionsCommand(), token).ConfigureAwait(false)).Next;
        var now = clock.GetUtcNow();
        return next <= now ? now + Retry : next;
    }
}
