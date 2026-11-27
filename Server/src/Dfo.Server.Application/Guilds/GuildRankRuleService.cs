using Dfo.Server.Data.Guilds;

namespace Dfo.Server.Application.Guilds;

internal static class GuildRankRuleService
{
    public static bool AllowsRankName(byte rank, string name) =>
        GuildRankSettings.IsMemberRank(rank) && GuildTextRules.IsValidRankName(name);

    public static GuildLeadershipTransfer? EvaluateLeadershipTransfer(GuildMember actor, GuildMember target) =>
        actor.IsLeader && !target.IsLeader && actor.Guild == target.Guild && actor.Character != target.Character
            ? new(actor with { IsLeader = false, MemberRank = GuildRankSettings.Member },
                target with { IsLeader = true, MemberRank = GuildRankSettings.Member })
            : null;

    public static bool CanTransferWarehouseItems(GuildMember member) =>
        member.Rank is >= GuildRankSettings.Leader and < GuildRankSettings.Member;

    public static bool CanReviewApplications(GuildMember actor, IReadOnlyList<GuildRankSetting> ranks) =>
        HasPermission(actor, ranks, GuildRankSettings.ReviewApplicationsPermission);

    public static uint? NormalizePermissions(byte rank, uint permissions)
    {
        if (!GuildRankSettings.IsMemberRank(rank) || (permissions & ~GuildRankSettings.ValidPermissionMask) != 0)
            return null;
        return rank == GuildRankSettings.Leader ? permissions | GuildRankSettings.LeaderDefaultPermissions : permissions;
    }

    public static bool CanAssignRank(GuildMember actor, GuildMember target, byte rank, IReadOnlyList<GuildRankSetting> ranks)
    {
        if (rank is < GuildRankSettings.ViceLeader or > GuildRankSettings.LastRank || target.IsLeader || target.Character == actor.Character)
            return false;
        if (!actor.IsLeader && (actor.Rank != GuildRankSettings.ViceLeader
            || target.Rank <= GuildRankSettings.ViceLeader || rank <= GuildRankSettings.ViceLeader))
            return false;

        return HasPermission(actor, ranks, GuildRankSettings.AppointMembersPermission);
    }

    private static bool HasPermission(GuildMember actor, IReadOnlyList<GuildRankSetting> ranks, uint permission) =>
        actor.IsLeader || (ranks[actor.Rank].PermissionMask & permission) != 0;

    public static IReadOnlyList<GuildRankSetting> EffectiveRanks(IReadOnlyList<GuildRankOverride> overrides)
    {
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
