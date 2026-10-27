using System.Threading.Channels;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Protocol.Game.Dungeons;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Application.Execution;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.Altar;

public sealed class AltarWakeSignal(TimeProvider clock) : IAltarSchedulerWakeSignal
{
    private readonly Channel<bool> wake = System.Threading.Channels.Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    { SingleReader = true, FullMode = BoundedChannelFullMode.DropWrite });
    public void Wake() => wake.Writer.TryWrite(true);
    public async ValueTask WaitAsync(DateTimeOffset? deadline, CancellationToken ct)
    {
        if (deadline is null) { _ = await wake.Reader.ReadAsync(ct).ConfigureAwait(false); return; }
        var delay = deadline.Value - clock.GetUtcNow();
        if (delay < TimeSpan.FromMilliseconds(50)) delay = TimeSpan.FromMilliseconds(50);
        using var timer = new CancellationTokenSource(delay, clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timer.Token);
        try { _ = await wake.Reader.ReadAsync(linked.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && timer.IsCancellationRequested) { }
    }
}
public sealed record MaintainAltarCommand : ICommand<AltarNextDeadline>;
public sealed record AdvanceAltarDeadlineCommand(AltarDeadline Deadline) : ICommand<bool>;
public sealed record AltarNextDeadline(DateTimeOffset? At)
{
    public IReadOnlyList<AltarDeadline> Due { get; init; } = [];
}
public sealed class AltarTimerCommandHandler(IAltarDungeonDomainService altars, IDungeonRunDomainService runs,
    Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService sessions,
    Dfo.Server.Abstractions.Networking.IDungeonRunProjectionDomainService registry,
    Dfo.Server.Abstractions.OnlineUsers.IOnlineUserQueryDomainService users,
    ICommandWorkUnits units, Dfo.Server.Abstractions.DomainEvents.ICommandProjections projections,
    IAltarSchedulerWakeSignal wake, TimeProvider clock, ILogger<AltarTimerCommandHandler> logger)
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
                clock, wake, logger, token).ConfigureAwait(false);
    }

}
public sealed class AltarScheduler(ICommandBus commands, AltarWakeSignal wake, ILogger<AltarScheduler> logger, TimeProvider clock)
    : TypedGameplayTimerSource(commands, logger)
{
    private DateTimeOffset? next;
    protected override string SourceName => "timer:AltarScheduler";
    protected override async Task RunRoundAsync(GameplayCallChain root, CancellationToken token)
    {
        var scan = await SubmitAsync(new MaintainAltarCommand(), root, SourceName, token).ConfigureAwait(false);
        foreach (var due in scan.Due)
            await ItemAsync(new AdvanceAltarDeadlineCommand(due), root, SourceName + "/deadline", token).ConfigureAwait(false);
        next = (await SubmitAsync(new MaintainAltarCommand(), root, SourceName, token).ConfigureAwait(false)).At;
    }
    internal async ValueTask AdvanceAsync(AltarDeadline due, CancellationToken token) =>
        _ = await SubmitAsync(new AdvanceAltarDeadlineCommand(due), GameplayCallChain.NewRoot(GameplayCallSource.Timer), SourceName, token).ConfigureAwait(false);
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                // A failed scan or item cleanup must not turn a live scheduler into an infinite wait.
                next = clock.GetUtcNow().AddSeconds(1);
                await HostedAsync(stoppingToken).ConfigureAwait(false);
                await wake.WaitAsync(next, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
