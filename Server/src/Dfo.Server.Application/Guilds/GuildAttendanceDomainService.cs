using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Guilds;

namespace Dfo.Server.Application.Guilds;

internal sealed class GuildAttendanceDomainService(IGuildDomainStore store, TimeProvider clock) : IGuildAttendanceDomainService
{
    public ValueTask<bool> RecordAttendanceAsync(CharacterId character, DateTimeOffset now, CancellationToken ct)
    {
        GameplayWriteAssertion.Writing(nameof(GuildAttendanceDomainService) + "." + nameof(RecordAttendanceAsync));
        var day = GuildAttendanceRuleService.AttendanceDay(now, clock.LocalTimeZone);
        return store.RecordAttendanceAsync(character, day, ct);
    }

    public ValueTask<IReadOnlyList<byte[]>> ReadAttendanceTodayAsync(CharacterId viewer, CancellationToken ct)
    {
        var day = GuildAttendanceRuleService.AttendanceDay(clock.GetUtcNow(), clock.LocalTimeZone);
        return store.ReadAttendanceAsync(viewer, day, ct);
    }

    public ValueTask<int> CountWeeklyVisitorsAsync(GuildId guild, CancellationToken ct)
    {
        var (first, last) = GuildAttendanceRuleService.WeeklyVisitorPeriod(clock.GetUtcNow(), clock.LocalTimeZone);
        return store.CountAttendingMembersAsync(guild, first, last, ct);
    }
}
