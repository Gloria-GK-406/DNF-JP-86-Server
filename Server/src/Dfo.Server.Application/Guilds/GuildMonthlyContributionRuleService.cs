using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Guilds;

namespace Dfo.Server.Application.Guilds;

internal sealed class GuildMonthlyContributionRuleService(IGuildEconomySource content, IDungeonDefinitionSource dungeons)
{
    private const int ResetHour = 6;

    public GuildMonthlyContributionRule? ReadMonthlyContribution() => content.ReadMonthlyContribution();

    public static DateOnly AttendanceDay(DateTimeOffset instant) =>
        DateOnly.FromDateTime(instant.ToOffset(IDailyResetRuleService.BusinessOffset).DateTime.AddHours(-ResetHour));

    public static (DateTimeOffset Start, DateTimeOffset End) CurrentPeriod(DateTimeOffset instant)
    {
        var start = MonthStartAt(instant);
        return (start, start.AddMonths(1));
    }

    public static (DateTimeOffset Start, DateTimeOffset End) SettlementPeriod(DateTimeOffset boundary)
    {
        var end = MonthStartAt(boundary);
        return (end.AddMonths(-1), end);
    }

    public static DateTimeOffset MonthStartAt(DateTimeOffset instant)
    {
        var day = AttendanceDay(instant);
        return new(day.Year, day.Month, 1, ResetHour, 0, 0, IDailyResetRuleService.BusinessOffset);
    }

    public static GuildItemReward? EvaluateReward(GuildMonthlyContributionRule rule, long points, DateTimeOffset now) =>
        points >= rule.Target ? new(new(rule.ItemId, rule.Count), now.AddDays(7)) : null;

    public static (GuildContributionSource Source, int Points) AttendanceAward(bool isLeader) =>
        isLeader ? (GuildContributionSource.LeaderAttendance, 100) : (GuildContributionSource.Attendance, 10);

    public async ValueTask<bool> IsRecommendedAsync(int dungeonId, int level, CancellationToken ct) =>
        await dungeons.GetAsync(dungeonId, ct).ConfigureAwait(false) is { } dungeon
        && DungeonRecommendation.IsRecommended(dungeon, level);
}
