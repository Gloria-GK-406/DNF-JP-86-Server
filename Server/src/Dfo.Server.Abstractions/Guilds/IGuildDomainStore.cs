using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Guilds;

namespace Dfo.Server.Abstractions.Guilds;

public interface IGuildDomainStore
{
    ValueTask<bool> ExistsAsync(GuildId guild, CancellationToken ct);
    ValueTask<bool> NameTakenAsync(string nameKey, CancellationToken ct);
    ValueTask<bool> TryRenameAsync(GuildId guild, string originalName, string name, string nameKey, CancellationToken ct);
    ValueTask<GuildMembership?> FindMembershipAsync(CharacterId character, CancellationToken ct);
    ValueTask<GuildMember?> FindRosterMemberAsync(GuildId guild, byte[] name, CancellationToken ct);
    ValueTask SetMemberRankAsync(GuildId guild, CharacterId character, byte rank, CancellationToken ct);
    ValueTask TransferLeadershipAsync(GuildLeadershipTransfer transfer, CancellationToken ct);
    ValueTask<GuildDiscoveryDetails?> FindGuildByNameKeyAsync(string nameKey, CancellationToken ct);
    ValueTask<IReadOnlyList<GuildAccountEntry>> ReadAccountGuildsAsync(AccountId account, int limit, CancellationToken ct);
    ValueTask<IReadOnlyList<GuildRecommendation>> ReadRecommendationsAsync(CharacterId viewer, int limit, CancellationToken ct);
    ValueTask<GuildApplication?> FindApplicationAsync(CharacterId applicant, CancellationToken ct);
    ValueTask<GuildPendingApplication?> FindPendingApplicationAsync(CharacterId applicant, CancellationToken ct);
    ValueTask<IReadOnlyList<GuildApplicationEntry>> ReadApplicationEntriesAsync(GuildId guild, CancellationToken ct);
    ValueTask<IReadOnlyList<GuildMember>> ReadRosterAsync(CharacterId viewer, CancellationToken ct);
    ValueTask<IReadOnlyList<CharacterId>> ReadRosterCharacterIdsAsync(CharacterId member, CancellationToken ct);
    ValueTask<IReadOnlyList<GuildRosterEntry>> ReadRosterEntriesAsync(CharacterId viewer, CancellationToken ct);
    ValueTask<IReadOnlyList<GuildDonationEntry>> ReadDonationHistoryAsync(GuildId guild, int limit, CancellationToken ct);
    ValueTask<bool> RecordAttendanceAsync(CharacterId character, DateOnly day, CancellationToken ct);
    ValueTask<IReadOnlyList<byte[]>> ReadAttendanceAsync(CharacterId viewer, DateOnly day, CancellationToken ct);
    ValueTask<int> CountAttendingMembersAsync(GuildId guild, DateOnly firstDay, DateOnly lastDay, CancellationToken ct);
    ValueTask<IReadOnlyList<GuildBoardPost>> ReadBoardPostsAsync(GuildId guild, CancellationToken ct);
    ValueTask<bool> TryWriteBoardPostAsync(GuildId guild, CharacterId author, string text, DateTimeOffset postedAt,
        int capacity, CancellationToken ct);
    ValueTask<bool> DeleteBoardPostAsync(GuildId guild, uint postId, CharacterId? author, CancellationToken ct);
    ValueTask<IReadOnlyList<CharacterId>> ReadMemberIdsAsync(GuildId guild, CancellationToken ct);
    ValueTask<IReadOnlyList<CharacterId>> ReadDisbandRecipientsAsync(GuildId guild, CancellationToken ct);
    ValueTask<Guild> InsertGuildAsync(string name, string nameKey, string promotion, DateTimeOffset at, CancellationToken ct);
    ValueTask<IReadOnlyList<GuildRankOverride>> ReadRankSettingsAsync(GuildId guild, CancellationToken ct);
    ValueTask SetPromotionAsync(GuildId guild, string promotion, CancellationToken ct);
    ValueTask<bool> TrySetMemberMemoAsync(CharacterId character, string memo, CancellationToken ct);
    ValueTask SetAnnouncementAsync(GuildId guild, string announcement, CancellationToken ct);
    ValueTask SetMarkAsync(GuildId guild, byte mark, CancellationToken ct);
    ValueTask<bool> TrySetMedalAuraAsync(GuildId guild, CharacterId leader, GuildMedalAura aura, CancellationToken ct);
    ValueTask SetRankNameAsync(GuildId guild, byte rank, string name, CancellationToken ct);
    ValueTask SetRankPermissionsAsync(GuildId guild, byte rank, uint permissions, CancellationToken ct);
    ValueTask SetRecommendedChannelAsync(GuildId guild, int channel, CancellationToken ct);
    ValueTask<IReadOnlyList<GuildPurchasedContent>> ReadContentsAsync(GuildId guild, CancellationToken ct);
    ValueTask<int?> ReadMonsterExperiencePercentAsync(CharacterId character, long now, CancellationToken ct);
    ValueTask<IReadOnlyList<GuildExpiredContent>> RemoveExpiredContentsAsync(long now, int limit, CancellationToken ct);
    ValueTask PutContentAsync(GuildId guild, GuildPurchasedContent content, CancellationToken ct);
    ValueTask<bool> TryConsumeTeleportAsync(GuildId guild, CancellationToken ct);
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
    GuildMedalAuraCatalog? ReadMedalAuraCatalog();
    GuildContentCatalog? ReadContentCatalog();
    GuildMonthlyContributionRule? ReadMonthlyContribution();
}
