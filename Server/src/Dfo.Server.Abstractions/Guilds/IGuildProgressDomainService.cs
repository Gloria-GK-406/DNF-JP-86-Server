using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.Guilds;

public interface IGuildProgressDomainService
{
    ValueTask<bool> RecordAttendanceAsync(CharacterId character, CancellationToken ct);
    ValueTask<IReadOnlyList<byte[]>> ReadAttendanceTodayAsync(CharacterId viewer, CancellationToken ct);
}
