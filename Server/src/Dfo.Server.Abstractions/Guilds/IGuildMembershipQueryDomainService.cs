using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Guilds;

namespace Dfo.Server.Abstractions.Guilds;

public interface IGuildMembershipQueryDomainService
{
    ValueTask<GuildPublicIdentity?> FindPublicIdentityAsync(CharacterId character, CancellationToken ct);
    ValueTask<GuildMembership?> FindMembershipAsync(CharacterId character, CancellationToken ct);
    ValueTask<IReadOnlyList<CharacterId>> ReadMemberIdsAsync(GuildId guild, CancellationToken ct);
    ValueTask<GuildApplication?> FindApplicationAsync(CharacterId applicant, CancellationToken ct);
    ValueTask<GuildPendingApplication?> FindPendingApplicationAsync(CharacterId applicant, CancellationToken ct);
    ValueTask<IReadOnlyList<GuildApplication>> ReadApplicationsAsync(CharacterId reviewer, CancellationToken ct);
    ValueTask<IReadOnlyList<GuildApplicationEntry>?> ReadApplicationEntriesAsync(CharacterId reviewer, CancellationToken ct);
    ValueTask<IReadOnlyList<GuildMember>> ReadRosterAsync(CharacterId viewer, CancellationToken ct);
    ValueTask<IReadOnlyList<GuildRosterEntry>> ReadRosterEntriesAsync(CharacterId viewer, CancellationToken ct);
    ValueTask<IReadOnlyList<GuildRankSetting>> ReadRankSettingsAsync(GuildId guild, CancellationToken ct);
}
