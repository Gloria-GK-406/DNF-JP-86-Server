using System.Threading.Channels;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.OnlineUsers;
using Dfo.Server.Protocol.Game.Dungeons;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Application.Execution;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.Altar;

public sealed class AltarWakeSignal(TimeProvider clock)
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
public sealed class AltarScheduler(IAltarDungeonDomainService altars, IDungeonRunDomainService runs,
    DungeonProjectionGate projections, SelectedSessionTransitionGate transitions, ICommandBus commands,
    AltarProjectionDelivery delivery, AltarWakeSignal wake, TimeProvider clock, ILogger<AltarScheduler> logger,
    IGameplayQueue queue) : BackgroundService
{
    private readonly CoalescingGameplaySource ticks = new(queue);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                // One round (due deadlines and the next one) is one timer CMD; only the wait is outside.
                DateTimeOffset? next = null;
                await GameplayTimerWork.TickAsync(ticks, "timer:AltarScheduler", async token =>
                {
                    var now = clock.GetUtcNow();
                    foreach (var due in altars.Pending().Where(x => x.At <= now))
                    {
                        try { await AdvanceAsync(due, token).ConfigureAwait(false); }
                        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                        catch (Exception error) { logger.LogError(error, "Altar deadline failed run={RunId} generation={Generation}", due.RunId, due.Generation); }
                    }
                    var pending = altars.Pending();
                    next = pending.Count == 0 ? (DateTimeOffset?)null : pending.Min(x => x.At);
                    // Failed or overdue actions back off; an empty scheduler sleeps until a command wakes it.
                    if (next is { } expired && expired <= clock.GetUtcNow()) next = clock.GetUtcNow().AddMilliseconds(100);
                }, stoppingToken).ConfigureAwait(false);
                await wake.WaitAsync(next, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
    internal async ValueTask AdvanceAsync(AltarDeadline due, CancellationToken ct)
    {
        await using var projection = await projections.AcquireAsync(due.RunId, UserAccessMode.Exclusive, ct).ConfigureAwait(false);
        var cohort = delivery.Participants(due.RunId);
        if (cohort.Count == 0) return;
        var leases = new List<IAsyncDisposable>();
        try
        {
            foreach (var account in cohort.Select(x => x.AccountId).Distinct().OrderBy(x => x.Value))
                leases.Add(await transitions.AcquireAsync(account, UserAccessMode.Exclusive, ct).ConfigureAwait(false));
            var current = altars.Find(due.RunId);
            if (current?.Generation != due.Generation || current.RoomVersion != due.RoomVersion
                || runs.Find(due.RunId)?.RoomVersion != due.RoomVersion) return;
            var result = await commands.SendAsync(new AltarActionCommand(cohort[0], cohort, due.RunId, due.RoomVersion,
                due.Generation, clock.GetUtcNow(), AltarOperation.Advance), ct).ConfigureAwait(false);
            await delivery.ProjectAsync(result.Action, ct).ConfigureAwait(false);
        }
        finally { for (var i = leases.Count - 1; i >= 0; i--) await leases[i].DisposeAsync().ConfigureAwait(false); }
    }
}
