using Dfo.Server.Abstractions.Content;
using Dfo.Server.Infrastructure.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

public sealed class PvfInventoryExpansionDefinitionSourceTests
{
    [SkippableFact]
    public async Task Account_tiers_keep_every_cost_and_gold_limit_including_late_paid_stages()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfInventoryExpansionDefinitionSource(provider);
        var definition = await source.AccountCargoAsync(default);
        Assert.Equal(60, definition.RequiredLevel);
        Assert.Equal(new[] { 1,8,16,24,32,40,48,56,64,72,80,88,96,104,112,120 }, definition.Tiers.Select(t => t.Capacity));
        Assert.Equal(new AccountCargoTier(1,10000000,-1,0,100000,-1), definition.Tiers[0]);
        Assert.Equal(new AccountCargoTier(24,200000000,3299,250,0,-1), definition.Tiers[3]);
        var catalog = new PvfCeraShopDefinitionSource(provider);
        foreach (var tier in definition.Tiers.Where(t => t.ProductId > 0))
        {
            var product = await catalog.GetProductAsync(tier.ProductId, default);
            Assert.NotNull(product);
            Assert.Empty(product.Diagnostics);
            Assert.True(product.IsService);
            Assert.True(product.Price!.Cera > 0);
            Assert.Equal(new InventoryExpansionGrant(InventoryExpansionKind.AccountCargo,tier.Capacity,tier.ProductId),
                await source.ProductAsync(product.ProductId, product.ItemId, default));
        }
    }
    [SkippableFact]
    public async Task Cargo_rules_resolve_real_equipment_and_stackable_declarations()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfCargoItemDefinitionSource(provider);
        Assert.NotNull(await source.GetAsync(20002, default));
        Assert.NotNull(await source.GetAsync(3299, default));
        Assert.Null(await source.GetAsync(int.MaxValue, default));
    }

    [SkippableTheory]
    [InlineData(50, InventoryExpansionKind.PersonalCargo,24)]
    [InlineData(10098633, InventoryExpansionKind.PersonalCargo,200)]
    [InlineData(2660296, InventoryExpansionKind.Main,8)]
    [InlineData(2660297, InventoryExpansionKind.Main,16)]
    [InlineData(2683675, InventoryExpansionKind.Main,24)]
    [InlineData(2683901, InventoryExpansionKind.Avatar,7)]
    [InlineData(2683915, InventoryExpansionKind.Avatar,105)]
    public async Task Upgrade_item_identity_resolves_explicit_a21_target(int item, InventoryExpansionKind kind, int parameter)
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        Assert.Equal(new InventoryExpansionGrant(kind,parameter), await new PvfInventoryExpansionDefinitionSource(provider).ItemAsync(item,default));
    }
}
