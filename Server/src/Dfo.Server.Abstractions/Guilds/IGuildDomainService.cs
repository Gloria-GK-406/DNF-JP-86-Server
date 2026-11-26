using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Guilds;

namespace Dfo.Server.Abstractions.Guilds;

public interface IGuildDomainService
{
    ValueTask<GuildMutationStatus> CheckCreationAsync(CharacterId actor, string name, CancellationToken ct);
    ValueTask<GuildMutationResult> CreateAsync(CharacterId actor, string name, string promotion, CancellationToken ct);
    ValueTask<GuildMutationStatus> SetPromotionAsync(CharacterId actor, string promotion, CancellationToken ct);
    ValueTask<GuildRankAssignmentResult?> AssignRankAsync(CharacterId actor, byte[] name, byte rank, CancellationToken ct);
    ValueTask<GuildMutationStatus> SetRankNameAsync(CharacterId actor, byte rank, string name, CancellationToken ct);
    ValueTask<GuildMutationStatus> SetRankPermissionsAsync(CharacterId actor, byte rank, uint permissions, CancellationToken ct);
    ValueTask<GuildMutationStatus> SetRecommendedChannelAsync(CharacterId actor, int channel, CancellationToken ct);
    ValueTask<GuildMutationResult> DonateGoldAsync(CharacterId actor, int amount, CancellationToken ct);
    ValueTask<GuildMutationResult> ApplyAsync(CharacterId actor, GuildId guild, string message, CancellationToken ct);
    ValueTask<GuildMutationResult> ApproveAsync(CharacterId actor, CharacterId applicant, CancellationToken ct);
    ValueTask<GuildMutationResult> RejectApplicationAsync(CharacterId actor, CharacterId applicant, CancellationToken ct);
    ValueTask<GuildMutationResult> CancelApplicationAsync(CharacterId actor, GuildId guild, CancellationToken ct);
    ValueTask<GuildMutationResult> LeaveAsync(CharacterId actor, CancellationToken ct);
    ValueTask<GuildMutationResult> RemoveMemberAsync(CharacterId actor, CharacterId member, CancellationToken ct);
    ValueTask<GuildMutationResult> DisbandAsync(CharacterId actor, CancellationToken ct);

    /// <summary>Clears a nonmember's application within the same transaction as the character deletion.</summary>
    ValueTask<bool> PrepareCharacterDeletionAsync(CharacterId character, CancellationToken ct);
}

public interface IGuildQueryDomainService
{
    ValueTask<GuildDiscoveryDetails?> FindGuildAsync(string name, CancellationToken ct);
    ValueTask<IReadOnlyList<GuildAccountEntry>> ReadAccountGuildsAsync(AccountId account, CancellationToken ct);
    ValueTask<IReadOnlyList<GuildRecommendation>> ReadRecommendationsAsync(CancellationToken ct);
    ValueTask<GuildPublicIdentity?> FindPublicIdentityAsync(CharacterId character, CancellationToken ct);
    ValueTask<GuildMembership?> FindMembershipAsync(CharacterId character, CancellationToken ct);
    ValueTask<GuildApplication?> FindApplicationAsync(CharacterId applicant, CancellationToken ct);
    ValueTask<GuildPendingApplication?> FindPendingApplicationAsync(CharacterId applicant, CancellationToken ct);
    ValueTask<IReadOnlyList<GuildApplication>> ReadApplicationsAsync(CharacterId reviewer, CancellationToken ct);
    ValueTask<IReadOnlyList<GuildApplicationEntry>?> ReadApplicationEntriesAsync(CharacterId reviewer, CancellationToken ct);
    ValueTask<IReadOnlyList<GuildMember>> ReadRosterAsync(CharacterId viewer, CancellationToken ct);
    ValueTask<IReadOnlyList<GuildRosterEntry>> ReadRosterEntriesAsync(CharacterId viewer, CancellationToken ct);
    ValueTask<IReadOnlyList<GuildRankSetting>> ReadRankSettingsAsync(GuildId guild, CancellationToken ct);
    ValueTask<IReadOnlyList<GuildDonationEntry>?> ReadDonationHistoryAsync(CharacterId viewer, int limit, CancellationToken ct);
}
