using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Data.Guilds;

namespace Dfo.Server.Application.Guilds;

internal sealed class GuildDungeonRuleService(IGuildDungeonContentSource content)
{
    public GuildDungeonCatalog? Catalog() => content.Read();
    public bool Supports(int dungeon) => Catalog()?.Dungeons.ContainsKey(dungeon) == true;

    public static bool PrerequisitesMet(GuildDungeonCatalog catalog, GuildDungeonDefinition dungeon,
        IReadOnlyDictionary<int, int> counts) => dungeon.Prerequisites.All(id =>
        catalog.Dungeons[id].Ratio(counts.GetValueOrDefault(id)) >= catalog.Dungeons[id].ConquestRatio);

    public static GuildDungeonReward RewardFor(GuildDungeonDefinition dungeon, int clears)
    {
        var ratio = dungeon.Ratio(clears);
        return dungeon.Rewards.First(row => ratio <= row.MaximumRatio);
    }

    public static int? MedalShopFor(GuildDungeonCatalog catalog, GuildDungeonAdmission admission,
        IReadOnlyDictionary<int, int> counts) =>
        catalog.Dungeons.TryGetValue(GuildHallTown.MedalShopUnlockDungeonId, out var dungeon)
        && dungeon.Ratio(counts.GetValueOrDefault(dungeon.Id)) == 100 ? admission.MedalShopId : null;
}
