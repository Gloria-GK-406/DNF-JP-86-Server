using Dfo.Server.Data.Guilds;
using Dfo.Server.Infrastructure.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

public sealed class PvfGuildEconomySourceTests
{
    [SkippableFact]
    public void Reads_A21_guild_benefits_and_warehouse_expansions()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var catalog = new PvfGuildEconomySource(provider).ReadContentCatalog();
        Assert.NotNull(catalog);
        Assert.Equal(36_000, catalog.Products[1].Price);
        Assert.Equal(300, catalog.Products[13].Price);
        Assert.Equal(10, catalog.Products[13].Value);
        Assert.Equal(100, catalog.Products[14].Value);
        Assert.Equal(1_000, catalog.Products[15].Value);
        Assert.Equal(new[] { 1, 7, 30 }, new uint[] { 2, 5, 8 }.Select(id => catalog.Products[id].DurationDays));
        Assert.Equal(new[] { 1, 7, 30 }, new uint[] { 3, 6, 9 }.Select(id => catalog.Products[id].DurationDays));
        Assert.Equal(167, catalog.TemporaryAttributes!.BuffId);
        Assert.Equal(10, catalog.Products[3].Value);
        Assert.Equal(new uint[] { 2, 3 }, catalog.Products.Values.Where(product => product.CanGift)
            .Select(product => product.ProductId).Order().ToArray());
        Assert.Equal(56, catalog.WarehouseCapacityLimit);
        Assert.Equal(16, catalog.Products[11].Value);
        Assert.Equal(8, catalog.Products[12].Value);
        Assert.Equal(169, catalog.Attributes!.BuffId);
        Assert.Equal(60, catalog.Attributes.PhysicalAttack);
        Assert.Equal(60, catalog.Attributes.PhysicalDefense);
        Assert.Equal(60, catalog.Attributes.MagicalAttack);
        Assert.Equal(60, catalog.Attributes.MagicalDefense);
        Assert.False(catalog.Products.ContainsKey(4));
    }

    [SkippableFact]
    public void Reads_the_A21_gold_rename_offer_from_additional_guild_contents()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var catalog = new PvfGuildEconomySource(provider).ReadContentCatalog();
        Assert.NotNull(catalog);
        Assert.NotNull(catalog.GoldRename);
        Assert.Equal(22u, catalog.GoldRename.ProductId);
        Assert.Equal(10_000_000, catalog.GoldRename.GoldCost);
        Assert.False(catalog.Products.ContainsKey(22));
    }

    [SkippableFact]
    public void Reads_the_A21_paid_mark_threshold_and_price()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var pricing = new PvfGuildEconomySource(provider).ReadMarkPricing();
        Assert.NotNull(pricing);
        Assert.Equal(5, pricing.FirstPaidMark);
        Assert.Equal(0, pricing.Price(4));
        Assert.Equal(2000, pricing.Price(5));
    }

    [SkippableFact]
    public void Reads_the_A21_guild_creation_fee_from_the_archive()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        Assert.Equal(300_000, new PvfGuildEconomySource(provider).ReadGoldCost());
    }

    [SkippableFact]
    public void Reads_the_A21_gold_per_guild_donation_point_from_the_archive()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        Assert.Equal(1000, new PvfGuildEconomySource(provider).ReadDonationGoldPerPoint());
    }

    [SkippableFact]
    public void Reads_the_A21_monthly_contribution_reward_and_recommended_clear_score()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var rule = Assert.IsType<GuildMonthlyContributionRule>(new PvfGuildEconomySource(provider).ReadMonthlyContribution());
        Assert.Equal(new GuildMonthlyContributionRule(10, 1100, 10153528, 1), rule);
    }
}
