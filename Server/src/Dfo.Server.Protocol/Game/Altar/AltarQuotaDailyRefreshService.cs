using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.UsageQuotas;
using Dfo.Server.Application.OnlineUsers;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.World;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.Altar;

/// <summary>Refreshes the same absolute remaining counts at the existing daily-reset boundary.</summary>
public sealed class AltarQuotaDailyRefreshService(ISelectedSessionSnapshotQuery directory,
    ISelectedSessionDomainService sessions, SelectedSessionTransitionGate transitions,
    Dfo.Server.Protocol.Game.StateSynchronization.UsageQuotaProjectionDelivery delivery,
    IDailyResetRuleService daily, TimeProvider clock, ILogger<AltarQuotaDailyRefreshService> logger) : BackgroundService
{
    private readonly Dictionary<SelectedSessionIdentity, DateTimeOffset> delivered = [];
    public async Task RefreshAsync(CancellationToken ct)
    {
        var period = daily.PeriodStart(clock.GetUtcNow()); var current = directory.Snapshot();
        foreach (var stale in delivered.Keys.Except(current).ToArray()) delivered.Remove(stale);
        foreach (var session in current)
        {
            if (delivered.TryGetValue(session, out var previous) && previous == period) continue;
            try
            {
                await using var lease = await transitions.AcquireAsync(session.AccountId, UserAccessMode.Exclusive, ct).ConfigureAwait(false);
                if (sessions.FindByCharacter(session.CharacterId) != session) continue;
                var result = await delivery.SendAsync(session, UsageQuotaFamily.AltarDungeon, ct).ConfigureAwait(false);
                if (sessions.FindByCharacter(session.CharacterId) == session && result.Attempts.Any(x =>
                    x.CharacterId == session.CharacterId && x.Outcome == Dfo.Server.Abstractions.Notifications.NotificationOutcome.Delivered))
                    delivered[session] = period;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception error) { logger.LogWarning(error, "Altar daily quota refresh failed character={CharacterId}", session.CharacterId); }
        }
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1), clock);
        try { while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false)) await RefreshAsync(stoppingToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
