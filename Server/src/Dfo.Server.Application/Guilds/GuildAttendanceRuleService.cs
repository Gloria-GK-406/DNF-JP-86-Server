namespace Dfo.Server.Application.Guilds;

internal static class GuildAttendanceRuleService
{
    public static DateOnly AttendanceDay(DateTimeOffset instant, TimeZoneInfo serverZone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, serverZone).DateTime);

    public static (DateOnly FirstDay, DateOnly LastDay) WeeklyVisitorPeriod(DateTimeOffset instant, TimeZoneInfo serverZone)
    {
        var today = AttendanceDay(instant, serverZone);
        return (today.AddDays(-6), today);
    }
}
