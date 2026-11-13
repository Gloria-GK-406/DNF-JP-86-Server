using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Mail;

namespace Dfo.Server.Abstractions.Auction;

/// <summary>Which book a listing is on. The numeric value is stored and never renumbered.</summary>
/// <remarks>
/// One auction house keeps both books (70: one auction program run twice, identical tables). The
/// market decides the rules, what is handed over and which currency pays for it; the listing life
/// cycle is the same for both.
/// </remarks>
public enum AuctionMarket : byte
{
    /// <summary>Items, priced in the seller's character gold.</summary>
    Items = 0,

    /// <summary>Gold consignment: a fixed amount of gold, priced in account Cera.</summary>
    GoldConsignment = 1,
}

/// <summary>Where a listing is in its life. Only <see cref="Active"/> moves; the others are final.</summary>
public enum AuctionListingState : byte
{
    Active = 0,
    Sold = 1,
    Cancelled = 2,
    Expired = 3,
}

/// <summary>Why the auction house refused. Facts only; each protocol maps them to what it shows.</summary>
public enum AuctionRefusal
{
    None,
    MarketClosed,
    ListingNotFound,
    NotSeller,
    OwnListing,
    AlreadyTopBidder,
    BidTooLow,
    InvalidPrice,
    NotEligible,
    NeedsCoupon,
    ItemMoved,
    ExceedsHolding,
    ListingLimit,
    InsufficientFunds,
    NotDivisible,
}

/// <summary>A listing's identity: the stored row key.</summary>
public readonly record struct AuctionListingId(long Value);

/// <summary>The prices a listing asks. Each is a total for the whole remaining lot unless named per unit.</summary>
/// <param name="StartPrice">The opening bid; null when only a buyout is offered.</param>
/// <param name="BuyoutPrice">The price that ends the listing at once; null when only bids are taken.</param>
/// <param name="UnitPrice">Per-unit price of a buyout-only pile, kept so it can be bought piece by piece.</param>
public sealed record AuctionPricing(long? StartPrice, long? BuyoutPrice, long? UnitPrice = null);

/// <summary>What a seller hands over.</summary>
public abstract record AuctionLotSource
{
    private AuctionLotSource() { }

    /// <summary>A quantity of the item in one of the seller's carried slots, as the request names it.</summary>
    public sealed record CarriedItem(BusinessSlot Slot, int ItemId, int Count) : AuctionLotSource;

    /// <summary>An amount of the seller's gold, sold as the consignment tier that names it.</summary>
    public sealed record Gold(int Amount, int TierItemId) : AuctionLotSource;
}

public sealed record AuctionListingRequest(AuctionMarket Market, AuctionLotSource Lot, AuctionPricing Pricing);

/// <summary>One listing as the house knows it.</summary>
/// <param name="Item">The item as it was handed over, for showing and searching it; never used to deliver it.</param>
public sealed record AuctionListing(
    AuctionListingId Id,
    AuctionMarket Market,
    AuctionListingState State,
    CharacterId Seller,
    AccountId SellerAccount,
    byte[] SellerName,
    int ItemId,
    int QuantityInitial,
    int QuantityRemaining,
    int GoldAmount,
    AuctionPricing Pricing,
    long? CurrentBid,
    CharacterId? TopBidder,
    AccountId? TopBidderAccount,
    int Deposit,
    int FeeRateBasisPoints,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    ItemSnapshot? Item)
{
    public bool HasBid => TopBidder is not null;
}

/// <summary>What an operation did to the acting character's own assets, for showing it.</summary>
public sealed record AuctionReceipt(AuctionListingId Listing, IReadOnlyList<MailAssetChange> Changes)
{
    public static AuctionReceipt Empty(AuctionListingId listing) => new(listing, []);
}

/// <summary>A refusal or what was done.</summary>
public sealed record AuctionOutcome(AuctionRefusal Refusal, AuctionReceipt? Receipt)
{
    public bool Succeeded => Refusal == AuctionRefusal.None;
    public static AuctionOutcome Done(AuctionReceipt receipt) => new(AuctionRefusal.None, receipt);
    public static AuctionOutcome Refused(AuctionRefusal refusal) => new(refusal, null);
}

/// <summary>Raised inside the house's write so the caller's unit can roll everything back.</summary>
public sealed class AuctionRefusedException(AuctionRefusal refusal)
    : InvalidOperationException($"Auction refused: {refusal}.")
{
    public AuctionRefusal Refusal { get; } = refusal;
}

/// <summary>What to look for. Ranges are inclusive; null means any.</summary>
public sealed record AuctionSearchCriteria(
    AuctionMarket Market,
    IReadOnlyList<int>? ItemIds,
    ushort? Category,
    byte? Rarity,
    byte? MinimumUpgrade,
    byte? MaximumUpgrade,
    byte? MinimumLevel,
    byte? MaximumLevel,
    byte? MinimumForging,
    byte? MaximumForging);

