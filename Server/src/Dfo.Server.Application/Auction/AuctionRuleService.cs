using Dfo.Server.Abstractions.Auction;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;
using Microsoft.Extensions.Configuration;

namespace Dfo.Server.Application.Auction;

/// <summary>The auction house's terms. The one place PVF and auction configuration are read.</summary>
/// <remarks>
/// Item market terms are the 70 server's (70Reference, df_auction_r / df_game_r, 2026-11-13): 10
/// listings, 24 hours, a 10000 gold deposit, 5% fee. The listing limit counts per character, the
/// coupon and VIP terms are not carried yet (user decisions D1, D2, D7 of #324).
/// </remarks>
internal sealed class AuctionRuleService : IAuctionRuleService
{
    private readonly IAuctionItemDefinitionSource definitions;
    private readonly IMailItemRuleService transfers;
    private readonly AuctionMarketRule items;
    private readonly AuctionMarketRule gold;

    public AuctionRuleService(IAuctionItemDefinitionSource definitions, IMailItemRuleService transfers, IConfiguration configuration)
    {
        this.definitions = definitions;
        this.transfers = transfers;
        items = new(AuctionMarket.Items, configuration.GetValue("Auction:Items:Enabled", true),
            ListingLimit: 10, Duration: TimeSpan.FromHours(24), Deposit: 10_000, FeeRateBasisPoints: 500);
        gold = new(AuctionMarket.GoldConsignment, configuration.GetValue("Auction:GoldConsignment:Enabled", true),
            ListingLimit: 3, Duration: TimeSpan.FromHours(24), Deposit: 0, FeeRateBasisPoints: 500);
        referenceCeraPerMillion = configuration.GetValue("Auction:GoldConsignment:ReferenceCeraPerMillion", 100);
    }

    private readonly int referenceCeraPerMillion;

    /// <summary>
    /// The 17 consignment tier items and the gold each stands for. The PVF tier items
    /// (<c>stackable/cash/chn_trade_gold/</c>) carry no amount, so this table is the rule (#322).
    /// </summary>
    private static readonly IReadOnlyDictionary<int, int> Tiers = new Dictionary<int, int>
    {
        [2681725] = 1_000_000, [2681726] = 2_000_000, [2681727] = 3_000_000, [2681728] = 4_000_000,
        [2681729] = 5_000_000, [2681730] = 6_000_000, [2681731] = 7_000_000, [2681732] = 8_000_000,
        [2681733] = 9_000_000, [2681734] = 10_000_000, [2681735] = 20_000_000, [2681736] = 30_000_000,
        [2683069] = 40_000_000, [2683070] = 50_000_000, [2683071] = 100_000_000, [2683072] = 150_000_000,
        [2683073] = 200_000_000,
    };

    public int? ConsignmentTierGold(int tierItemId) => Tiers.TryGetValue(tierItemId, out var amount) ? amount : null;

    /// <remarks>
    /// A server policy, not an original value: with no trade of the tier in the last seven days the
    /// price is measured against <c>Auction:GoldConsignment:ReferenceCeraPerMillion</c> Cera per
    /// million gold (default 100).
    /// </remarks>
    public long ConsignmentReferencePrice(int gold) => Math.Max(1, (long)gold / 1_000_000 * referenceCeraPerMillion);

    /// <remarks>
    /// The official tiers (#322 D3): below 40% of the recent average cannot be listed; up to 150%
    /// costs 5% (1% with a coupon), then 30%, 40%, 50% up to 160%, 170%, 180%, and 95% above.
    /// Compared in integers, so 180.1% is above 180%.
    /// </remarks>
    public int? ConsignmentFeeRate(long price, long average, bool hasCoupon)
    {
        if (price <= 0 || average <= 0) return null;
        var scaled = price * 100;
        if (scaled < average * 40) return null;
        if (scaled <= average * 150) return hasCoupon ? 100 : 500;
        if (scaled <= average * 160) return 3000;
        if (scaled <= average * 170) return 4000;
        if (scaled <= average * 180) return 5000;
        return 9500;
    }

    public AuctionMarketRule For(AuctionMarket market) => market switch
    {
        AuctionMarket.Items => items,
        AuctionMarket.GoldConsignment => gold,
        _ => throw new ArgumentOutOfRangeException(nameof(market), market, "Not an auction market."),
    };

