namespace Dfo.Server.Data.Guilds;

public sealed record GuildDungeonReward(int MaximumRatio, int ItemId, int Count, int Contribution);

public sealed record GuildDungeonDefinition(int Id, int GaugeLimit, int GaugePerClear, int ConquestRatio,
    IReadOnlyList<int> Prerequisites, IReadOnlyList<GuildDungeonReward> Rewards)
{
    public int Ratio(int clears) => (int)(Math.Min(GaugeLimit, (long)clears * GaugePerClear) * 100 / GaugeLimit);
}

public sealed record GuildDungeonRepeatBonus(int ItemId, int Count, int LevelDifference)
{
    public const int FullProgress = 100;

    public int Gain(int participants, int levelSpread) =>
        5 * participants * (levelSpread >= LevelDifference ? 2 : 1);
}

public sealed record GuildDungeonCatalog(int StyleId, IReadOnlyDictionary<int, GuildDungeonDefinition> Dungeons)
{
    public GuildDungeonRepeatBonus? RepeatBonus { get; init; }
    public IReadOnlyDictionary<int, int> PartyBuffs { get; init; } = new Dictionary<int, int>();
}

public static class GuildDungeonExtensionTicket
{
    public const int ItemId = 10155510;
    public const string Action = "[guild dungeon time ticket]";
}

public sealed record GuildDungeonPeriod(long StartUnix, long EndUnix)
{
    public GuildDungeonPeriod SkipNextReset() =>
        new(StartUnix, DateTimeOffset.FromUnixTimeSeconds(EndUnix).ToOffset(TimeSpan.FromHours(8)).AddMonths(1).ToUnixTimeSeconds());

    public static GuildDungeonPeriod At(DateTimeOffset instant)
    {
        var local = instant.ToOffset(TimeSpan.FromHours(8));
        var start = new DateTimeOffset(local.Year, local.Month, 1, 6, 0, 0, local.Offset);
        if (local < start) start = start.AddMonths(-1);
        return new(start.ToUnixTimeSeconds(), start.AddMonths(1).ToUnixTimeSeconds());
    }
}

public sealed record GuildDungeonAdmission(GuildId Guild, int StyleId, GuildDungeonPeriod Period)
{
    public int MedalShopId
    {
        get
        {
            var start = DateTimeOffset.FromUnixTimeSeconds(Period.StartUnix).ToOffset(TimeSpan.FromHours(8));
            var month = start.Year * 12 + start.Month - 1;
            return GuildHallTown.FirstMedalShopId + (int)((Guild.Value + (long)month) % GuildHallTown.MedalShopCount);
        }
    }
}
public sealed record GuildDungeonProgress(int DungeonId, int ClearCount);
public sealed record GuildDungeonView(GuildDungeonAdmission Admission, IReadOnlyList<GuildDungeonProgress> Progress)
{
    public int? MedalShopId { get; init; }
}
