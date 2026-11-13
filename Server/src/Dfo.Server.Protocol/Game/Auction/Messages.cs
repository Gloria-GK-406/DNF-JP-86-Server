using Dfo.Server.Abstractions.Auction;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.Auction;

// Requests. Every auction CMD but 0x014E starts with the market byte, the 70 server's PAY_TYPE
// (df_game_r IsAuctionServerDisconnected: 0 CAuctionServerProxy, 1 CCeraAuctionServerProxy; 70Reference).
// Layouts are the reference server's A21 reading of the same executable (ClientStatic as relayed,
// not re-proved here; Document 拍卖行实现方案 §2.2). A body whose length does not match its market's
// layout decodes with Malformed set and is still answered.

/// <summary>The fields every auction request carries.</summary>
public interface IAuctionRequest : IClientNetworkMessage
{
    GameMessageMetadata Metadata { get; }

    /// <summary>The market byte as sent; 0x014E has none and says <see cref="AuctionMarket.Items"/>.</summary>
    byte PayType { get; }

    bool Malformed { get; }
}

/// <summary>CMD 0x00B6: what an item sells for. Items: <c>pt, u32 item, u8 ×3</c>; gold: <c>pt, u32 tier, u8</c>.</summary>
public sealed record AuctionAveragePriceRequest(GameMessageMetadata Metadata, byte PayType, bool Malformed, int ItemId)
    : IAuctionRequest
{
    public ClientMessageType MessageType => ClientMessageType.GameAuctionAveragePrice;
}

/// <summary>CMD 0x00B7: list. Items, 37 bytes; gold consignment, 28 bytes (decoded by its own branch).</summary>
/// <param name="Slot">The carried slot the item is taken from.</param>
/// <param name="Pricing">Totals for the whole lot, normalized from the client's unit or total entry.</param>
/// <param name="Hours">The client's duration choice; the market's rule decides the actual one.</param>
public sealed record AuctionRegisterRequest(GameMessageMetadata Metadata, byte PayType, bool Malformed,
    BusinessSlot Slot, int ItemId, int Count, AuctionPricing Pricing, uint Hours, int GoldAmount = 0) : IAuctionRequest
{
    public ClientMessageType MessageType => ClientMessageType.GameAuctionRegister;
}

/// <summary>CMD 0x00B8: <c>pt, u64 listing</c>, both markets 9 bytes.</summary>
public sealed record AuctionCancelRequest(GameMessageMetadata Metadata, byte PayType, bool Malformed, AuctionListingId Listing)
    : IAuctionRequest
{
    public ClientMessageType MessageType => ClientMessageType.GameAuctionCancel;
}

/// <summary>CMD 0x00B9: bid, or buy out when the amount is the buyout price.</summary>
/// <remarks>Items: <c>pt, u32 amount, u64 listing, 13-byte seller name</c>; gold: <c>pt, u32 price, u32 listing, u32 0, u32 tier</c>.</remarks>
public sealed record AuctionBidRequest(GameMessageMetadata Metadata, byte PayType, bool Malformed, long Amount,
    AuctionListingId Listing, byte[] SellerName) : IAuctionRequest
{
    public ClientMessageType MessageType => ClientMessageType.GameAuctionBid;
}

/// <summary>CMD 0x00BA or 0x00BB: a search, answered under 0x00BA.</summary>
public sealed record AuctionSearchRequest(GameMessageMetadata Metadata, byte PayType, bool Malformed, int Position,
    AuctionSearchCriteria? Criteria, bool ByCriteria) : IAuctionRequest
{
    public ClientMessageType MessageType => ByCriteria ? ClientMessageType.GameAuctionSearchByCriteria : ClientMessageType.GameAuctionSearchByItem;
}

/// <summary>CMD 0x00BC (own listings) or 0x00BD (own top bids): <c>pt</c>.</summary>
public sealed record AuctionMineRequest(GameMessageMetadata Metadata, byte PayType, bool Malformed, bool Bids) : IAuctionRequest
{
    public ClientMessageType MessageType => Bids ? ClientMessageType.GameAuctionMyBids : ClientMessageType.GameAuctionMyListings;
}

