using Dfo.Server.Infrastructure.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

public sealed class MarriageHousingContentTests
{
    [SkippableFact]
    public async Task Current_archive_resolves_every_furniture_anchor_tree_currency_and_native_gate()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var rules = new PvfMarriageHousingContentSource(provider).Load();
        Assert.Equal(75, rules.Furniture.Count);
        Assert.Equal(5140, rules.Furniture.Values.Sum(x => x.PointCost));
        Assert.Equal(Enumerable.Range(0, 28).Except([11, 24]), rules.Furniture.Values.SelectMany(x => x.Slots).Distinct().Order());
        Assert.False(rules.Furniture[10002797].CanRecall);
        Assert.Equal(1, rules.Seeds[10002766]); Assert.Equal(2, rules.Seeds[10002768]); Assert.Equal(3, rules.Seeds[10002767]);
        Assert.Equal(10, rules.Water[10002765]); Assert.Equal(20, rules.Water[10002905]);
        Assert.Equal(100000, rules.Rewards.Sum(x => x.Weight));
        Assert.Equal(new[] { 10, 40, 100, 200, 500, 800, 1000 }, rules.RoomAttributes.Select(x => x.Threshold));
        Assert.Equal(Enumerable.Range(0, 6), rules.RingAttributes.Select(x => x.Threshold));
        Assert.Equal(14, rules.EntrySources.Count); Assert.Contains((2, 0), rules.EntrySources); Assert.DoesNotContain((2, 1), rules.EntrySources);
        var items = new PvfItemPlacementDefinitionSource(provider);
        var shops = new PvfNpcShopDefinitionSource(provider);
        foreach (var furniture in rules.Furniture.Values)
        {
            var placement = await items.GetAsync(furniture.ItemId, default);
            Assert.NotNull(placement); Assert.Equal(Dfo.Server.Abstractions.Content.ItemWorldClass.Interchangeable, placement.WorldClass);
            var price = await shops.GetItemAsync(furniture.ItemId, default);
            Assert.NotNull(price); Assert.True(price.IsReadable);
            Assert.Equal(furniture.PointCost, Assert.Single(price.Materials).Count);
        }
    }
}
