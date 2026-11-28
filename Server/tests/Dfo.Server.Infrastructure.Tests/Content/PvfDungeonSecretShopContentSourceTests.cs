using Dfo.Server.Infrastructure.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

public sealed class PvfDungeonSecretShopContentSourceTests
{
    [SkippableFact]
    public void A21_secret_shop_content_loads_real_weights_and_both_currencies()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var catalog = PvfDungeonSecretShopContentSource.Load(provider.Archive);

        Assert.Equal(59, catalog.DungeonWeights.Select(row => row.Group).Distinct().Count());
        Assert.Equal(9, catalog.Levels.Count);
        Assert.Equal(new[] { 1000, 1002, 1003, 1004 },
            catalog.DungeonWeights.Where(row => row.Group == 33).Select(row => row.NpcId));
        Assert.Equal(new[] { 9265, 30, 700, 5 },
            catalog.DungeonWeights.Where(row => row.Group == 33).Select(row => Assert.Single(row.Weights)));

        Assert.Contains(catalog.Pools.SelectMany(pool => pool.Candidates), candidate =>
            candidate.Item is { ItemId: 22163, UnitPrice: 3300, CostItemId: 600 });
        Assert.Contains(catalog.Pools.SelectMany(pool => pool.Candidates), candidate =>
            candidate.Item is { ItemId: 3033, UnitPrice: 100, CostItemId: 0, RemainingCount: 100 });
        Assert.Contains(catalog.Pools, pool => pool is { NpcId: 1004, DrawCount: 1, IsDungeon: true });
    }
}
