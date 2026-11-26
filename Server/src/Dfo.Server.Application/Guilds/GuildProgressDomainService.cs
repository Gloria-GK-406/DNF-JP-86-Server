using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Guilds;

internal sealed class GuildProgressDomainService(IGuildDomainStore store, TimeProvider clock) : IGuildProgressDomainService
{
    public ValueTask<bool> RecordAttendanceAsync(CharacterId character, CancellationToken ct) =>
        store.RecordAttendanceAsync(character, Today(), ct);

    public ValueTask<IReadOnlyList<byte[]>> ReadAttendanceTodayAsync(CharacterId viewer, CancellationToken ct) =>
        store.ReadAttendanceAsync(viewer, Today(), ct);

    private DateOnly Today() => DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
}
