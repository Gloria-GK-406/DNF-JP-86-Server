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
