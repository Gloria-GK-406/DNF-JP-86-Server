using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Guilds;

namespace Dfo.Server.Application.Guilds;

internal sealed class GuildQueryDomainService(IGuildDomainStore store, GuildRuleService rules) : IGuildQueryDomainService
{
    public ValueTask<GuildDiscoveryDetails?> FindGuildAsync(string name, CancellationToken ct) =>
        rules.NameKey(name) is { } key ? store.FindGuildByNameKeyAsync(key, ct) : ValueTask.FromResult<GuildDiscoveryDetails?>(null);

    public ValueTask<IReadOnlyList<GuildAccountEntry>> ReadAccountGuildsAsync(AccountId account, CancellationToken ct) =>
        store.ReadAccountGuildsAsync(account, byte.MaxValue, ct);

    public ValueTask<IReadOnlyList<GuildRecommendation>> ReadRecommendationsAsync(CancellationToken ct) =>
        store.ReadRecommendationsAsync(10, ct);

    public async ValueTask<GuildPublicIdentity?> FindPublicIdentityAsync(CharacterId character, CancellationToken ct) =>
        await store.FindMembershipAsync(character, ct).ConfigureAwait(false) is { Guild: var guild }
            ? new(guild.Id, guild.Name, guild.Level)
            : null;

    public ValueTask<GuildMembership?> FindMembershipAsync(CharacterId character, CancellationToken ct) =>
        store.FindMembershipAsync(character, ct);

    public ValueTask<GuildApplication?> FindApplicationAsync(CharacterId applicant, CancellationToken ct) =>
        store.FindApplicationAsync(applicant, ct);

    public ValueTask<IReadOnlyList<GuildApplication>> ReadApplicationsAsync(CharacterId leader, CancellationToken ct) =>
        store.ReadApplicationsAsync(leader, ct);

    public ValueTask<IReadOnlyList<GuildMember>> ReadRosterAsync(CharacterId viewer, CancellationToken ct) =>
        store.ReadRosterAsync(viewer, ct);

    public ValueTask<IReadOnlyList<GuildRosterEntry>> ReadRosterEntriesAsync(CharacterId viewer, CancellationToken ct) =>
        store.ReadRosterEntriesAsync(viewer, ct);

    public async ValueTask<IReadOnlyList<GuildRankSetting>> ReadRankSettingsAsync(GuildId guild, CancellationToken ct)
    {
        var overrides = await store.ReadRankSettingsAsync(guild, ct).ConfigureAwait(false);
        if (overrides.Count == 0)
            return GuildRankSettings.Defaults;

        var settings = GuildRankSettings.Defaults.ToArray();
        foreach (var setting in overrides)
        {
            var defaultRank = settings[setting.Rank];
            settings[setting.Rank] = defaultRank with
            {
                PermissionMask = setting.PermissionMask ?? defaultRank.PermissionMask,
                Name = setting.Name ?? defaultRank.Name,
            };
        }
        return settings;
    }

    public async ValueTask<IReadOnlyList<GuildDonationEntry>?> ReadDonationHistoryAsync(
        CharacterId viewer, int limit, CancellationToken ct)
    {
        if (await store.FindMembershipAsync(viewer, ct).ConfigureAwait(false) is not { } membership)
            return null;
        return await store.ReadDonationHistoryAsync(membership.Guild.Id, limit, ct).ConfigureAwait(false);
    }
}