public sealed record AuctionPage(int Total, IReadOnlyList<AuctionListing> Listings);

/// <summary>Recent prices for one item: what sold, and what is offered now at the lowest unit prices.</summary>
public sealed record AuctionPriceReference(long? AverageUnitPrice, IReadOnlyList<(long UnitPrice, int Count)> LowestOffers);

/// <summary>The auction house: the one owner of listings and of everything they hold.</summary>
/// <remarks>
/// Protocol-free. Every write needs the caller's writable transaction; a refusal after something was
/// already taken is thrown as <see cref="AuctionRefusedException"/> so the caller rolls back, and the
/// same refusal is never committed half done. Every hand-over out of the house is a system mail
/// written in the same transaction, so a final state never leaves anything held.
/// </remarks>
public interface IAuctionHouseDomainService
{
    ValueTask<AuctionOutcome> ListAsync(CharacterId seller, AuctionListingRequest request, CancellationToken cancellationToken);

    ValueTask<AuctionOutcome> CancelAsync(CharacterId seller, AuctionMarket market, AuctionListingId listing, CancellationToken cancellationToken);

    /// <summary>A bid, or the buyout when <paramref name="amount"/> is the buyout price.</summary>
    ValueTask<AuctionOutcome> BidAsync(CharacterId bidder, AuctionMarket market, AuctionListingId listing, long amount, CancellationToken cancellationToken);

    /// <summary>Buys <paramref name="quantity"/> of a buyout-only pile, paying exactly <paramref name="amount"/>.</summary>
    ValueTask<AuctionOutcome> BuyPiecesAsync(CharacterId buyer, AuctionListingId listing, int quantity, long amount, CancellationToken cancellationToken);

    /// <summary>The active listings whose time is up, oldest first.</summary>
    ValueTask<IReadOnlyList<AuctionListingId>> DueAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken);

    /// <summary>Ends one listing whose time is up: to the top bidder if there is one, back to the seller otherwise.</summary>
    ValueTask<bool> SettleExpiredAsync(AuctionListingId listing, DateTimeOffset now, CancellationToken cancellationToken);
}

/// <summary>Reads listings. A domain service so notification handlers and other domains may ask it.</summary>
public interface IAuctionQueryDomainService
{
    ValueTask<AuctionListing?> FindAsync(AuctionListingId listing, CancellationToken cancellationToken);

    ValueTask<AuctionPage> SearchAsync(AuctionSearchCriteria criteria, int offset, int limit, DateTimeOffset now, CancellationToken cancellationToken);

    ValueTask<IReadOnlyList<AuctionListing>> BySellerAsync(CharacterId seller, AuctionMarket market, int limit, CancellationToken cancellationToken);

    ValueTask<IReadOnlyList<AuctionListing>> ByTopBidderAsync(CharacterId bidder, AuctionMarket market, int limit, DateTimeOffset now, CancellationToken cancellationToken);

    ValueTask<AuctionPriceReference> PriceReferenceAsync(AuctionMarket market, int itemId, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Whether the character sells, or is the top bidder on, anything still active.</summary>
    ValueTask<bool> HasOpenObligationsAsync(CharacterId character, CancellationToken cancellationToken);

    /// <summary>Whether the market takes new listings and bids now.</summary>
    bool IsOpen(AuctionMarket market);
}

/// <summary>What the content says about an item, as far as the auction house cares.</summary>
/// <param name="Packagable">PVF <c>[packagable]</c>: the 70 server's main switch for listing.</param>
/// <param name="CashPrice">PVF <c>[cash]</c>; non-zero marks a Cera-shop item.</param>
/// <param name="Timed">The template declares a usable period or an expiration date.</param>
public sealed record AuctionItemDefinition(
    bool Packagable,
    int CashPrice,
    bool Timed,
    byte Rarity,
    byte MinimumLevel,
    AuctionItemFamily Family);

/// <summary>Coarse grouping that the client's category tree is matched against.</summary>
public enum AuctionItemFamily : byte
{
    Other,
    Weapon,
    Armor,
    Accessory,
    TitleName,
    SpecialEquipment,
    Avatar,
    Stackable,
}

/// <summary>PVF facts for listing and searching. Read only by <see cref="IAuctionRuleService"/>.</summary>
public interface IAuctionItemDefinitionSource
{
    ValueTask<AuctionItemDefinition?> GetAsync(int itemId, CancellationToken cancellationToken);
}

/// <summary>The rules of one market, fixed for as long as the rule service gives it out.</summary>
public sealed record AuctionMarketRule(
    AuctionMarket Market,
    bool Open,
    int ListingLimit,
    TimeSpan Duration,
    int Deposit,
    int FeeRateBasisPoints);

/// <summary>Auction rules: market terms, eligibility, prices and the client's category tree.</summary>
public interface IAuctionRuleService
{
    AuctionMarketRule For(AuctionMarket market);

