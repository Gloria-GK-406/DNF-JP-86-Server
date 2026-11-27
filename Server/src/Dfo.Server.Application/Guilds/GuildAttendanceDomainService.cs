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
        return store.RecordAttendanceAsync(character, Today(now), ct);
    }

    public ValueTask<IReadOnlyList<byte[]>> ReadAttendanceTodayAsync(CharacterId viewer, CancellationToken ct) =>
        store.ReadAttendanceAsync(viewer, Today(clock.GetUtcNow()), ct);

    public ValueTask<int> CountWeeklyVisitorsAsync(GuildId guild, CancellationToken ct)
    {
        var today = Today(clock.GetUtcNow());
        return store.CountAttendingMembersAsync(guild, today.AddDays(-6), today, ct);
    }

    private DateOnly Today(DateTimeOffset now) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, clock.LocalTimeZone).DateTime);
}
