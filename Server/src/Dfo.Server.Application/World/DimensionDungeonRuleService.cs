using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.UsageQuotas;

namespace Dfo.Server.Application.World;

internal sealed class DimensionDungeonRuleService(IDimensionDungeonContentSource source) : IDimensionDungeonRuleService
{
    // UpstreamStatic: DimensionDropSystem. These are a playable reference policy, not native drop rates.
    private static readonly HashSet<int> Elites = [61340, 56108, 62940, 56507, 107000106, 61412, 61347,
        107000107, 56506, 61353, 56140, 56715, 56146, 59009, 56721, 56448, 61238, 56151, 56453, 59388, 61391];
    private static readonly HashSet<int> Bosses = [61809, 61128, 61135, 62141];
    public int Initialize() => source.Load().DungeonIds.Count;
    public bool IsDimension(int dungeonId) => source.Load().DungeonIds.Contains(dungeonId);
    public DimensionQuotaSnapshot Snapshot(DateOnly period, int used)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(used);
        var config = source.Load();
        return new(period, used, Math.Max(0, config.DailyEntries - used),
            Math.Max(0, config.ExtraEntries - Math.Max(0, used - config.DailyEntries)));
    }
    public IReadOnlyList<RolledDrop> DrawMonster(int dungeonId, int monsterId, int job, int growType, ulong seed)
    {
        if (!IsDimension(dungeonId) || !Bosses.Contains(monsterId) && !Elites.Contains(monsterId)) return [];
        var content = source.Load();
        if (!content.Pools.TryGetValue((job, growType & 15), out var pool)) return [];
        var random = new DropRandom(seed);
        var result = new List<RolledDrop>();
        void Pick(IReadOnlyList<RolledDrop> items) { if (items.Count > 0) result.Add(items[random.Next(items.Count) - 1]); }
        if (Bosses.Contains(monsterId)) { Pick(pool.Normal); Pick(pool.Set); }
        else Pick(pool.Normal.Concat(pool.Set).ToArray());
        result.Add(content.Fragment with { Count = Bosses.Contains(monsterId) ? 2 : 1 });
        return result;
    }
    public ClearRewardCardFace? DrawCard(int dungeonId, int job, int growType, ClearRewardCardFace ordinary, ulong seed)
    {
        if (!IsDimension(dungeonId) || ordinary.Kind == ClearRewardCardKind.BlackDiamond) return null;
        if (!source.Load().Pools.TryGetValue((job, growType & 15), out var pool))
            return ordinary with { Slots = ordinary.Slots.Select(s => s with { Item = null }).ToArray() };
        var candidates = ordinary.Kind == ClearRewardCardKind.Paid ? pool.Set : pool.Normal;
        var random = new DropRandom(seed ^ 0x44494D454E53494FUL);
        return ordinary with { Slots = ordinary.Slots.Select(slot =>
        {
            if (candidates.Count == 0) return slot with { Item = null };
            var item = candidates[random.Next(candidates.Count) - 1];
            return slot with { Item = new ClearRewardCardItem(item.ItemId, 1, item.Rarity, item.Durability) };
        }).ToArray() };
    }
}
