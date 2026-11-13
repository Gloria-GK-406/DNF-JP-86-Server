using Dfo.Server.Abstractions.Auction;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Auction;
using Dfo.Server.Data.Inventory;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Dfo.Server.Application.Tests.Auction;

public sealed class AuctionRuleServiceTests
{
    private static AuctionRuleService Rules(AuctionItemDefinition? definition = null, bool sendable = true) =>
        new(new Definitions(definition), new Transfers(sendable), new ConfigurationBuilder().Build());

    [Fact]
    public void The_item_market_has_the_70_terms()
    {
        var rule = Rules().For(AuctionMarket.Items);
        Assert.Equal((true, 10, TimeSpan.FromHours(24), 10_000, 500),
            (rule.Open, rule.ListingLimit, rule.Duration, rule.Deposit, rule.FeeRateBasisPoints));
    }

    [Fact]
    public void The_coupon_gives_thirty_listings_and_a_three_percent_fee()
    {
        var rule = Rules().For(AuctionMarket.Items, hasCoupon: true);
        Assert.Equal((30, 500 - 200, 10_000), (rule.ListingLimit, rule.FeeRateBasisPoints, rule.Deposit));
        Assert.Equal((10, 100), (Rules().For(AuctionMarket.GoldConsignment, true).ListingLimit, Rules().For(AuctionMarket.GoldConsignment, true).FeeRateBasisPoints));
    }

    [Theory]
    [InlineData(24u, false, 24)]
    [InlineData(48u, false, 24)]
    [InlineData(48u, true, 48)]
    [InlineData(24u, true, 24)]
    [InlineData(0u, true, 24)]
    public void Forty_eight_hours_need_the_coupon(uint requested, bool coupon, int hours) =>
        Assert.Equal(TimeSpan.FromHours(hours), Rules().ListingDuration(requested, coupon));

    [Fact]
    public async Task A_cera_item_may_be_listed_with_the_coupon() =>
        Assert.Equal(AuctionRefusal.None, await Rules(Packagable with { CashPrice = 100 }).CheckEligibilityAsync(Sword, true, default));

    [Theory]
    [InlineData(1000, null, 1050)]
    [InlineData(10, null, 11)]
    [InlineData(1, null, 2)]
    [InlineData(1000, 1020L, 1020)]
    public void The_next_bid_is_five_percent_over_never_stricter_than_the_client_and_capped_by_the_buyout(long current, long? buyout, long minimum) =>
        Assert.InRange(Rules().MinimumNextBid(current, buyout), minimum - 1, minimum);

    [Fact]
    public void The_fee_is_floored_at_the_locked_rate() => Assert.Equal(52, Rules().Fee(1049, 500));

    [Theory]
    [InlineData(null, null, null, 1, false, AuctionRefusal.InvalidPrice)]
    [InlineData(5000L, 5000L, null, 1, false, AuctionRefusal.InvalidPrice)]
    [InlineData(1000L, 5000L, null, 1, false, AuctionRefusal.None)]
    [InlineData(null, 3000L, 100L, 30, true, AuctionRefusal.None)]
    [InlineData(null, 3001L, 100L, 30, true, AuctionRefusal.InvalidPrice)]
    [InlineData(1000L, 3000L, 100L, 30, true, AuctionRefusal.InvalidPrice)]
    [InlineData(null, 3000L, null, 2, false, AuctionRefusal.ExceedsHolding)]
    [InlineData(null, 3_000_000_000L, null, 1, false, AuctionRefusal.InvalidPrice)]
    public void Prices_are_checked_as_70_checks_them(long? start, long? buyout, long? unit, int quantity, bool pile, AuctionRefusal expected) =>
        Assert.Equal(expected, Rules().CheckPricing(AuctionMarket.Items, new(start, buyout, unit), quantity, pile));

    [Theory]
    [InlineData(40119, AuctionItemFamily.Stackable, true)]
    [InlineData(10101, AuctionItemFamily.Weapon, true)]
    [InlineData(10101, AuctionItemFamily.Armor, false)]
    [InlineData(12000, AuctionItemFamily.TitleName, true)]
    [InlineData(12002, AuctionItemFamily.TitleName, false)]
    [InlineData(13001, AuctionItemFamily.Stackable, true)]
    public void Category_nodes_select_their_family(int category, AuctionItemFamily family, bool expected) =>
        Assert.Equal(expected, Rules().InCategory((ushort)category, family));