    /// <remarks>
    /// 70 refuses locked items, unpackagable templates, attach types 1/2/5 and unsealed 3, timed
    /// items and temporary avatars, and Cera items without a coupon. The transfer checks are the mail
    /// rule's, asked as for another account. Items with avatar or creature details are not taken yet.
    /// </remarks>
    public async ValueTask<AuctionRefusal> CheckEligibilityAsync(ItemSnapshot item, bool hasCoupon, CancellationToken cancellationToken)
    {
        if (item.IsMirror || item.AvatarDetail is not null || item.CreatureDetail is not null || !item.Lifetime.IsPermanent
            || item.EquipmentLockId != 0 || item.SortLockFlag != 0)
            return AuctionRefusal.NotEligible;
        if (!(await transfers.CanSendAsync(item, sameAccount: false, cancellationToken).ConfigureAwait(false)).Allowed)
            return AuctionRefusal.NotEligible;
        var definition = await definitions.GetAsync(item.ItemId, cancellationToken).ConfigureAwait(false);
        if (definition is null || !definition.Packagable || definition.Timed || definition.Family == AuctionItemFamily.Avatar)
            return AuctionRefusal.NotEligible;
        if (definition.CashPrice != 0 && !hasCoupon) return AuctionRefusal.NeedsCoupon;
        return AuctionRefusal.None;
    }

    public AuctionRefusal CheckPricing(AuctionMarket market, AuctionPricing pricing, int quantity, bool pile)
    {
        if (quantity <= 0 || !pile && quantity != 1) return AuctionRefusal.ExceedsHolding;
        if (pricing.StartPrice is null && pricing.BuyoutPrice is null) return AuctionRefusal.InvalidPrice;
        if (!Valid(pricing.StartPrice) || !Valid(pricing.BuyoutPrice) || !Valid(pricing.UnitPrice)) return AuctionRefusal.InvalidPrice;
        if (pricing.StartPrice is { } start && pricing.BuyoutPrice is { } buyout && start >= buyout) return AuctionRefusal.InvalidPrice;
        if (market == AuctionMarket.GoldConsignment && (pricing.StartPrice is not null || pricing.BuyoutPrice is null))
            return AuctionRefusal.InvalidPrice;
        if (pricing.UnitPrice is { } unit
            && (!pile || pricing.StartPrice is not null || pricing.BuyoutPrice != unit * quantity))
            return AuctionRefusal.InvalidPrice;
        return AuctionRefusal.None;

        static bool Valid(long? price) => price is null or (> 0 and <= int.MaxValue);
    }

    /// <remarks>
    /// 70 <c>AuctionDictionary::Bidding</c>: <c>(int)(price * 1.05)</c>, plus one when that equals the
    /// price, capped at the buyout. The client checks first with a float slightly under 0.05, so the
    /// server's bound is one lower to never refuse what the client offered.
    /// </remarks>
    public long MinimumNextBid(long current, long? buyout)
    {
        var minimum = current + current * 5 / 100 - 1;
        if (minimum <= current) minimum = current + 1;
        return buyout is { } cap && minimum > cap ? cap : minimum;
    }

    public long Fee(long amount, int feeRateBasisPoints) => amount * feeRateBasisPoints / 10_000;

    public ValueTask<AuctionItemDefinition?> DescribeAsync(int itemId, CancellationToken cancellationToken) =>
        definitions.GetAsync(itemId, cancellationToken);

    /// <remarks>
    /// The client's tree (node ids from the reference server's A21 evidence): 40119 is everything,
    /// 10xxx weapons, 11xxx armor, 12001-12003 accessories, 12004 title names, 12000 both, 13xxx
    /// stackables, 32xxx special equipment. Leaves below a family are not told apart yet; a node
    /// that is none of these filters nothing.
    /// </remarks>
    public bool InCategory(ushort category, AuctionItemFamily family) => category switch
    {
        0 or 40119 => true,
        12000 => family is AuctionItemFamily.Accessory or AuctionItemFamily.TitleName,
        >= 12005 and <= 12016 or 12004 => family == AuctionItemFamily.TitleName,
        >= 10000 and <= 10999 => family == AuctionItemFamily.Weapon,
        >= 11000 and <= 11999 => family == AuctionItemFamily.Armor,
        >= 12001 and <= 12003 => family == AuctionItemFamily.Accessory,
        >= 13000 and <= 13999 => family == AuctionItemFamily.Stackable,
        >= 32000 and <= 32999 => family == AuctionItemFamily.SpecialEquipment,
        _ => true,
    };
}
