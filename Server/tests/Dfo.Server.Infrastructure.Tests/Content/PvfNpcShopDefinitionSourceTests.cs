using Dfo.Server.Abstractions.Content;
using Dfo.Server.Infrastructure.Content;
using Dfo.Pvf.Tokens;

namespace Dfo.Server.Infrastructure.Tests.Content;

public sealed class PvfNpcShopDefinitionSourceTests
{
    [SkippableFact]
    public async Task Wedding_shop_ignores_empty_grid_cells_and_keeps_real_goods_and_prices()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfNpcShopDefinitionSource(provider);
        var shop = await source.GetShopAsync(20013, default);
        Assert.NotNull(shop);
        Assert.Equal(1208, shop.ShopId);
        Assert.Equal(new[] { 10002610, 10002616, 10002625, 10002626 }, shop.Pages.SelectMany(x => x.ItemIds).Distinct().Order().ToArray());
        Assert.Equal(1000, (await source.GetItemAsync(10002625, default))!.GoldPrice);
        Assert.Equal(1000, (await source.GetItemAsync(10002626, default))!.GoldPrice);
    }

    [SkippableFact]
    public async Task Delilah_simple_shop_is_available_only_after_dungeon_clear()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfNpcShopDefinitionSource(provider);
        var shop = await source.GetShopAsync(1000, default);
        Assert.NotNull(shop);
        Assert.True(shop.VisibleOnDungeonClear);
        Assert.Equal(1000, shop.ShopId);
        Assert.Contains(shop.Pages, page => page.ItemIds.Contains(2600655));
        Assert.False((await source.GetShopAsync(2, default))!.VisibleOnDungeonClear);
        var drapyus = await source.GetShopAsync(1001, default);
        Assert.NotNull(drapyus);
        Assert.Equal(1001, drapyus.ShopId);
        Assert.False(drapyus.VisibleOnDungeonClear);
        Assert.Empty(drapyus.Pages.SelectMany(page => page.ItemIds));
    }
    [SkippableTheory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public void Only_a_closed_opening_reward_block_is_excluded_from_purchase_conditions(
        bool insideBooster, bool closed, bool readable)
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var sections = new List<PvfNpcShopDefinitionSource.Section>
            { new("[price]", [new(PvfTokenKind.Int32, 100)]) };
        if (insideBooster)
            sections.Add(new("[booster info]", []));
        sections.Add(new("[cera]", [new(PvfTokenKind.Int32, 1)]));
        sections.Add(new("[/cera]", []));
        if (closed)
            sections.Add(new("[/booster info]", []));
        var result = PvfNpcShopDefinitionSource.ParseItem(provider.Archive, 1, sections);
        Assert.Equal(readable, result.IsReadable);
        Assert.Equal(100, result.GoldPrice);
    }

    [SkippableFact]
    public async Task Resurrection_coin_box_costs_materials_even_though_its_opening_reward_contains_cera()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfNpcShopDefinitionSource(provider);
        var box = await source.GetItemAsync(490003531, CancellationToken.None);

        Assert.NotNull(box);
        Assert.True(box.IsReadable);
        Assert.Null(box.GoldPrice);
        Assert.Equal(new NpcShopMaterialCost(490003528, 2), Assert.Single(box.Materials));
        Assert.Equal(new NpcShopPurchaseLimit(NpcShopLimitScope.Account, 10, false), box.Limit);
        var placement = await new PvfItemPlacementDefinitionSource(provider, new Dfo.Server.Abstractions.Inventory.InventorySlotMappingRuleService()).GetAsync(490003531, default);
        Assert.NotNull(placement);
        Assert.Equal(2, placement.ItemKind);
    }

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