    /// <summary>Whether this held item may be listed. The snapshot is the caller's fact.</summary>
    ValueTask<AuctionRefusal> CheckEligibilityAsync(ItemSnapshot item, bool hasCoupon, CancellationToken cancellationToken);

    /// <summary>Normalizes and checks the asked prices for a lot of <paramref name="quantity"/>.</summary>
    AuctionRefusal CheckPricing(AuctionMarket market, AuctionPricing pricing, int quantity, bool pile);

    /// <summary>The lowest acceptable next bid over <paramref name="current"/>, capped by the buyout.</summary>
    long MinimumNextBid(long current, long? buyout);

    /// <summary>The fee taken from <paramref name="amount"/> at the locked rate.</summary>
    long Fee(long amount, int feeRateBasisPoints);

    /// <summary>Search facts for an item, or null when the content does not know it.</summary>
    ValueTask<AuctionItemDefinition?> DescribeAsync(int itemId, CancellationToken cancellationToken);

    /// <summary>Whether an item of <paramref name="family"/> belongs under the client's category node.</summary>
    bool InCategory(ushort category, AuctionItemFamily family);
}

/// <summary>Where listings and trades are kept. Data operations only; every rule is the house's.</summary>
public interface IAuctionDomainStore
{
    ValueTask<AuctionListingId> InsertAsync(AuctionListing listing, CancellationToken cancellationToken);

    ValueTask<AuctionListing?> FindAsync(AuctionListingId listing, CancellationToken cancellationToken);

    ValueTask<int> CountActiveAsync(CharacterId seller, AuctionMarket market, CancellationToken cancellationToken);

    /// <summary>Records a new top bid on an active listing; false when it is no longer active.</summary>
    ValueTask<bool> SetBidAsync(AuctionListingId listing, long amount, CharacterId bidder, AccountId bidderAccount, CancellationToken cancellationToken);

    /// <summary>Lowers what an active listing still offers after a part of it was bought.</summary>
    ValueTask<bool> ReduceAsync(AuctionListingId listing, int quantityRemaining, long buyoutPrice, CancellationToken cancellationToken);

    /// <summary>Moves an active listing to a final state; false when it was not active.</summary>
    ValueTask<bool> CloseAsync(AuctionListingId listing, AuctionListingState state, DateTimeOffset at, CancellationToken cancellationToken);

    ValueTask RecordTradeAsync(AuctionListing listing, int quantity, long price, long fee, CharacterId buyer, DateTimeOffset at, CancellationToken cancellationToken);

    ValueTask<IReadOnlyList<AuctionListingId>> DueAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken);

    /// <summary>Active listings of the market not yet expired, by id.</summary>
    ValueTask<IReadOnlyList<AuctionListing>> ActiveAsync(AuctionMarket market, IReadOnlyList<int>? itemIds, DateTimeOffset now, CancellationToken cancellationToken);

    ValueTask<IReadOnlyList<AuctionListing>> ActiveBySellerAsync(CharacterId seller, AuctionMarket market, int limit, CancellationToken cancellationToken);

    ValueTask<IReadOnlyList<AuctionListing>> ActiveByTopBidderAsync(CharacterId bidder, AuctionMarket market, int limit, CancellationToken cancellationToken);

    /// <summary>Total price and quantity of trades of the item since <paramref name="since"/>.</summary>
    ValueTask<(long Price, long Quantity)> TradedSinceAsync(AuctionMarket market, int itemId, DateTimeOffset since, CancellationToken cancellationToken);

    ValueTask<bool> HasActiveObligationsAsync(CharacterId character, CancellationToken cancellationToken);
}

/// <summary>Item custody inside the item transaction, beside mail custody.</summary>
/// <remarks>
/// A listing holds the one record taken from the seller. Equipment keeps its item row and identity;
/// a pile keeps its exact core. Taking hands back a pending record that must be received in the same
/// transaction (by a mail attachment), so custody never leaves an item that nobody holds.
/// </remarks>
public interface IAuctionItemCustodySession
{
    ValueTask AcceptAuctionLotAsync(AuctionListingId listing, ItemRecord record, CancellationToken cancellationToken);

    /// <summary>Takes <paramref name="count"/> units (all when null) out of the listing's custody.</summary>
    ValueTask<ItemRecord> TakeAuctionLotAsync(AuctionListingId listing, int? count, CancellationToken cancellationToken);
}
