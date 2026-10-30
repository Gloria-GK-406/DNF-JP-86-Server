using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Protocol.Game.Dungeons;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Application.Execution;
using Dfo.Server.Application.World;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.Altar;

public sealed record MaintainAltarCommand : ICommand<AltarNextDeadline>;
public sealed record AdvanceAltarDeadlineCommand(AltarDeadline Deadline) : ICommand<bool>;
public sealed record AltarNextDeadline(DateTimeOffset? At)
{
    public IReadOnlyList<AltarDeadline> Due { get; init; } = [];
}
public sealed class AltarTimerCommandHandler(IAltarDungeonDomainService altars, IDungeonRunQueryDomainService runs,
    Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService sessions,
    Dfo.Server.Abstractions.Networking.IDungeonRunProjectionDomainService registry,
    Dfo.Server.Abstractions.OnlineUsers.IOnlineUserQueryDomainService users,
    ICommandWorkUnits units, Dfo.Server.Abstractions.DomainEvents.ICommandProjections projections,
    TimeProvider clock, ILogger<AltarTimerCommandHandler> logger)
    : ICommandHandler<MaintainAltarCommand, AltarNextDeadline>, ICommandHandler<AdvanceAltarDeadlineCommand, bool>
{
    public ValueTask<AltarNextDeadline> HandleAsync(CommandExecutionContext context, MaintainAltarCommand command, CancellationToken token)
    {
        var now = clock.GetUtcNow();
        var due = altars.Pending().Where(x => x.At <= now).ToArray();
        var pending = altars.Pending();
        var next = pending.Count == 0 ? (DateTimeOffset?)null : pending.Min(x => x.At);
        if (next is {} expired && expired <= clock.GetUtcNow()) next = clock.GetUtcNow().AddMilliseconds(100);
        return ValueTask.FromResult(new AltarNextDeadline(next) { Due = due });
    }
    public async ValueTask<bool> HandleAsync(CommandExecutionContext context, AdvanceAltarDeadlineCommand command, CancellationToken token)
    { await AdvanceAsync(context, command.Deadline, token).ConfigureAwait(false); return true; }
    private async ValueTask AdvanceAsync(CommandExecutionContext context, AltarDeadline due, CancellationToken token)
    {
        var cohort = AltarTransitionProjection.Participants(runs, sessions, registry, due.RunId);
        if (cohort.Count == 0) return;
        var current = altars.Find(due.RunId);
        if (current?.Generation != due.Generation || current.RoomVersion != due.RoomVersion || runs.Find(due.RunId)?.RoomVersion != due.RoomVersion) return;
        Exception? rejected = null;
        var write = await units.WriteResultAsync(context, new("altar.deadline", TimerRecipientCapture.Capture(cohort, users)), async (_, ct) =>
        {
            try { return new AltarCommandResult(await altars.AdvanceAsync(due.RunId, due.RoomVersion, due.Generation, clock.GetUtcNow(), ct).ConfigureAwait(false)); }
            catch (Exception error) when (error is not OperationCanceledException && !Dfo.Server.Abstractions.Transactions.TransactionOutcomeUncertainException.Contains(error))
            { rejected = error; _.RequireRollback(error); return new AltarCommandResult(null); }
        }, token).ConfigureAwait(false);
        if (write is CommandWriteResult<AltarCommandResult>.CommitRolledBack rolledBack) rejected = rolledBack.Cause;
        if (rejected is not null) { logger.LogError(rejected, "Altar deadline failed run={RunId} generation={Generation}", due.RunId, due.Generation); return; }
        var result = ((CommandWriteResult<AltarCommandResult>.Returned)write).Value;
        if (result.Action is {} action)
            await AltarTransitionProjection.ProjectAsync(projections, action, AltarTransitionProjection.Participants(runs, sessions, registry, action.State.RunId),
                clock, logger, token).ConfigureAwait(false);
    }

}
internal sealed class AltarDeadlineSource : DungeonDeadlineSource
{
    public override DungeonDeadlineKind Kind => DungeonDeadlineKind.Altar;
    public override async ValueTask<DateTimeOffset?> RunAsync(DungeonDeadlineRound round, CancellationToken token)
    {
        var scan = await round.SubmitAsync(new MaintainAltarCommand(), token).ConfigureAwait(false);
        foreach (var due in scan.Due)
            await round.ItemAsync(new AdvanceAltarDeadlineCommand(due), "deadline", token).ConfigureAwait(false);
        return (await round.SubmitAsync(new MaintainAltarCommand(), token).ConfigureAwait(false)).At;
    }
}
