using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Guilds;

namespace Dfo.Server.Abstractions.Guilds;

public interface IGuildMonthlyContributionDomainService
{
    ValueTask<bool> RecordAttendanceAsync(CharacterId character, DateTimeOffset now, CancellationToken ct);
    ValueTask RecordClearAsync(CharacterId character, DungeonRunState run, int level, CancellationToken ct);
    ValueTask<GuildContributionTotals> ReadTotalsAsync(GuildId guild, CancellationToken ct);
    ValueTask<IReadOnlyList<GuildContributionEntry>?> ReadHistoryAsync(CharacterId viewer, int limit, CancellationToken ct);
}

public interface IGuildMonthlyContributionStore
{
    ValueTask<bool> RecordAsync(GuildId guild, CharacterId character, GuildContributionSource source, string key,
        int points, DateTimeOffset at, CancellationToken ct);
    ValueTask<GuildContributionTotals> ReadTotalsAsync(GuildId guild, DateTimeOffset start, DateTimeOffset end, CancellationToken ct);
    ValueTask<IReadOnlyList<GuildContributionEntry>> ReadHistoryAsync(GuildId guild, int limit, CancellationToken ct);
    ValueTask<IReadOnlyList<GuildMonthlyContributionCandidate>> ReadUnpaidAsync(
        DateTimeOffset start, DateTimeOffset end, CharacterId? after, int limit, CancellationToken ct);
    ValueTask MarkPaidAsync(GuildMonthlyContributionCandidate recipient, DateTimeOffset start, CancellationToken ct);
}
