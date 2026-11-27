using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Data.Guilds;

namespace Dfo.Server.Application.Guilds;

internal sealed class GuildDungeonRuleService(IGuildDungeonContentSource content)
{
    public GuildDungeonCatalog? Catalog() => content.Read();
    public bool Supports(int dungeon) => Catalog()?.Dungeons.ContainsKey(dungeon) == true;

    public static GuildDungeonPeriod CalendarPeriodAt(DateTimeOffset now) => GuildDungeonPeriod.At(now);

    public static GuildDungeonPeriod? EvaluateExtension(GuildDungeonPeriod calendar, GuildDungeonPeriod current,
        bool isLeader, int pointCost) =>
        isLeader && pointCost > 0 && current == calendar ? calendar.SkipNextReset() : null;

    public static int? PartyBuffFor(GuildDungeonCatalog catalog, int participantCount) =>
        catalog.PartyBuffs.TryGetValue(participantCount, out var buffId) ? buffId : null;

    public static bool PrerequisitesMet(GuildDungeonCatalog catalog, GuildDungeonDefinition dungeon,
        IReadOnlyDictionary<int, int> counts) => dungeon.Prerequisites.All(id =>
        catalog.Dungeons[id].Ratio(counts.GetValueOrDefault(id)) >= catalog.Dungeons[id].ConquestRatio);

    public static (int Contribution, GuildItemReward Reward) RewardFor(GuildDungeonDefinition dungeon, int clears, DateTimeOffset now)
    {
        var ratio = dungeon.Ratio(clears);
        var row = dungeon.Rewards.First(row => ratio <= row.MaximumRatio);
        return (row.Contribution, MailReward(row.ItemId, row.Count, now));
    }

    public static (int NextProgress, GuildItemReward? Reward) EvaluateRepeatBonus(
        GuildDungeonRepeatBonus bonus, int currentProgress, IReadOnlyList<int> entryLevels, DateTimeOffset now)
    {
        var next = currentProgress + bonus.Gain(entryLevels.Count, entryLevels.Max() - entryLevels.Min());
        return next >= GuildDungeonRepeatBonus.FullProgress ? (0, MailReward(bonus.ItemId, bonus.Count, now)) : (next, null);
    }

    private static GuildItemReward MailReward(int itemId, int count, DateTimeOffset now) =>
        new(new(itemId, count), now.AddDays(30));

    public static int? MedalShopFor(GuildDungeonCatalog catalog, GuildDungeonAdmission admission,
        IReadOnlyDictionary<int, int> counts) =>
        catalog.Dungeons.TryGetValue(GuildHallTown.MedalShopUnlockDungeonId, out var dungeon)
        && dungeon.Ratio(counts.GetValueOrDefault(dungeon.Id)) == 100 ? admission.MedalShopId : null;
}
