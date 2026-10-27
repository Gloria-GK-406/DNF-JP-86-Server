using Dfo.Server.Abstractions.Execution;

namespace Dfo.Server.Application.Tests.Execution;

/// <summary>
/// AC-006 for spec appendix A rows 5-7 and the joust part of row 11: every timer source of the
/// production composition, driven by its own clock, runs each tick as a CMD of its type on the
/// executor. One row per source; "source → CMD type → this row" is the reconciliation line.
/// </summary>
[Collection("Scheduler")]
public sealed class TimerSourceReconciliationTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(15);

    public static TheoryData<string, string, GameplayCallSource> Sources => new()
    {
        { "JoustMaintenanceService", "timer:JoustMaintenanceService", GameplayCallSource.Timer },
        { "JoustMaintenanceService", "startup:JoustMaintenanceService", GameplayCallSource.StartupRecovery },
        { "DungeonPartyWipeService", "timer:DungeonPartyWipeService", GameplayCallSource.Timer },
        { "FatigueDailyRefreshService", "timer:FatigueDailyRefreshService", GameplayCallSource.Timer },
        { "PremiumDailyRefreshService", "timer:PremiumDailyRefreshService", GameplayCallSource.Timer },
        { "MirrorQuotaDailyRefreshService", "timer:MirrorQuotaDailyRefreshService", GameplayCallSource.Timer },
        { "DimensionQuotaDailyRefreshService", "timer:DimensionQuotaDailyRefreshService", GameplayCallSource.Timer },
        { "GoldSilverHornQuotaDailyRefreshService", "timer:GoldSilverHornQuotaDailyRefreshService", GameplayCallSource.Timer },
        { "ReviveCoinQuotaDailyRefreshService", "timer:ReviveCoinQuotaDailyRefreshService", GameplayCallSource.Timer },
        { "AltarQuotaDailyRefreshService", "timer:AltarQuotaDailyRefreshService", GameplayCallSource.Timer },
        { "TournamentQuotaDailyRefreshService", "timer:TournamentQuotaDailyRefreshService", GameplayCallSource.Timer },
        { "HerasQuotaDailyRefreshService", "timer:HerasQuotaDailyRefreshService", GameplayCallSource.Timer },
        { "ItemUsageDailyRefreshService", "timer:ItemUsageDailyRefreshService", GameplayCallSource.Timer },
        { "WeaknessRecoveryService", "timer:WeaknessRecoveryService", GameplayCallSource.Timer },
        { "CreatureClockService", "timer:CreatureClockService", GameplayCallSource.Timer },
        { "AltarScheduler", "timer:AltarScheduler", GameplayCallSource.Timer },
        { "MarriageWeddingScheduler", "timer:MarriageWeddingScheduler", GameplayCallSource.Timer },
        { "TournamentRewardSweepService", "timer:TournamentRewardSweepService", GameplayCallSource.Timer },
        { "RoomMechanismMaintenanceService", "timer:RoomMechanismMaintenanceService", GameplayCallSource.Timer },
        { "BossTowerReturnSweepService", "timer:BossTowerReturnSweepService", GameplayCallSource.Timer },
        { "IllusionTowerReturnSweepService", "timer:IllusionTowerReturnSweepService", GameplayCallSource.Timer },
        { "ScheduledTaskHostedService", "timer:ScheduledTaskHostedService/character.name-tag.expiration", GameplayCallSource.Timer },
        { "ScheduledTaskHostedService", "timer:ScheduledTaskHostedService/inventory.expiration.online", GameplayCallSource.Timer },
        { "ScheduledTaskHostedService", "timer:ScheduledTaskHostedService/tau-kingdom.ticket-refill.online", GameplayCallSource.Timer },
        { "ScheduledTaskHostedService", "timer:ScheduledTaskHostedService/southern-dale.ticket-refill.online", GameplayCallSource.Timer },
        { "ScheduledTaskHostedService", "timer:ScheduledTaskHostedService/boss-tower.ticket-refill.online", GameplayCallSource.Timer },
    };

    [Theory]
    [MemberData(nameof(Sources))]
    public async Task Each_timer_source_runs_its_ticks_as_its_own_cmd_on_the_executor(
        string service, string commandType, GameplayCallSource source)
    {
        await using var host = new TimerSourceHost();
        await host.StartAsync(host.Hosted(service));

        using var timeout = new CancellationTokenSource(Patience);
        TimerJobSeen? seen;
        while ((seen = host.Queue.Ran.FirstOrDefault(x => x.CommandType == commandType)) is null)
        {
            // Every manual timer fires once per advance, whatever its period.
            host.Clock.Advance(TimeSpan.FromDays(1));
            await Task.Delay(20, timeout.Token);
        }

        Assert.Contains((commandType, source), host.Queue.Enqueued);
        Assert.True(seen.OnExecutor);
        var chain = Assert.NotNull(seen.Current);
        Assert.Equal(source, chain.Source);
        Assert.Equal(commandType, chain.CommandType);
        Assert.True(seen.Completed, $"{commandType} failed on the executor.");
    }
}
