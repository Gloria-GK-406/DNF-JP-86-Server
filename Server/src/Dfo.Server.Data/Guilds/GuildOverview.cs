namespace Dfo.Server.Data.Guilds;

public sealed record GuildOverview(
    Guild Guild, IReadOnlyList<GuildMember> Members, int AttendanceCount, IReadOnlyList<GuildRankSetting> Ranks,
    IReadOnlyList<GuildPurchasedContent> Contents, GuildAlliancePeer? Ally, GuildContributionTotals Contributions);
