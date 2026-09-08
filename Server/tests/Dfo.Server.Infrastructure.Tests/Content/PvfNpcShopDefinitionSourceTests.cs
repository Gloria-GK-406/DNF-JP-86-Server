using Dfo.Server.Abstractions.Content;
using Dfo.Server.Infrastructure.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

public sealed class PvfNpcShopDefinitionSourceTests
{
    [SkippableFact]
    public async Task The_two_sampled_npcs_resolve_their_actual_shop_pages()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfNpcShopDefinitionSource(provider);
        var seria = await source.GetShopAsync(2, CancellationToken.None);
        var tana = await source.GetShopAsync(302, CancellationToken.None);

        Assert.NotNull(seria);
        Assert.Equal(15, seria.ShopId);
        Assert.Contains(seria.Pages, page => page.Job is null && page.ItemIds.Contains(490003532));
        Assert.NotNull(tana);
        Assert.Equal(87, tana.ShopId);
        Assert.Contains(tana.Pages, page => page.Job == 12 && page.ItemIds.Contains(27312));
        Assert.DoesNotContain(tana.Pages, page => page.Job == 0 && page.ItemIds.Contains(27312));
        Assert.Null(await source.GetShopAsync(int.MaxValue, CancellationToken.None));
    }

    [SkippableFact]
    public async Task Sampled_prices_and_limit_flags_are_read_without_turning_value_into_gold_cost()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfNpcShopDefinitionSource(provider);
        var sword = await source.GetItemAsync(27312, CancellationToken.None);
        var potion = await source.GetItemAsync(490003532, CancellationToken.None);
        var book = await source.GetItemAsync(490003534, CancellationToken.None);

        Assert.NotNull(sword);
        Assert.True(sword.IsReadable);
        Assert.Equal(18000, sword.GoldPrice);
        Assert.True(sword.Durability > 0);
        Assert.Empty(sword.Materials);
        Assert.NotNull(potion);
        Assert.True(potion.IsReadable);
        Assert.Null(potion.GoldPrice);
        Assert.Equal(new NpcShopMaterialCost(490003528, 1), Assert.Single(potion.Materials));
        Assert.Null(potion.Limit);
        Assert.NotNull(book);
        Assert.True(book.IsReadable);
        Assert.Equal(new NpcShopMaterialCost(490003528, 35), Assert.Single(book.Materials));
        Assert.Equal(new NpcShopPurchaseLimit(NpcShopLimitScope.Account, 1, false), book.Limit);
    }
}
