using System.Threading.Channels;
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
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.GuardianAltar;

internal sealed class GuardianAltarWakeSignal(TimeProvider clock) : IGuardianAltarSchedulerWakeSignal
{
    private readonly Channel<bool> channel = System.Threading.Channels.Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    { SingleReader = true, FullMode = BoundedChannelFullMode.DropWrite });
    public void Wake() => channel.Writer.TryWrite(true);
    public async ValueTask WaitAsync(DateTimeOffset? at, CancellationToken ct)
    {
        var delay = at is null ? TimeSpan.FromSeconds(30) : at.Value - clock.GetUtcNow();
        if (delay < TimeSpan.FromMilliseconds(50)) delay = TimeSpan.FromMilliseconds(50);
        using var timer = new CancellationTokenSource(delay, clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timer.Token);
        try { _ = await channel.Reader.ReadAsync(linked.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && timer.IsCancellationRequested) { }
    }
}
public sealed record MaintainGuardianAltarCommand : ICommand<GuardianAltarSchedule>;
public sealed record AdvanceGuardianAltarCommand(GuardianAltarRunState Expected) : ICommand<bool>;
public sealed record ReturnGuardianAltarCommand(GuardianAltarRunState Expected) : ICommand<ImmutableArray<Dfo.Server.Data.World.SelectedSessionIdentity>>;
public sealed record GuardianAltarSchedule(DateTimeOffset? Next, IReadOnlyList<GuardianAltarRunState> Due);

internal sealed class GuardianAltarReturnCommandHandler(IGuardianAltarDomainService altar, IDungeonRunDomainService runs,
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
    IOnlineUserQueryDomainService users, ICommandWorkUnits units, IDungeonRunDomainService runs, IGameCommandOutbox outbox, IInventorySlotProjectionDomainService slots, TimeProvider clock,
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
            await GuardianAltarProjection.PublishAsync(action, altar, sessions, runs, outbox, slots, clock, ct).ConfigureAwait(false);
            foreach (var spawn in action.Spawns)
                logger.LogInformation("Guardian altar spawn prepared run={RunId} generation={Generation} map={MapId} sequence={Sequence} signedSequence={SignedSequence} monster={Monster} level={Level} basis={Basis} phase={Phase} body={Body}",
                    action.State.RunId, action.State.Generation, action.State.Admission.Map.MapId, spawn.Sequence,
                    unchecked((short)spawn.Sequence), spawn.Spawn.MonsterCode, spawn.Spawn.Level,
                    action.State.Admission.BasisLevel, action.State.Phase,
                    Convert.ToHexString(GuardianAltarPayloadCodec.Spawn(spawn.Sequence, spawn.Spawn, altar.Catalog)));
            if (action.StageEnded)
                logger.LogInformation("Guardian altar end prepared run={RunId} status={Status} reason={Reason} returnDue={Deadline}",
                    action.State.RunId, action.State.Status, action.State.EndReason, action.State.Deadline);
        }
        return true;
    }
}
internal sealed class GuardianAltarScheduler(ICommandBus commands, GuardianAltarWakeSignal wake, TimeProvider clock,
    ILogger<GuardianAltarScheduler> logger) : TypedGameplayTimerSource(commands, logger)
{
    private DateTimeOffset? next;
    protected override string SourceName => "timer:GuardianAltarScheduler";
    protected override async Task RunRoundAsync(GameplayCallChain root, CancellationToken ct)
    {
        var schedule = await SubmitAsync(new MaintainGuardianAltarCommand(), root, SourceName, ct).ConfigureAwait(false);
        foreach (var due in schedule.Due)
        {
            if (due.Status is GuardianAltarStatus.Won or GuardianAltarStatus.Lost or GuardianAltarStatus.Abandoned)
            {
                var recipients = await SubmitAsync(new ReturnGuardianAltarCommand(due), root, SourceName + "/return", ct).ConfigureAwait(false);
                foreach (var session in recipients)
                    await ItemAsync(new ProjectDungeonReturnCommand(session, due.RunId, due.Status == GuardianAltarStatus.Won),
                        root, SourceName + "/return-member", CancellationToken.None).ConfigureAwait(false);
            }
            else await ItemAsync(new AdvanceGuardianAltarCommand(due), root, SourceName + "/deadline", ct).ConfigureAwait(false);
        }
        next = (await SubmitAsync(new MaintainGuardianAltarCommand(), root, SourceName, ct).ConfigureAwait(false)).Next;
    }
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            { next = clock.GetUtcNow().AddSeconds(1); await HostedAsync(ct).ConfigureAwait(false); await wake.WaitAsync(next, ct).ConfigureAwait(false); }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }
}
