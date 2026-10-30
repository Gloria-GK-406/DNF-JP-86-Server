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
        { "SelectedSessionRefreshService", "timer:SelectedSessionRefreshService/altar", GameplayCallSource.Timer },
        { "SelectedSessionRefreshService", "timer:SelectedSessionRefreshService/dimension", GameplayCallSource.Timer },
        { "SelectedSessionRefreshService", "timer:SelectedSessionRefreshService/gold-silver-horn", GameplayCallSource.Timer },
        { "SelectedSessionRefreshService", "timer:SelectedSessionRefreshService/revive-coin", GameplayCallSource.Timer },
        { "SelectedSessionRefreshService", "timer:SelectedSessionRefreshService/mirror", GameplayCallSource.Timer },
        { "SelectedSessionRefreshService", "timer:SelectedSessionRefreshService/heras", GameplayCallSource.Timer },
        { "SelectedSessionRefreshService", "timer:SelectedSessionRefreshService/tournament", GameplayCallSource.Timer },
        { "SelectedSessionRefreshService", "timer:SelectedSessionRefreshService/item-usage", GameplayCallSource.Timer },
        { "SelectedSessionRefreshService", "timer:SelectedSessionRefreshService/premium", GameplayCallSource.Timer },
        { "SelectedSessionRefreshService", "timer:SelectedSessionRefreshService/fatigue", GameplayCallSource.Timer },
        { "SelectedSessionRefreshService", "timer:SelectedSessionRefreshService/lost-land", GameplayCallSource.Timer },
        { "DungeonDeadlineScheduler", "timer:DungeonDeadlineScheduler/lost-land-completion", GameplayCallSource.Timer },
        { "DungeonDeadlineScheduler", "timer:DungeonDeadlineScheduler/lost-land-fever", GameplayCallSource.Timer },
        { "WeaknessRecoveryService", "timer:WeaknessRecoveryService", GameplayCallSource.Timer },
        { "CreatureClockService", "timer:CreatureClockService", GameplayCallSource.Timer },
        { "DungeonDeadlineScheduler", "timer:DungeonDeadlineScheduler/guardian-altar", GameplayCallSource.Timer },
        { "DungeonDeadlineScheduler", "timer:DungeonDeadlineScheduler/altar", GameplayCallSource.Timer },
        { "DungeonDeadlineScheduler", "timer:DungeonDeadlineScheduler/room-mechanism", GameplayCallSource.Timer },
        { "MarriageWeddingScheduler", "timer:MarriageWeddingScheduler", GameplayCallSource.Timer },
        { "TournamentRewardSweepService", "timer:TournamentRewardSweepService", GameplayCallSource.Timer },
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
