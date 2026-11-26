using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Guilds;

namespace Dfo.Server.Abstractions.Guilds;

public interface IGuildDomainStore
{
    ValueTask<bool> ExistsAsync(GuildId guild, CancellationToken ct);
    ValueTask<bool> NameTakenAsync(string nameKey, CancellationToken ct);
    ValueTask<GuildMembership?> FindMembershipAsync(CharacterId character, CancellationToken ct);
    ValueTask<GuildMember?> FindRosterMemberAsync(GuildId guild, byte[] name, CancellationToken ct);
    ValueTask SetMemberRankAsync(GuildId guild, CharacterId character, byte rank, CancellationToken ct);
    ValueTask<GuildDiscoveryDetails?> FindGuildByNameKeyAsync(string nameKey, CancellationToken ct);
    ValueTask<IReadOnlyList<GuildAccountEntry>> ReadAccountGuildsAsync(AccountId account, int limit, CancellationToken ct);
    ValueTask<IReadOnlyList<GuildRecommendation>> ReadRecommendationsAsync(int limit, CancellationToken ct);
    ValueTask<GuildApplication?> FindApplicationAsync(CharacterId applicant, CancellationToken ct);
    ValueTask<GuildPendingApplication?> FindPendingApplicationAsync(CharacterId applicant, CancellationToken ct);
    ValueTask<IReadOnlyList<GuildApplicationEntry>> ReadApplicationEntriesAsync(GuildId guild, CancellationToken ct);
    ValueTask<IReadOnlyList<GuildMember>> ReadRosterAsync(CharacterId viewer, CancellationToken ct);
    ValueTask<IReadOnlyList<GuildRosterEntry>> ReadRosterEntriesAsync(CharacterId viewer, CancellationToken ct);
    ValueTask<IReadOnlyList<GuildDonationEntry>> ReadDonationHistoryAsync(GuildId guild, int limit, CancellationToken ct);
    ValueTask<bool> RecordAttendanceAsync(CharacterId character, DateOnly day, CancellationToken ct);
    ValueTask<IReadOnlyList<byte[]>> ReadAttendanceAsync(CharacterId viewer, DateOnly day, CancellationToken ct);
    ValueTask<IReadOnlyList<CharacterId>> AffectedCharactersAsync(GuildId guild, CancellationToken ct);
    ValueTask<Guild> InsertGuildAsync(string name, string nameKey, string promotion, DateTimeOffset at, CancellationToken ct);
    ValueTask<IReadOnlyList<GuildRankOverride>> ReadRankSettingsAsync(GuildId guild, CancellationToken ct);
    ValueTask SetPromotionAsync(GuildId guild, string promotion, CancellationToken ct);
    ValueTask SetAnnouncementAsync(GuildId guild, string announcement, CancellationToken ct);
    ValueTask<bool> TrySetMarkAsync(GuildId guild, byte mark, int cost, CancellationToken ct);
    ValueTask SetRankNameAsync(GuildId guild, byte rank, string name, CancellationToken ct);
    ValueTask SetRankPermissionsAsync(GuildId guild, byte rank, uint permissions, CancellationToken ct);
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
    GuildMarkPricing? ReadMarkPricing();
}
