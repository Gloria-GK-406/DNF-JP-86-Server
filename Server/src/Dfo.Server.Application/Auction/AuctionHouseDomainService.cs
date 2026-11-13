using Dfo.Server.Abstractions.Auction;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Mail;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Mail;

namespace Dfo.Server.Application.Auction;

/// <summary>The one owner of auction listings and everything they hold, behind the write and the read face.</summary>
/// <remarks>
/// A listing's lot, the seller's deposit and the top bid are held here until the listing ends, and
/// every one of them leaves by a system mail written in the same transaction as the state change.
/// Every refusal is thrown as <see cref="AuctionRefusedException"/>: some come after gold or an item
/// was already taken, and the caller's unit must roll all of it back.
/// </remarks>
internal sealed class AuctionHouseDomainService(
    IAuctionDomainStore store,
    IAuctionRuleService rules,
    IAuctionLotHandling lots,
    IAuctionPurse purse,
    ICharacterDomainService characters,
    ICharacterTable owners,
    Dfo.Server.Abstractions.Accounts.IPremiumServiceDomainService premiums,
    IDomainTransactionAccessor transactions,
    TimeProvider clock) : IAuctionHouseDomainService, IAuctionQueryDomainService
{

    public async ValueTask<AuctionOutcome> ListAsync(CharacterId seller, AuctionListingRequest request, CancellationToken ct)
    {
        RequireWritable();
        var account = await AccountOf(seller, ct).ConfigureAwait(false);
        var now = clock.GetUtcNow();
        // The account's identities are read at listing; the fee and end time they give are kept on
        // the listing, so a later change of identity does not touch it.
        var identities = (await premiums.For(account).ReadAtAsync(now, ct).ConfigureAwait(false))
            .Where(entry => entry.ExpiresAtUnixSeconds > now.ToUnixTimeSeconds())
            .Select(entry => (Dfo.Server.Abstractions.Accounts.PremiumServiceType)entry.PremiumType).ToArray();
        var entitlement = rules.EntitlementFor(identities);
        var rule = rules.For(request.Market);
        if (!rule.Open) throw Refuse(AuctionRefusal.MarketClosed);
        var name = (await characters.Get(seller).IdentityAsync(ct).ConfigureAwait(false))?.Name
            ?? throw new InvalidOperationException($"Character {seller.Value} cannot list without an identity.");
        var limit = request.Market == AuctionMarket.Items ? entitlement.ItemListingLimit : entitlement.ConsignmentListingLimit;
        if (await store.CountActiveAsync(seller, request.Market, ct).ConfigureAwait(false) >= limit)
            throw Refuse(AuctionRefusal.ListingLimit);

        var lot = await lots.InspectAsync(seller, request.Market, request.Lot, entitlement, ct).ConfigureAwait(false);
        Check(rules.CheckPricing(request.Market, request.Pricing, lot.Quantity, lot.Pile));
        var feeRate = entitlement.ItemFeeRateBasisPoints;
        if (request.Market == AuctionMarket.GoldConsignment)
        {
            // The asked price against the tier's recent average sets the rate, and it is kept.
            var average = await ConsignmentAverageAsync(lot.ItemId, lot.GoldAmount, now, ct).ConfigureAwait(false);
            feeRate = rules.ConsignmentFeeRate(request.Pricing.BuyoutPrice!.Value, average, entitlement)
                ?? throw Refuse(AuctionRefusal.InvalidPrice);
        }
        var changes = new List<MailAssetChange>();
        if (rule.Deposit > 0) changes.Add(await purse.CollectGoldAsync(seller, rule.Deposit, ct).ConfigureAwait(false));
        var listing = new AuctionListing(default, request.Market, AuctionListingState.Active, seller, account,
            name, lot.ItemId, lot.Quantity, lot.Quantity,
            lot.GoldAmount, request.Pricing, null, null, null, rule.Deposit, feeRate, now,
            now + rules.ListingDuration(request.Hours, entitlement), lot.Shown);
        var id = await store.InsertAsync(listing, ct).ConfigureAwait(false);
        changes.AddRange(await lots.TakeIntoCustodyAsync(seller, id, lot, ct).ConfigureAwait(false));
        return AuctionOutcome.Done(new(id, changes));
    }

    public async ValueTask<AuctionOutcome> CancelAsync(CharacterId seller, AuctionMarket market, AuctionListingId id, CancellationToken ct)
    {
        RequireWritable();
        var listing = await ActiveAsync(id, market, ct).ConfigureAwait(false);
        if (listing.Seller != seller) throw Refuse(AuctionRefusal.NotSeller);
        await CloseAsync(listing, AuctionListingState.Cancelled, ct).ConfigureAwait(false);
        // 70 RegistCancel: the bidder gets the bid back in full, the seller the lot, the deposit is kept.
        if (listing.TopBidder is { } bidder)
            await purse.RefundAsync(listing.Market, bidder, listing.TopBidderAccount!.Value, listing.CurrentBid!.Value,
                AuctionLetters.CancelledToBidder, ct).ConfigureAwait(false);
        await lots.DeliverAsync(listing, listing.Seller, null, 0, AuctionLetters.CancelledToSeller, ct).ConfigureAwait(false);
        return AuctionOutcome.Done(AuctionReceipt.Empty(id));
    }

    public async ValueTask<AuctionOutcome> BidAsync(CharacterId bidder, AuctionMarket market, AuctionListingId id, long amount, CancellationToken ct)
    {
        RequireWritable();
        if (!rules.For(market).Open) throw Refuse(AuctionRefusal.MarketClosed);
        var listing = await ActiveAsync(id, market, ct).ConfigureAwait(false);
        var account = await AccountOf(bidder, ct).ConfigureAwait(false);
        if (account == listing.SellerAccount) throw Refuse(AuctionRefusal.OwnListing);
        var buyout = listing.Pricing.BuyoutPrice;
        if (buyout is { } price && amount == price)
            return await BuyOutAsync(listing, bidder, account, price, ct).ConfigureAwait(false);
        if (listing.TopBidder == bidder) throw Refuse(AuctionRefusal.AlreadyTopBidder);
        if ((listing.CurrentBid ?? listing.Pricing.StartPrice) is not { } current) throw Refuse(AuctionRefusal.BidTooLow);
        if (buyout is { } cap && amount > cap) throw Refuse(AuctionRefusal.InvalidPrice);
        if (amount < rules.MinimumNextBid(current, buyout)) throw Refuse(AuctionRefusal.BidTooLow);

        var change = await purse.CollectAsync(market, bidder, account, amount, ct).ConfigureAwait(false);
        if (listing.TopBidder is { } previous)
            await purse.RefundAsync(market, previous, listing.TopBidderAccount!.Value, listing.CurrentBid!.Value,
                AuctionLetters.Outbid, ct).ConfigureAwait(false);
        if (!await store.SetBidAsync(id, amount, bidder, account, ct).ConfigureAwait(false))
            throw Refuse(AuctionRefusal.ListingNotFound);
        return AuctionOutcome.Done(new(id, change is null ? [] : [change]));
    }

    public async ValueTask<AuctionOutcome> BuyPiecesAsync(CharacterId buyer, AuctionListingId id, int quantity, long amount, CancellationToken ct)
    {
        RequireWritable();
        if (!rules.For(AuctionMarket.Items).Open) throw Refuse(AuctionRefusal.MarketClosed);
        var listing = await ActiveAsync(id, AuctionMarket.Items, ct).ConfigureAwait(false);
        var account = await AccountOf(buyer, ct).ConfigureAwait(false);
        if (account == listing.SellerAccount) throw Refuse(AuctionRefusal.OwnListing);
        if (listing.Pricing is not { StartPrice: null, UnitPrice: { } unit, BuyoutPrice: { } buyout } || listing.HasBid)
            throw Refuse(AuctionRefusal.NotDivisible);
        if (quantity <= 0 || quantity > listing.QuantityRemaining) throw Refuse(AuctionRefusal.ExceedsHolding);
        if (unit * quantity != amount) throw Refuse(AuctionRefusal.InvalidPrice);
        if (quantity == listing.QuantityRemaining)
            return await BuyOutAsync(listing, buyer, account, buyout, ct).ConfigureAwait(false);

        var change = await purse.CollectAsync(AuctionMarket.Items, buyer, account, amount, ct).ConfigureAwait(false);
        if (!await store.ReduceAsync(id, listing.QuantityRemaining - quantity, buyout - amount, ct).ConfigureAwait(false))
            throw Refuse(AuctionRefusal.ListingNotFound);
        var now = clock.GetUtcNow();
        var fee = rules.Fee(amount, listing.FeeRateBasisPoints);
        await lots.DeliverAsync(listing, buyer, quantity, 0, AuctionLetters.Bought, ct).ConfigureAwait(false);
        await purse.PaySellerAsync(listing, amount, fee, 0, quantity, ct).ConfigureAwait(false);
        await store.RecordTradeAsync(listing, quantity, amount, fee, buyer, now, ct).ConfigureAwait(false);
        return AuctionOutcome.Done(new(id, change is null ? [] : [change]));
    }

    public ValueTask<IReadOnlyList<AuctionListingId>> DueAsync(DateTimeOffset now, int limit, CancellationToken ct) =>
        store.DueAsync(now, limit, ct);

    public async ValueTask<bool> SettleExpiredAsync(AuctionListingId id, DateTimeOffset now, CancellationToken ct)
    {
        RequireWritable();
        if (await store.FindAsync(id, ct).ConfigureAwait(false) is not { State: AuctionListingState.Active } listing
            || listing.ExpiresAt > now)
            return false;
        if (listing.TopBidder is { } bidder)
        {
            // 70 makeSuccessfulBid at expiry: the top bid wins; its money is already held.
            await SellAsync(listing, bidder, listing.CurrentBid!.Value, ct).ConfigureAwait(false);
            return true;
        }
        await CloseAsync(listing, AuctionListingState.Expired, ct).ConfigureAwait(false);
        await lots.DeliverAsync(listing, listing.Seller, null, listing.Deposit, AuctionLetters.ExpiredToSeller, ct).ConfigureAwait(false);
        return true;
    }

    private async ValueTask<AuctionOutcome> BuyOutAsync(AuctionListing listing, CharacterId buyer, AccountId account,
        long price, CancellationToken ct)
    {
        var change = await purse.CollectAsync(listing.Market, buyer, account, price, ct).ConfigureAwait(false);
        if (listing.TopBidder is { } previous)
            await purse.RefundAsync(listing.Market, previous, listing.TopBidderAccount!.Value, listing.CurrentBid!.Value,
                AuctionLetters.Outbid, ct).ConfigureAwait(false);
        await SellAsync(listing, buyer, price, ct).ConfigureAwait(false);
        return AuctionOutcome.Done(new(listing.Id, change is null ? [] : [change]));
    }

    /// <summary>Ends the listing as sold: the lot to the buyer, the price less the fee and with the deposit to the seller.</summary>
    private async ValueTask SellAsync(AuctionListing listing, CharacterId buyer, long price, CancellationToken ct)
    {
        await CloseAsync(listing, AuctionListingState.Sold, ct).ConfigureAwait(false);
        var fee = rules.Fee(price, listing.FeeRateBasisPoints);
        await lots.DeliverAsync(listing, buyer, null, 0, AuctionLetters.Bought, ct).ConfigureAwait(false);
        await purse.PaySellerAsync(listing, price, fee, listing.Deposit, listing.QuantityRemaining, ct).ConfigureAwait(false);
        await store.RecordTradeAsync(listing, listing.QuantityRemaining, price, fee, buyer, clock.GetUtcNow(), ct).ConfigureAwait(false);
    }

    private async ValueTask CloseAsync(AuctionListing listing, AuctionListingState state, CancellationToken ct)
    {
        if (!await store.CloseAsync(listing.Id, state, clock.GetUtcNow(), ct).ConfigureAwait(false))
            throw Refuse(AuctionRefusal.ListingNotFound);
    }

    private async ValueTask<AuctionListing> ActiveAsync(AuctionListingId id, AuctionMarket market, CancellationToken ct) =>
        await store.FindAsync(id, ct).ConfigureAwait(false) is { State: AuctionListingState.Active } listing
            && listing.Market == market && listing.ExpiresAt > clock.GetUtcNow()
            ? listing
            : throw Refuse(AuctionRefusal.ListingNotFound);

    private async ValueTask<AccountId> AccountOf(CharacterId character, CancellationToken ct) =>
        await owners.FindOwnerAsync(character, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Character {character.Value} has no live owner.");

    private void RequireWritable()
    {
        if (transactions.Current is not { IsReadOnly: false })
            throw new InvalidOperationException("Auction changes require a writable transaction.");
    }

    private static void Check(AuctionRefusal refusal)
    {
        if (refusal != AuctionRefusal.None) throw Refuse(refusal);
    }

    private static AuctionRefusedException Refuse(AuctionRefusal refusal) => new(refusal);

    // ---- Read face -----------------------------------------------------------------------------

    public ValueTask<AuctionListing?> FindAsync(AuctionListingId listing, CancellationToken ct) => store.FindAsync(listing, ct);

    public async ValueTask<AuctionPage> SearchAsync(AuctionSearchCriteria criteria, int offset, int limit, DateTimeOffset now,
        CancellationToken ct)
    {
        var candidates = await store.ActiveAsync(criteria.Market, criteria.ItemIds, now, ct).ConfigureAwait(false);
        var matches = new List<AuctionListing>();
        foreach (var listing in candidates)
            if (await MatchesAsync(listing, criteria, ct).ConfigureAwait(false)) matches.Add(listing);
        return new(matches.Count, matches.Skip(Math.Max(offset, 0)).Take(limit).ToArray());
    }

    private async ValueTask<bool> MatchesAsync(AuctionListing listing, AuctionSearchCriteria criteria, CancellationToken ct)
    {
        var upgrade = listing.Item?.ReinforcementLevel ?? 0;
        var forging = listing.Item?.GenuineUpgrade ?? 0;
        if (upgrade < (criteria.MinimumUpgrade ?? 0) || upgrade > (criteria.MaximumUpgrade ?? byte.MaxValue)
            || forging < (criteria.MinimumForging ?? 0) || forging > (criteria.MaximumForging ?? byte.MaxValue))
            return false;
        if (criteria.Category is null && criteria.Rarity is null && criteria.MinimumLevel is null && criteria.MaximumLevel is null)
            return true;
        var definition = await rules.DescribeAsync(listing.ItemId, ct).ConfigureAwait(false);
        if (definition is null) return false;
        return (criteria.Category is not { } category || rules.InCategory(category, definition.Family))
            && (criteria.Rarity is not { } rarity || definition.Rarity == rarity)
            && definition.MinimumLevel >= (criteria.MinimumLevel ?? 0)
            && definition.MinimumLevel <= (criteria.MaximumLevel ?? byte.MaxValue);
    }

    public ValueTask<IReadOnlyList<AuctionListing>> BySellerAsync(CharacterId seller, AuctionMarket market, int limit, CancellationToken ct) =>
        store.ActiveBySellerAsync(seller, market, limit, ct);

    public async ValueTask<IReadOnlyList<AuctionListing>> ByTopBidderAsync(CharacterId bidder, AuctionMarket market, int limit,
        DateTimeOffset now, CancellationToken ct) =>
        (await store.ActiveByTopBidderAsync(bidder, market, limit, ct).ConfigureAwait(false)).Where(x => x.ExpiresAt > now).ToArray();

    /// <remarks>The average is over trades of the last seven days; the offers are the six lowest unit prices on sale.</remarks>
    public async ValueTask<AuctionPriceReference> PriceReferenceAsync(AuctionMarket market, int itemId, DateTimeOffset now, CancellationToken ct)
    {
        if (market == AuctionMarket.GoldConsignment)
            return rules.ConsignmentTierGold(itemId) is { } gold
                ? new(await ConsignmentAverageAsync(itemId, gold, now, ct).ConfigureAwait(false), [])
                : new(null, []);
        var (price, quantity) = await store.TradedSinceAsync(market, itemId, now.AddDays(-7), ct).ConfigureAwait(false);
        var offers = (await store.ActiveAsync(market, [itemId], now, ct).ConfigureAwait(false))
            .Where(x => x.Pricing.BuyoutPrice is not null && x.QuantityRemaining > 0)
            .Select(x => (UnitPrice: x.Pricing.UnitPrice ?? x.Pricing.BuyoutPrice!.Value / x.QuantityRemaining, Count: x.QuantityRemaining))
            .GroupBy(x => x.UnitPrice).Select(g => (UnitPrice: g.Key, Count: g.Sum(x => x.Count)))
            .OrderBy(x => x.UnitPrice).Take(6).ToArray();
        return new(quantity > 0 ? price / quantity : null, offers);
    }

    /// <summary>A tier's average traded price over seven days, or the rule's reference when it has none.</summary>
    private async ValueTask<long> ConsignmentAverageAsync(int tierItemId, int gold, DateTimeOffset now, CancellationToken ct)
    {
        var (price, quantity) = await store.TradedSinceAsync(AuctionMarket.GoldConsignment, tierItemId, now.AddDays(-7), ct)
            .ConfigureAwait(false);
        return quantity > 0 ? price / quantity : rules.ConsignmentReferencePrice(gold);
    }

    public ValueTask<bool> HasOpenObligationsAsync(CharacterId character, CancellationToken ct) =>
        store.HasActiveObligationsAsync(character, ct);

    public bool IsOpen(AuctionMarket market) => rules.For(market).Open;
}
