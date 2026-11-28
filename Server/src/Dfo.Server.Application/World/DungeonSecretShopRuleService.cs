using System.Collections.Immutable;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.World;

internal sealed class DungeonSecretShopRuleService(IDungeonSecretShopContentSource content)
{
    public const int Delilah = 1000;

    public (int NpcId, DungeonSecretShopPool? Pool) Choose(DungeonLayout layout, int entryCount, Func<int, int> next)
    {
        if (layout.Mode.Kind != DungeonRunKind.Ordinary)
            return (Delilah, null);

        var catalog = content.Read();
        var level = catalog.Levels.FirstOrDefault(row => layout.BasisLevel >= row.Minimum && layout.BasisLevel <= row.Maximum);
        var dungeon = catalog.DungeonWeights.Where(row => row.Group == layout.DungeonId).ToArray();
        var weights = dungeon.Length != 0
            ? dungeon.Select(row => (row.NpcId, Weight: row.Weights[0]))
            : catalog.LevelWeights.Where(row => row.Group == level?.Id)
                .Select(row => (row.NpcId, Weight: row.Weights[Math.Clamp(entryCount, 1, row.Weights.Count) - 1]));
        var positive = weights.Where(row => row.Weight > 0).ToArray();
        if (positive.Length == 0)
            return (Delilah, null);

        var npc = Weighted(positive, row => row.Weight, next).NpcId;
        if (npc is not (1002 or 1003 or 1004))
            return (Delilah, null);
        var pool = catalog.Pools.FirstOrDefault(p => p.NpcId == npc && p.IsDungeon && p.Group == layout.DungeonId)
            ?? catalog.Pools.FirstOrDefault(p => p.NpcId == npc && !p.IsDungeon && p.Group == level?.Id);
        return pool is { DrawCount: > 0, Candidates.Count: > 0 } ? (npc, pool) : (Delilah, null);
    }

    public static DungeonSecretShopOffer DrawOffer(int npc, DungeonSecretShopPool pool, Func<int, int> next)
    {
        var remaining = pool.Candidates.ToList();
        var result = ImmutableArray.CreateBuilder<DungeonSecretShopItem>();
        while (result.Count < pool.DrawCount && remaining.Count > 0)
        {
            var selected = Weighted(remaining, row => row.Weight, next);
            result.Add(selected.Item);
            // 0x0128 identifies a row by item ID. Alternative candidates may share the same ID.
            remaining.RemoveAll(row => row.Item.ItemId == selected.Item.ItemId);
        }
        return new(npc, result.ToImmutable());
    }

    private static T Weighted<T>(IReadOnlyList<T> values, Func<T, int> weight, Func<int, int> next)
    {
        var roll = next(values.Sum(weight));
        foreach (var row in values)
        {
            roll -= weight(row);
            if (roll < 0)
                return row;
        }
        throw new InvalidOperationException("Secret shop roll exceeded its configured weights.");
    }
}
