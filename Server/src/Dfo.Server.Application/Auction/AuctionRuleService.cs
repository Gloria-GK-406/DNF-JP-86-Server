using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Auction;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;
using Microsoft.Extensions.Configuration;

namespace Dfo.Server.Application.Auction;

/// <summary>The auction house's terms. The one place PVF and auction configuration are read.</summary>
/// <remarks>
/// Market terms are the 70 server's (70Reference, df_auction_r / df_game_r, 2026-11-13): 24 hours
/// and a 10000 gold deposit for items. What an account may do there -- how many listings, the fee,
/// the durations it may choose, whether Cera-shop items may be listed -- is an entitlement, kept in
/// one table keyed by the account identity (premium service) that grants it.
/// </remarks>
internal sealed class AuctionRuleService : IAuctionRuleService
{
    /// <summary>
    /// Every identity's entitlement. A new identity is one more row; nothing else changes.
    /// </summary>
    /// <remarks>
    /// Ordinary: 70's 10 listings and 5% (consignment: 3 listings, 5% in the normal band).
    /// Auction coupon (premium 29, item 193 "拍卖行优惠券"), as its PVF explanation lists: 30 listings
    /// (70 premium 0x1d also 30), a 48-hour choice, 3% (70 VIP 3%), Cera-shop items listable; for
    /// consignment the official 1% normal band and, as 70's point-auction VIP, 10 listings.
    /// </remarks>
    private static readonly IReadOnlyList<AuctionEntitlement> Entitlements =
    [
        new(Identity: null, Priority: 0, ItemListingLimit: 10, ItemFeeRateBasisPoints: 500, ListingHours: [24],
            CashItemsListable: false, ConsignmentListingLimit: 3, ConsignmentFeeRateBasisPoints: 500),
        new(Identity: PremiumServiceType.AuctionCoupon, Priority: 10, ItemListingLimit: 30, ItemFeeRateBasisPoints: 300,
            ListingHours: [24, 48], CashItemsListable: true, ConsignmentListingLimit: 10, ConsignmentFeeRateBasisPoints: 100),
    ];

    private readonly IAuctionItemDefinitionSource definitions;
    private readonly IMailItemRuleService transfers;
    private readonly AuctionMarketRule items;
    private readonly AuctionMarketRule gold;
    private readonly TimeSpan day;

    public AuctionRuleService(IAuctionItemDefinitionSource definitions, IMailItemRuleService transfers, IConfiguration configuration)
    {
        this.definitions = definitions;
        this.transfers = transfers;
        // A listing day is 24 hours; the minutes setting exists to test expiry and scales every duration.
        day = TimeSpan.FromMinutes(Math.Max(1, configuration.GetValue("Auction:ListingMinutes", 24 * 60)));
        items = new(AuctionMarket.Items, configuration.GetValue("Auction:Items:Enabled", true), day, Deposit: 10_000);
        gold = new(AuctionMarket.GoldConsignment, configuration.GetValue("Auction:GoldConsignment:Enabled", true), day, Deposit: 0);
        referenceCeraPerMillion = configuration.GetValue("Auction:GoldConsignment:ReferenceCeraPerMillion", 100);
    }

    public AuctionEntitlement EntitlementFor(IReadOnlyCollection<PremiumServiceType> activeServices) =>
        Entitlements.Where(entitlement => entitlement.Identity is not { } identity || activeServices.Contains(identity))
            .MaxBy(entitlement => entitlement.Priority)!;

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
    /// costs the entitlement's rate (5%, 1% with the coupon), then 30%, 40%, 50% up to 160%, 170%, 180%, and 95% above.
    /// Compared in integers, so 180.1% is above 180%.
    /// </remarks>
    public int? ConsignmentFeeRate(long price, long average, AuctionEntitlement entitlement)
    {
        if (price <= 0 || average <= 0) return null;
        var scaled = price * 100;
        if (scaled < average * 40) return null;
        if (scaled <= average * 150) return entitlement.ConsignmentFeeRateBasisPoints;
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
    /// The client sends 24 or 48. Hours the entitlement does not list fall back to its first. The test
    /// setting <c>Auction:ListingMinutes</c> scales the day, so "48" is two of those.
    /// </remarks>
    public TimeSpan ListingDuration(uint requestedHours, AuctionEntitlement entitlement)
    {
        var hours = entitlement.ListingHours.Contains(requestedHours) ? requestedHours : entitlement.ListingHours[0];
        return day * (hours / 24.0);
    }

    /// <remarks>
    /// 70 refuses locked items, unpackagable templates, attach types 1/2/5 and unsealed 3, timed
    /// items and temporary avatars, and Cera items without a coupon. The transfer checks are the mail
    /// rule's, asked as for another account. Items with avatar or creature details are not taken yet.
    /// </remarks>
    public async ValueTask<AuctionRefusal> CheckEligibilityAsync(ItemSnapshot item, AuctionEntitlement entitlement, CancellationToken cancellationToken)
    {
        if (item.IsMirror || item.AvatarDetail is not null || item.CreatureDetail is not null || !item.Lifetime.IsPermanent
            || item.EquipmentLockId != 0 || item.SortLockFlag != 0)
            return AuctionRefusal.NotEligible;
        if (!(await transfers.CanSendAsync(item, sameAccount: false, cancellationToken).ConfigureAwait(false)).Allowed)
            return AuctionRefusal.NotEligible;
        var definition = await definitions.GetAsync(item.ItemId, cancellationToken).ConfigureAwait(false);
        if (definition is null || !definition.Packagable || definition.Timed || definition.Family == AuctionItemFamily.Avatar)
            return AuctionRefusal.NotEligible;
        if (definition.CashPrice != 0 && !entitlement.CashItemsListable) return AuctionRefusal.NeedsCoupon;
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
