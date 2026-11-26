namespace Dfo.Server.Data.Guilds;

public sealed record GuildRankSetting(byte Rank, uint PermissionMask, string Name);

public sealed record GuildRankOverride(byte Rank, uint? PermissionMask, string? Name);

public static class GuildRankSettings
{
    public const byte Leader = 1;
    public const byte LastRank = 5;
    public const int MaximumNameBytes = 23;
    public const uint ValidPermissionMask = ushort.MaxValue;
    public const uint LeaderDefaultPermissions = 0x8DF6;

    public static IReadOnlyList<GuildRankSetting> Defaults { get; } = Array.AsReadOnly<GuildRankSetting>(
    [
        new(0, 0, ""),
        new(Leader, LeaderDefaultPermissions, "会长"),
        new(2, 0, "副会长"),
        new(3, 0, "优秀成员"),
        new(4, 0, "普通成员"),
        new(5, 0, "精英成员"),
    ]);

    public static bool IsMemberRank(byte rank) => rank is >= Leader and <= LastRank;
}