    private static ItemSnapshot Sword => new() { ItemId = 123, LegacyKind = 1, Type = ItemTraits.Equipment, Count = 1 };
    private static readonly AuctionItemDefinition Packagable = new(true, 0, false, 2, 50, AuctionItemFamily.Weapon);

    [Fact]
    public async Task A_packagable_tradable_item_may_be_listed() =>
        Assert.Equal(AuctionRefusal.None, await Rules(Packagable).CheckEligibilityAsync(Sword, false, default));

    [Fact]
    public async Task Locked_untradable_unpackagable_timed_and_unknown_items_may_not()
    {
        Assert.Equal(AuctionRefusal.NotEligible, await Rules(Packagable).CheckEligibilityAsync(Sword with { EquipmentLockId = 1 }, false, default));
        Assert.Equal(AuctionRefusal.NotEligible, await Rules(Packagable, sendable: false).CheckEligibilityAsync(Sword, false, default));
        Assert.Equal(AuctionRefusal.NotEligible, await Rules(Packagable with { Packagable = false }).CheckEligibilityAsync(Sword, false, default));
        Assert.Equal(AuctionRefusal.NotEligible, await Rules(Packagable with { Timed = true }).CheckEligibilityAsync(Sword, false, default));
        Assert.Equal(AuctionRefusal.NotEligible, await Rules(null).CheckEligibilityAsync(Sword, false, default));
    }

    [Fact]
    public async Task A_cera_item_needs_a_coupon() =>
        Assert.Equal(AuctionRefusal.NeedsCoupon, await Rules(Packagable with { CashPrice = 100 }).CheckEligibilityAsync(Sword, false, default));

    private sealed class Definitions(AuctionItemDefinition? definition) : IAuctionItemDefinitionSource
    {
        public ValueTask<AuctionItemDefinition?> GetAsync(int itemId, CancellationToken cancellationToken) => ValueTask.FromResult(definition);
    }

    private sealed class Transfers(bool sendable) : IMailItemRuleService
    {
        public ValueTask<MailItemSendDecision> CanSendAsync(ItemSnapshot core, bool sameAccount, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new MailItemSendDecision(sendable ? MailItemSendRefusal.None : MailItemSendRefusal.TemplateTradeRestricted));
    }
}

public sealed class GoldConsignmentRuleTests
{
    private static AuctionRuleService Rules() => new(new NoDefinitions(), new AnyTransfer(), new ConfigurationBuilder().Build());

    [Fact]
    public void The_consignment_market_is_open_with_three_listings_for_a_day()
    {
        var rule = Rules().For(AuctionMarket.GoldConsignment);
        Assert.Equal((true, 3, TimeSpan.FromHours(24), 0), (rule.Open, rule.ListingLimit, rule.Duration, rule.Deposit));
    }

    [Theory]
    [InlineData(2681725, 1_000_000)]
    [InlineData(2681736, 30_000_000)]
    [InlineData(2683069, 40_000_000)]
    [InlineData(2683073, 200_000_000)]
    public void Tier_items_stand_for_their_gold(int item, int gold) => Assert.Equal(gold, Rules().ConsignmentTierGold(item));

    [Fact]
    public void Other_items_are_not_tiers() => Assert.Null(Rules().ConsignmentTierGold(2681762));

    [Theory]
    [InlineData(39, null)]
    [InlineData(40, 500)]
    [InlineData(150, 500)]
    [InlineData(151, 3000)]
    [InlineData(170, 4000)]
    [InlineData(180, 5000)]
    [InlineData(181, 9500)]
    public void The_fee_rate_follows_the_price_against_the_average(int price, int? rate) =>
        Assert.Equal(rate, Rules().ConsignmentFeeRate(price, 100, hasCoupon: false));

    [Fact]
    public void Just_over_one_hundred_eighty_percent_is_above_it() => Assert.Equal(9500, Rules().ConsignmentFeeRate(1801, 1000, false));

    [Fact]
    public void Without_trades_a_tier_is_measured_against_the_reference_per_million() =>
        Assert.Equal(20_000, Rules().ConsignmentReferencePrice(200_000_000));

    private sealed class NoDefinitions : IAuctionItemDefinitionSource
    {
        public ValueTask<AuctionItemDefinition?> GetAsync(int itemId, CancellationToken cancellationToken) => ValueTask.FromResult<AuctionItemDefinition?>(null);
    }

    private sealed class AnyTransfer : IMailItemRuleService
    {
        public ValueTask<MailItemSendDecision> CanSendAsync(ItemSnapshot core, bool sameAccount, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new MailItemSendDecision(MailItemSendRefusal.None));
    }
}
