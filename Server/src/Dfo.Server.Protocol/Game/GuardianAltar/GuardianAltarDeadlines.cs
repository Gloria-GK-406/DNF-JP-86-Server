using System.Collections.Immutable;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Execution;
using Dfo.Server.Application.World;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.GuardianAltar;

public sealed record MaintainGuardianAltarCommand : ICommand<GuardianAltarSchedule>;
public sealed record AdvanceGuardianAltarCommand(GuardianAltarRunState Expected) : ICommand<bool>;
public sealed record ReturnGuardianAltarCommand(GuardianAltarRunState Expected) : ICommand<ImmutableArray<Dfo.Server.Data.World.SelectedSessionIdentity>>;
public sealed record GuardianAltarSchedule(DateTimeOffset? Next, IReadOnlyList<GuardianAltarRunState> Due);

internal sealed class GuardianAltarReturnCommandHandler(IGuardianAltarDomainService altar, IDungeonRunDepartureDomainService runs,
    ISelectedSessionDomainService sessions, IDungeonRunProjectionDomainService registry, TimeProvider clock,
    ISelectedCharacterCurrentViewDomainService currentViews, ICharacterStateProjectionDomainService currentState, IGameCommandOutbox outbox)
    : ICommandHandler<ReturnGuardianAltarCommand, ImmutableArray<Dfo.Server.Data.World.SelectedSessionIdentity>>
{
    public async ValueTask<ImmutableArray<Dfo.Server.Data.World.SelectedSessionIdentity>> HandleAsync(CommandExecutionContext context,
        ReturnGuardianAltarCommand command, CancellationToken ct)
    {
        var expected = command.Expected;
        if (!sessions.IsInWorld(expected.Session) || altar.Find(expected.RunId) is not { } current
            || current.Session != expected.Session || current.Generation != expected.Generation
            || current.Status is not (GuardianAltarStatus.Won or GuardianAltarStatus.Lost or GuardianAltarStatus.Abandoned)
            || current.Deadline is null || current.Deadline > clock.GetUtcNow()) return [];
        if (!await runs.EndAsync(expected.Session.CharacterId, expected.RunId).ConfigureAwait(false)) return [];
        registry.ParticipantDeparted(expected.RunId, expected.Session.CharacterId);
        await GuardianAltarProjection.RestoreInventoryAsync(expected.Session, sessions, currentViews, currentState, outbox, ct).ConfigureAwait(false);
        return [expected.Session];
    }
}

internal sealed class GuardianAltarTimerCommandHandler(IGuardianAltarDomainService altar, ISelectedSessionDomainService sessions,
    IOnlineUserQueryDomainService users, ICommandWorkUnits units, IDungeonRunDepartureDomainService runs, IGameCommandOutbox outbox, TimeProvider clock,
    ILogger<GuardianAltarTimerCommandHandler> logger) : ICommandHandler<MaintainGuardianAltarCommand, GuardianAltarSchedule>,
    ICommandHandler<AdvanceGuardianAltarCommand, bool>
{
    public async ValueTask<GuardianAltarSchedule> HandleAsync(CommandExecutionContext context, MaintainGuardianAltarCommand command, CancellationToken ct)
    {
        _ = await units.WriteAsync(context, new("guardian-altar.prune", []), (_, _) =>
        { altar.PruneStaleSessions(); return ValueTask.FromResult(true); }, ct).ConfigureAwait(false);
        var pending = altar.Pending(); var now = clock.GetUtcNow();
        return new GuardianAltarSchedule(pending.Count == 0 ? null : pending.Min(x => x.Deadline),
            pending.Where(x => x.Deadline <= now).ToArray());
    }
    public async ValueTask<bool> HandleAsync(CommandExecutionContext context, AdvanceGuardianAltarCommand command, CancellationToken ct)
    {
        var expected = command.Expected;
        Exception? rejected = null;
        var participants = sessions.IsInWorld(expected.Session) ? TimerRecipientCapture.Capture([expected.Session], users) : [];
        var result = await units.WriteResultAsync(context, new("guardian-altar.deadline", participants), async (unit, token) =>
        {
            try { return new GuardianAltarMutation(Action: await altar.AdvanceAsync(expected.RunId, expected.Generation, clock.GetUtcNow(), token).ConfigureAwait(false)); }
            catch (Exception error) when (error is not OperationCanceledException && !Dfo.Server.Abstractions.Transactions.TransactionOutcomeUncertainException.Contains(error))
            { rejected = error; unit.RequireRollback(error); return new GuardianAltarMutation(); }
        }, ct).ConfigureAwait(false);
        if (result is CommandWriteResult<GuardianAltarMutation>.CommitRolledBack failed) rejected = failed.Cause;
        if (rejected is not null) { logger.LogError(rejected, "Guardian altar deadline failed run={Run} generation={Generation}", expected.RunId, expected.Generation); return false; }
        if (((CommandWriteResult<GuardianAltarMutation>.Returned)result).Value.Action is { } action)
        {
            await GuardianAltarProjection.PublishAsync(action, altar, sessions, runs, outbox, clock, ct).ConfigureAwait(false);
            foreach (var spawn in action.Spawns)
                logger.LogInformation("Guardian altar spawn prepared run={RunId} generation={Generation} map={MapId} sequence={Sequence} signedSequence={SignedSequence} monster={Monster} level={Level} basis={Basis} phase={Phase} body={Body}",
                    action.State.RunId, action.State.Generation, action.State.Admission.Map.MapId, spawn.Sequence,
                    unchecked((short)spawn.Sequence), spawn.Spawn.MonsterCode, spawn.Spawn.Level,
                    action.State.Admission.BasisLevel, action.State.Phase,
                    Convert.ToHexString(GuardianAltarPayloadCodec.Spawn(spawn.Sequence, altar.DescribeSpawn(spawn.Spawn))));
            if (action.StageEnded)
                logger.LogInformation("Guardian altar end prepared run={RunId} status={Status} reason={Reason} returnDue={Deadline}",
                    action.State.RunId, action.State.Status, action.State.EndReason, action.State.Deadline);
        }
        return true;
    }
}
internal sealed class GuardianAltarDeadlineSource : DungeonDeadlineSource
{
    public override DungeonDeadlineKind Kind => DungeonDeadlineKind.GuardianAltar;
    // The scan also prunes selections whose session went away, so it keeps running while no run is pending.
    public override TimeSpan? IdlePoll => TimeSpan.FromSeconds(30);
    public override async ValueTask<DateTimeOffset?> RunAsync(DungeonDeadlineRound round, CancellationToken ct)
    {
        var schedule = await round.SubmitAsync(new MaintainGuardianAltarCommand(), ct).ConfigureAwait(false);
        foreach (var due in schedule.Due)
        {
            if (due.Status is GuardianAltarStatus.Won or GuardianAltarStatus.Lost or GuardianAltarStatus.Abandoned)
            {
                var recipients = await round.SubmitAsync(new ReturnGuardianAltarCommand(due), "return", ct).ConfigureAwait(false);
                foreach (var session in recipients)
                    await round.ItemAsync(new ProjectDungeonReturnCommand(session, due.RunId, due.Status == GuardianAltarStatus.Won),
                        "return-member", CancellationToken.None).ConfigureAwait(false);
            }
            else await round.ItemAsync(new AdvanceGuardianAltarCommand(due), "deadline", ct).ConfigureAwait(false);
        }
        return (await round.SubmitAsync(new MaintainGuardianAltarCommand(), ct).ConfigureAwait(false)).Next;
    }
}
