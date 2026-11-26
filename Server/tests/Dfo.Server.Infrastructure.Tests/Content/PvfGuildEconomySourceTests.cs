using Dfo.Server.Infrastructure.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

public sealed class PvfGuildEconomySourceTests
{
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
}
