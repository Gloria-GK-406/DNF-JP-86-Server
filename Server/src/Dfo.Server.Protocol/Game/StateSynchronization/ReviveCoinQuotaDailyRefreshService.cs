using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.UsageQuotas;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.World;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Application.Execution;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.StateSynchronization;

public sealed class ReviveCoinQuotaDailyRefreshService(ISelectedSessionSnapshotQuery directory,
    ISelectedSessionDomainService sessions,
    UsageQuotaProjectionDelivery delivery, IDailyResetRuleService daily, TimeProvider clock,
    ILogger<ReviveCoinQuotaDailyRefreshService> logger, IGameplayQueue queue) : BackgroundService
{
    private readonly Dictionary<SelectedSessionIdentity, DateTimeOffset> delivered = [];
    private const string Command = "timer:ReviveCoinQuotaDailyRefreshService";
    private readonly CoalescingGameplaySource ticks = new(queue);
    // One refresh is one timer CMD on the executor; the entry enqueues it directly, the loop through its source.
    public Task RefreshAsync(CancellationToken ct) => GameplayTimerWork.RunAsync(queue, Command, RefreshCoreAsync, ct);
    private async ValueTask RefreshCoreAsync(CancellationToken ct)
    {
        GameplayWriteAssertion.Writing(nameof(ReviveCoinQuotaDailyRefreshService) + "." + nameof(RefreshCoreAsync));
        var period = daily.PeriodStart(clock.GetUtcNow());
        var current = directory.Snapshot();
        foreach (var stale in delivered.Keys.Except(current).ToArray()) delivered.Remove(stale);
        foreach (var session in current)
        {
            if (delivered.TryGetValue(session, out var previous) && previous == period) continue;
            try
            {
                if (!sessions.IsInWorld(session)) continue;
                var result = await delivery.SendAsync(session, UsageQuotaFamily.ReviveCoin, ct).ConfigureAwait(false);
                if (sessions.IsInWorld(session) && result.Attempts.Any(x =>
                    x.CharacterId == session.CharacterId && x.Outcome == Dfo.Server.Abstractions.Notifications.NotificationOutcome.Delivered))
                    delivered[session] = period;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception error) { logger.LogWarning(error, "Revive coin quota refresh failed character={CharacterId}", session.CharacterId); }
        }
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1), clock);
        try { while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false)) await GameplayTimerWork.TickAsync(ticks, Command, RefreshCoreAsync, logger, stoppingToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
