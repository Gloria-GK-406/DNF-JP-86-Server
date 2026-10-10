using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.UsageQuotas;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Application.World;

internal sealed class DimensionDungeonRuleService(IDimensionDungeonContentSource source) : IDimensionDungeonRuleService
{
    // A21 server policy inspired by TW70 independent drops, not copied native rates.
    private sealed record DifficultyPolicy(int EliteChance, int BossExtraChance, int SetWeight);
    private static readonly DifficultyPolicy[] Policies = [new(200000, 150000, 20),
        new(250000, 200000, 30), new(300000, 250000, 40), new(350000, 300000, 50), new(400000, 350000, 60)];
    private sealed record Prepared(DimensionDungeonContent Content, DimensionChroniclePool Pool);
    private readonly Lazy<Prepared> cache = new(() => Prepare(source.Load()));
    // Retain A21 fusion's explicitly rewarded elites even when declared with ordinary rank.
    private static readonly HashSet<int> Elites = [61340, 56108, 62940, 56507, 107000106, 61412, 61347,
        107000107, 56506, 61353, 56140, 56715, 56146, 59009, 56721, 56448, 61238, 56151, 56453, 59388, 61391];
    public int Initialize() => cache.Value.Content.DungeonIds.Count;
    public bool IsDimension(int dungeonId) => cache.Value.Content.DungeonIds.Contains(dungeonId);
    public DimensionQuotaSnapshot Snapshot(DateOnly period, int used)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(used);
        var config = cache.Value.Content;
        return new(period, used, Math.Max(0, config.DailyEntries - used),
            Math.Max(0, config.ExtraEntries - Math.Max(0, used - config.DailyEntries)));
    }
    public IReadOnlyList<RolledDrop> DrawMonster(MonsterDropRequest request, ulong seed)
    {
        if (!IsDimension(request.Context.DungeonId) || request.Context.SourceType != IndependentDropSourceType.Monster)
            return [];
        if ((int)request.Context.Difficulty >= Policies.Length)
            throw new ArgumentOutOfRangeException(nameof(request), "Unknown dimension difficulty.");
        var boss = request.MonsterRank == 3;
        if (!boss && request.MonsterRank is not (1 or 2) && !Elites.Contains(request.MonsterCode)) return [];
        var policy = Policies[(int)request.Context.Difficulty];
        var random = new DropRandom(seed);
        var result = new List<RolledDrop>();
        // Called once per recipient: never multiply these trials by party size again.
        var count = boss ? 1 : 0;
        count += IndependentDropRules.SuccessfulAttempts(1, 1,
            boss ? policy.BossExtraChance : policy.EliteChance, 1000000, random);
        for (var i = 0; i < count; i++)
            if (Pick(cache.Value.Pool, policy.SetWeight, random) is { } item) result.Add(item);
        // Materials are a separate source and do not depend on the equipment chance.
        result.Add(cache.Value.Content.Fragment with { Count = boss ? 2 : 1 });
        return result;
    }
    public ClearRewardCardFace? DrawCard(int dungeonId, ClearRewardCardFace ordinary, ulong seed)
    {
        if (!IsDimension(dungeonId) || ordinary.Kind == ClearRewardCardKind.BlackDiamond) return null;
        var pool = cache.Value.Pool;
        var candidates = ordinary.Kind == ClearRewardCardKind.Paid ? pool.Set : pool.Normal;
        var random = new DropRandom(seed ^ 0x44494D454E53494FUL);
        return ordinary with { Slots = ordinary.Slots.Select(slot =>
        {
            if (candidates.Count == 0) return slot with { Item = null };
            var item = candidates[random.Next(candidates.Count) - 1];
            return slot with { Item = new ClearRewardCardItem(item.ItemId, 1, item.Rarity, item.Durability) };
        }).ToArray() };
    }

    private static RolledDrop? Pick(DimensionChroniclePool pool, int setWeight, DropRandom random)
    {
        // Category weights are independent of category size; every item within a category
        // has equal weight. A duplicated career declaration must not make an item more common.
        var candidates = pool.Normal.Count == 0 ? pool.Set : pool.Set.Count == 0 ? pool.Normal
            : random.Hits(setWeight, 100) ? pool.Set : pool.Normal;
        return candidates.Count == 0 ? null : candidates[random.Next(candidates.Count) - 1];
    }

    private static Prepared Prepare(DimensionDungeonContent content)
    {
        var set = content.Pools.Values.SelectMany(pool => pool.Set).DistinctBy(item => item.ItemId)
            .OrderBy(item => item.ItemId).ToArray();
        var setIds = set.Select(item => item.ItemId).ToHashSet();
        var normal = content.Pools.Values.SelectMany(pool => pool.Normal)
            .Where(item => !setIds.Contains(item.ItemId)).DistinctBy(item => item.ItemId)
            .OrderBy(item => item.ItemId).ToArray();
        return new(content, new(normal, set));
    }
}
