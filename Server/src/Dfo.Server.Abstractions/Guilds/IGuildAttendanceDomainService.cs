using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Guilds;

namespace Dfo.Server.Abstractions.Guilds;

public interface IGuildAttendanceDomainService
{
    ValueTask<bool> RecordAttendanceAsync(CharacterId character, DateTimeOffset now, CancellationToken ct);
    ValueTask<IReadOnlyList<byte[]>> ReadAttendanceTodayAsync(CharacterId viewer, CancellationToken ct);
    ValueTask<int> CountWeeklyVisitorsAsync(GuildId guild, CancellationToken ct);
}
