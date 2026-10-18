using Dfo.Server.Abstractions.Content;
using Dfo.Server.Infrastructure.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

public sealed class HerasContentTests
{
    [SkippableFact]
    public async Task Archive_has_the_approved_grid_and_coupon_operands()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var heras = await new PvfHerasContentSource(provider, new PvfDungeonDefinitionSource(provider)).ReadAsync(default);
        Assert.Equal(3, heras.DailyEntries);
        Assert.Equal(9, Assert.Single(heras.Dungeon.Mazes).Cells.Count);
        var coupon = await new PvfStackableActionDefinitionSource(provider).GetAsync(490002847, default);
        Assert.NotNull(coupon);
        Assert.True(coupon.IsReadable);
        Assert.Equal(3900, coupon.EntryDungeonId);
        Assert.Equal(1, coupon.EntryIncrease);
    }

    [SkippableFact]
    public async Task Yinha_shop_uses_parts_and_recognizes_only_the_documented_character_limit_alias()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfNpcShopDefinitionSource(provider);
        var shop = await source.GetShopAsync(20152, default);
        Assert.NotNull(shop);
        Assert.Equal(1277, shop.ShopId);
        Assert.Contains(shop.Pages, p => p.ItemIds.Contains(300340000));
        var equipment = await source.GetItemAsync(300340000, default);
        Assert.NotNull(equipment);
        Assert.True(equipment.IsReadable);
        Assert.Equal(new NpcShopMaterialCost(490002846, 85), Assert.Single(equipment.Materials));
        var fatigue = await source.GetItemAsync(490002849, default);
        Assert.NotNull(fatigue);
        Assert.True(fatigue.IsReadable);
        Assert.Equal(new NpcShopPurchaseLimit(NpcShopLimitScope.Character, 1, true), fatigue.Limit);
    }
}
