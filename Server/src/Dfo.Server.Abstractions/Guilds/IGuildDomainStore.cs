using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Guilds;

namespace Dfo.Server.Abstractions.Guilds;

public interface IGuildDomainStore
{
    ValueTask<bool> ExistsAsync(GuildId guild, CancellationToken ct);
    ValueTask<bool> NameTakenAsync(string nameKey, CancellationToken ct);
    ValueTask<GuildMembership?> FindMembershipAsync(CharacterId character, CancellationToken ct);
    ValueTask<GuildApplication?> FindApplicationAsync(CharacterId applicant, CancellationToken ct);
    ValueTask<IReadOnlyList<GuildApplication>> ReadApplicationsAsync(CharacterId leader, CancellationToken ct);
    ValueTask<IReadOnlyList<GuildMember>> ReadRosterAsync(CharacterId viewer, CancellationToken ct);
    ValueTask<IReadOnlyList<GuildRosterEntry>> ReadRosterEntriesAsync(CharacterId viewer, CancellationToken ct);
    ValueTask<IReadOnlyList<GuildDonationEntry>> ReadDonationHistoryAsync(GuildId guild, int limit, CancellationToken ct);
    ValueTask<IReadOnlyList<CharacterId>> AffectedCharactersAsync(GuildId guild, CancellationToken ct);
    ValueTask<Guild> InsertGuildAsync(string name, string nameKey, string promotion, DateTimeOffset at, CancellationToken ct);
    ValueTask SetRecommendedChannelAsync(GuildId guild, int channel, CancellationToken ct);
    ValueTask AddDonationAsync(GuildId guild, CharacterId donor, int gold, int points, DateTimeOffset at, CancellationToken ct);
    ValueTask AddMemberAsync(GuildId guild, CharacterId member, bool isLeader, DateTimeOffset at, CancellationToken ct);
    ValueTask AddApplicationAsync(GuildApplication application, CancellationToken ct);
    ValueTask DeleteApplicationAsync(CharacterId applicant, CancellationToken ct);
    ValueTask RemoveMemberAsync(GuildId guild, CharacterId member, CancellationToken ct);
    ValueTask DisbandAsync(GuildId guild, CancellationToken ct);
}

public interface IGuildEconomySource
{
    int? ReadGoldCost();
    int? ReadDonationGoldPerPoint();
}
