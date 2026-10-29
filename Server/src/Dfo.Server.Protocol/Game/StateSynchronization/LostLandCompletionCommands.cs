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

internal sealed record CaptureLostLandCompletionsCommand : ICommand<IReadOnlyList<LostLandCompletion>>;
internal sealed record CompleteLostLandCommand(LostLandCompletion Completion) : ICommand<bool>;

internal sealed class LostLandCompletionCommandHandler(IDungeonRunDomainService runs,
    IDungeonRunProjectionDomainService registry, ISelectedSessionDomainService sessions,
    IOnlineUserQueryDomainService users, ICommandWorkUnits units, IGameCommandOutbox outbox,
    TimeProvider clock, ILogger<LostLandCompletionCommandHandler> logger)
    : ICommandHandler<CaptureLostLandCompletionsCommand, IReadOnlyList<LostLandCompletion>>,
        ICommandHandler<CompleteLostLandCommand, bool>
{
    public ValueTask<IReadOnlyList<LostLandCompletion>> HandleAsync(CommandExecutionContext context,
        CaptureLostLandCompletionsCommand command, CancellationToken token) =>
        ValueTask.FromResult(runs.FindDueLostLandCompletions(clock.GetUtcNow()));

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

internal sealed class LostLandCompletionMaintenanceService(ICommandBus commands, TimeProvider clock,
    ILogger<LostLandCompletionMaintenanceService> logger) : TypedGameplayTimerSource(commands, logger)
{
    protected override string SourceName => "timer:LostLandCompletionMaintenanceService";
    protected override async Task RunRoundAsync(GameplayCallChain root, CancellationToken token)
    {
        var due = await SubmitAsync(new CaptureLostLandCompletionsCommand(), root, SourceName, token).ConfigureAwait(false);
        foreach (var completion in due)
            await ItemAsync(new CompleteLostLandCommand(completion), root, SourceName + "/run", token).ConfigureAwait(false);
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250), clock);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
                await HostedAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