/// <summary>CMD 0x014E: buy part of a pile, <c>u32 amount, u32 count, u64 listing, 13-byte name, u32 item</c>; no market byte.</summary>
public sealed record AuctionBuyPiecesRequest(GameMessageMetadata Metadata, bool Malformed, long Amount, int Count,
    AuctionListingId Listing, byte[] SellerName, int ItemId) : IAuctionRequest
{
    public byte PayType => (byte)AuctionMarket.Items;
    public ClientMessageType MessageType => ClientMessageType.GameAuctionBuyPieces;
}

// Replies. Success is [1][pt]; a refusal [0][code][pt]; 0x014E has no market byte. The client's
// refusal controller (A21 VA 01055D60) shows text only for codes 0x8C..0x9E.

/// <summary>The reason codes the auction client shows, and one it shows nothing for.</summary>
public enum AuctionReplyCode : byte
{
    Silent = 0x7B,
    Closed = 0x8C,
    InsufficientGold = 0x90,
    NotListable = 0x91,
    ExceedsHolding = 0x92,
    MissingItem = 0x94,
    NeedsCoupon = 0x95,
    NoSuchListing = 0x96,
    ListingLimit = 0x97,
    InvalidPrice = 0x98,
    NoPermission = 0x9A,
    OwnListing = 0x9B,
    AlreadyTopBidder = 0x9C,
    BidTooLow = 0x9D,
}

public abstract record AuctionReply(byte PayType, AuctionReplyCode? Refusal) : IGameCmdMessage
{
    public abstract ServerMessageType MessageType { get; }
}

public sealed record AuctionRegisterResponse(byte PayType, AuctionReplyCode? Refusal) : AuctionReply(PayType, Refusal)
{
    public override ServerMessageType MessageType => ServerMessageType.GameAuctionRegister;
}

public sealed record AuctionCancelResponse(byte PayType, AuctionReplyCode? Refusal) : AuctionReply(PayType, Refusal)
{
    public override ServerMessageType MessageType => ServerMessageType.GameAuctionCancel;
}

public sealed record AuctionBidResponse(byte PayType, AuctionReplyCode? Refusal) : AuctionReply(PayType, Refusal)
{
    public override ServerMessageType MessageType => ServerMessageType.GameAuctionBid;
}

public sealed record AuctionBuyPiecesResponse(AuctionReplyCode? Refusal) : AuctionReply(0, Refusal)
{
    public override ServerMessageType MessageType => ServerMessageType.GameAuctionBuyPieces;
}

/// <summary>0x00B6 reply: <c>1, pt, u32 suggested, u32 0, u32 ×6 unit prices, u32 ×6 counts</c>.</summary>
public sealed record AuctionAveragePriceResponse(byte PayType, AuctionReplyCode? Refusal, AuctionPriceReference? Prices)
    : AuctionReply(PayType, Refusal)
{
    public override ServerMessageType MessageType => ServerMessageType.GameAuctionAveragePrice;
}

/// <summary>One listing as a list shows it.</summary>
public sealed record AuctionListingView(AuctionListing Listing, byte HoursLeft);

/// <summary>0x00BA reply to either search: <c>1, pt, u32 total, u16 n, n × 168</c>.</summary>
public sealed record AuctionSearchResponse(byte PayType, AuctionReplyCode? Refusal, int Total, IReadOnlyList<AuctionListingView> Listings)
    : AuctionReply(PayType, Refusal)
{
    public override ServerMessageType MessageType => ServerMessageType.GameAuctionSearch;
}

/// <summary>0x00BC reply: <c>1, pt, u8 n, n × 151</c>.</summary>
public sealed record AuctionMyListingsResponse(byte PayType, AuctionReplyCode? Refusal, IReadOnlyList<AuctionListingView> Listings)
    : AuctionReply(PayType, Refusal)
{
    public override ServerMessageType MessageType => ServerMessageType.GameAuctionMyListings;
}

/// <summary>0x00BD reply: <c>1, 0, u16 n, n × 159</c>.</summary>
public sealed record AuctionMyBidsResponse(byte PayType, AuctionReplyCode? Refusal, IReadOnlyList<AuctionListingView> Listings)
    : AuctionReply(PayType, Refusal)
{
    public override ServerMessageType MessageType => ServerMessageType.GameAuctionMyBids;
}
