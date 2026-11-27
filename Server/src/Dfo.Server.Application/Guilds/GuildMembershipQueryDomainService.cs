using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Guilds;

namespace Dfo.Server.Application.Guilds;

internal sealed class GuildMembershipQueryDomainService(IGuildDomainStore store) : IGuildMembershipQueryDomainService
{
    public async ValueTask<GuildPublicIdentity?> FindPublicIdentityAsync(CharacterId character, CancellationToken ct) =>
        await store.FindMembershipAsync(character, ct).ConfigureAwait(false) is { Guild: var guild }
            ? new(guild.Id, guild.Name, guild.Level, guild.Mark)
            : null;

    public ValueTask<GuildMembership?> FindMembershipAsync(CharacterId character, CancellationToken ct) =>
        store.FindMembershipAsync(character, ct);

    public ValueTask<IReadOnlyList<CharacterId>> ReadMemberIdsAsync(GuildId guild, CancellationToken ct) =>
        store.ReadMemberIdsAsync(guild, ct);

    public ValueTask<GuildApplication?> FindApplicationAsync(CharacterId applicant, CancellationToken ct) =>
        store.FindApplicationAsync(applicant, ct);

    public ValueTask<GuildPendingApplication?> FindPendingApplicationAsync(CharacterId applicant, CancellationToken ct) =>
        store.FindPendingApplicationAsync(applicant, ct);

    public async ValueTask<IReadOnlyList<GuildApplication>> ReadApplicationsAsync(CharacterId reviewer, CancellationToken ct) =>
        (await ReadApplicationEntriesAsync(reviewer, ct).ConfigureAwait(false))?.Select(row => row.Application).ToArray() ?? [];

    public async ValueTask<IReadOnlyList<GuildApplicationEntry>?> ReadApplicationEntriesAsync(CharacterId reviewer, CancellationToken ct)
    {
        var guild = await FindApplicationReviewGuildAsync(reviewer, ct).ConfigureAwait(false);
        return guild.HasValue ? await store.ReadApplicationEntriesAsync(guild.Value, ct).ConfigureAwait(false) : null;
    }

    public ValueTask<IReadOnlyList<GuildMember>> ReadRosterAsync(CharacterId viewer, CancellationToken ct) =>
        store.ReadRosterAsync(viewer, ct);

    public ValueTask<IReadOnlyList<GuildRosterEntry>> ReadRosterEntriesAsync(CharacterId viewer, CancellationToken ct) =>
        store.ReadRosterEntriesAsync(viewer, ct);

    public async ValueTask<GuildId?> FindApplicationReviewGuildAsync(CharacterId reviewer, CancellationToken ct)
    {
        if (await store.FindMembershipAsync(reviewer, ct).ConfigureAwait(false) is not { } membership)
            return null;
        var ranks = await ReadRankSettingsAsync(membership.Guild.Id, ct).ConfigureAwait(false);
        return GuildRankRuleService.CanReviewApplications(membership.Member, ranks) ? membership.Guild.Id : null;
    }

    public async ValueTask<IReadOnlyList<GuildRankSetting>> ReadRankSettingsAsync(GuildId guild, CancellationToken ct) =>
        GuildRankRuleService.EffectiveRanks(await store.ReadRankSettingsAsync(guild, ct).ConfigureAwait(false));
}
