using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Guilds;

namespace Dfo.Server.Application.Guilds;

internal static class GuildRankPolicy
{
    public static async ValueTask<GuildId?> FindApplicationReviewGuildAsync(
        IGuildDomainStore store, CharacterId reviewer, CancellationToken ct)
    {
        if (await store.FindMembershipAsync(reviewer, ct).ConfigureAwait(false) is not { } membership)
            return null;
        return await HasPermissionAsync(store, membership, GuildRankSettings.ReviewApplicationsPermission, ct).ConfigureAwait(false)
            ? membership.Guild.Id : null;
    }

    public static async ValueTask<bool> CanAssignRankAsync(IGuildDomainStore store, GuildMembership actor,
        GuildMember target, byte rank, CancellationToken ct)
    {
        if (target.IsLeader || target.Character == actor.Member.Character)
            return false;
        if (!actor.Member.IsLeader && (actor.Member.Rank != GuildRankSettings.ViceLeader
            || target.Rank <= GuildRankSettings.ViceLeader || rank <= GuildRankSettings.ViceLeader))
            return false;

        return await HasPermissionAsync(store, actor, GuildRankSettings.AppointMembersPermission, ct).ConfigureAwait(false);
    }

    private static async ValueTask<bool> HasPermissionAsync(IGuildDomainStore store, GuildMembership actor,
        uint permission, CancellationToken ct)
    {
        if (actor.Member.IsLeader)
            return true;
        var ranks = await ReadEffectiveRanksAsync(store, actor.Guild.Id, ct).ConfigureAwait(false);
        return (ranks[actor.Member.Rank].PermissionMask & permission) != 0;
    }

    public static async ValueTask<IReadOnlyList<GuildRankSetting>> ReadEffectiveRanksAsync(IGuildDomainStore store, GuildId guild, CancellationToken ct)
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
}
