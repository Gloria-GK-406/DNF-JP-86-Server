using System.Buffers.Binary;
using Dfo.Server.Abstractions.Auction;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.Auction;
using Xunit;

namespace Dfo.Server.Protocol.Tests.Game.Auction;

public sealed class AuctionCodecTests
{
    private static readonly GameMessageMetadata Metadata = GameMessageMetadata.Empty;

    private static byte[] Register(byte list, ushort slot, int item, int count, int start, int buyout, int second)
    {
        var body = new byte[37];
        body[1] = list;
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(2), slot);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(4), item);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(8), count);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(12), start);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(16), buyout);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(20), second);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(24), 24);
        return body;
    }

    [Fact]
    public void A_per_unit_buyout_of_a_pile_becomes_the_lot_total_and_keeps_the_unit_price()
    {
        var request = AuctionPayloadCodec.DecodeRegister(Metadata, Register(0, 20, 3037, 30, -1, 100, 100));
        Assert.False(request.Malformed);
        Assert.Equal(new AuctionPricing(null, 3000, 100), request.Pricing);
        Assert.Equal(30, request.Count);
    }

    [Fact]
    public void A_total_buyout_of_a_pile_keeps_its_total_and_derives_the_unit_price()
    {
        var request = AuctionPayloadCodec.DecodeRegister(Metadata, Register(0, 20, 3037, 30, -1, 3000, 100));
        Assert.Equal(new AuctionPricing(null, 3000, 100), request.Pricing);
    }

    [Fact]
    public void A_bid_listing_keeps_both_prices_and_no_unit_price()
    {
        var request = AuctionPayloadCodec.DecodeRegister(Metadata, Register(0, 9, 123, 1, 1000, 5000, 5000));
        Assert.Equal(new AuctionPricing(1000, 5000, null), request.Pricing);
    }

    [Theory]
    [InlineData(36)]
    [InlineData(38)]
    public void A_register_body_of_another_length_is_malformed(int length) =>
        Assert.True(AuctionPayloadCodec.DecodeRegister(Metadata, new byte[length]).Malformed);

    [Fact]
    public void Bid_reads_amount_listing_and_the_name_up_to_its_terminator()
    {
        var body = new byte[26];
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(1), 7777);
        BinaryPrimitives.WriteUInt64LittleEndian(body.AsSpan(5), 42);
        "seller"u8.CopyTo(body.AsSpan(13));
        body[20] = 0x55; // garbage after the terminator is not part of the name
        var request = AuctionPayloadCodec.DecodeBid(Metadata, body);
        Assert.Equal((7777L, new AuctionListingId(42)), (request.Amount, request.Listing));
        Assert.True(AuctionPayloadCodec.SameName(request.SellerName, "seller"u8.ToArray()));
    }

    [Fact]
    public void Search_by_item_reads_its_keys_and_the_forging_tail()
    {
        var body = new byte[18 + 8];
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(1), 60);
        body[5] = 3; body[6] = 10; body[7] = 2;
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(8), 40119);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(10), 123);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(14), 456);
        body[24] = 1; body[25] = 5;
        var request = AuctionPayloadCodec.DecodeSearchByItem(Metadata, body);
        Assert.False(request.Malformed);
        Assert.Equal(60, request.Position);
        var criteria = request.Criteria!;
        Assert.Equal([123, 456], criteria.ItemIds);
        Assert.Null(criteria.Category);
        Assert.Equal(((byte?)3, (byte?)10, (byte?)1, (byte?)5),
            (criteria.MinimumUpgrade, criteria.MaximumUpgrade, criteria.MinimumForging, criteria.MaximumForging));
    }

    [Fact]
    public void Replies_carry_the_market_byte_and_refusals_their_code()
    {
        Assert.Equal([1, 0], AuctionPayloadCodec.Encode(new AuctionRegisterResponse(0, null)));
        Assert.Equal([0, 0x97, 0], AuctionPayloadCodec.Encode(new AuctionRegisterResponse(0, AuctionReplyCode.ListingLimit)));
        Assert.Equal([0, 0x8C, 1], AuctionPayloadCodec.Encode(new AuctionBidResponse(1, AuctionReplyCode.Closed)));
        Assert.Equal([1], AuctionPayloadCodec.Encode(new AuctionBuyPiecesResponse(null)));
        Assert.Equal([0, 0x98], AuctionPayloadCodec.Encode(new AuctionBuyPiecesResponse(AuctionReplyCode.InvalidPrice)));
    }

    private static AuctionListing Listing(long? start, long? buyout, long? bid) => new(new AuctionListingId(42), AuctionMarket.Items,
        AuctionListingState.Active, new CharacterId(71), new AccountId(1), "seller"u8.ToArray(), 3037, 30, 18, 0,
        new(start, buyout, null), bid, bid is null ? null : new CharacterId(72), bid is null ? null : new AccountId(2), 10_000, 500,
        DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch,
        new ItemSnapshot { ItemId = 3037, LegacyKind = 2, Type = ItemTraitClassification.For(2, 3037), Count = 30, IsPile = true });

    [Fact]
    public void A_request_names_a_long_seller_by_the_twelve_bytes_the_client_was_shown()
    {
        var stored = "abcdefghijklmnop"u8.ToArray();
        Assert.True(AuctionPayloadCodec.SameName("abcdefghijkl"u8.ToArray(), stored));
        Assert.False(AuctionPayloadCodec.SameName("abcdefghijk"u8.ToArray(), stored));
        Assert.True(AuctionPayloadCodec.SameName("seller"u8.ToArray(), "seller"u8.ToArray()));
    }

    [Fact]
    public void Lists_have_the_record_sizes_the_client_reads()
    {
        var view = new AuctionListingView(Listing(null, 3000, null), 5);
        Assert.Equal(8 + 168 * 2, AuctionPayloadCodec.Encode(new AuctionSearchResponse(0, null, 9, [view, view])).Length);
        Assert.Equal(3 + 151, AuctionPayloadCodec.Encode(new AuctionMyListingsResponse(0, null, [view])).Length);
        Assert.Equal(4 + 159, AuctionPayloadCodec.Encode(new AuctionMyBidsResponse(0, null, [view])).Length);
        Assert.Equal(58, AuctionPayloadCodec.Encode(new AuctionAveragePriceResponse(0, null, new(null, [(100, 18)]))).Length);
    }

    [Fact]
    public void A_search_record_shows_no_bid_as_minus_one_and_the_remaining_count_of_a_pile()
    {
        var body = AuctionPayloadCodec.Encode(new AuctionSearchResponse(0, null, 1, [new(Listing(null, 3000, null), 5)]));
        var record = body.AsSpan(8, 168);
        Assert.Equal(42UL, BinaryPrimitives.ReadUInt64LittleEndian(record));
        Assert.Equal(uint.MaxValue, BinaryPrimitives.ReadUInt32LittleEndian(record[9..]));
        Assert.Equal(3000U, BinaryPrimitives.ReadUInt32LittleEndian(record[13..]));
        Assert.Equal("seller"u8.ToArray(), record.Slice(21, 6).ToArray());
        Assert.Equal(0, record[27]);
        Assert.Equal(5, record[42]);
        Assert.Equal(3037, BinaryPrimitives.ReadInt32LittleEndian(record[(43 + 1)..]));
        Assert.Equal(18, BinaryPrimitives.ReadInt32LittleEndian(record[(43 + 6)..]));
    }

    [Fact]
    public void A_record_with_a_bid_shows_the_current_bid()
    {
        var body = AuctionPayloadCodec.Encode(new AuctionSearchResponse(0, null, 1, [new(Listing(1000, 5000, 1200), 1)]));
        Assert.Equal(1200U, BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(8 + 9)));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(3600, 1)]
    [InlineData(3601, 2)]
    [InlineData(86400, 25)]
    public void Hours_left_count_as_the_70_server_counts_them(int seconds, byte hours) =>
        Assert.Equal(hours, GameAuctionCommandHandler.HoursLeft(DateTimeOffset.UnixEpoch.AddSeconds(seconds), DateTimeOffset.UnixEpoch));

    [Fact]
    public void A_consignment_listing_reads_its_tier_price_and_hours()
    {
        var body = new byte[28];
        body[0] = 1;
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(4), 2681725);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(8), 1);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(12), -1);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(16), 120);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(24), 24);
        var request = AuctionPayloadCodec.DecodeRegister(Metadata, body);
        Assert.False(request.Malformed);
        Assert.Equal((2681725, new AuctionPricing(null, 120)), (request.ItemId, request.Pricing));
    }

    [Fact]
    public void A_consignment_purchase_reads_price_and_the_four_byte_listing()
    {
        var body = Convert.FromHexString("01E80300001000000000000000" + "7DEB2800");
        var request = AuctionPayloadCodec.DecodeBid(Metadata, body);
        Assert.Equal((1, 1000L, new AuctionListingId(16)), (request.PayType, request.Amount, request.Listing));
    }

    [Fact]
    public void A_consignment_record_shows_its_tier_and_no_bid()
    {
        var listing = new AuctionListing(new AuctionListingId(7), AuctionMarket.GoldConsignment, AuctionListingState.Active,
            new CharacterId(71), new AccountId(1), "seller"u8.ToArray(), 2681725, 1, 1, 1_000_000, new(null, 120), null, null, null,
            0, 500, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null);
        var body = AuctionPayloadCodec.Encode(new AuctionSearchResponse(1, null, 1, [new(listing, 24)]));
        Assert.Equal(1, body[1]);
        var record = body.AsSpan(8, 168);
        Assert.Equal(uint.MaxValue, BinaryPrimitives.ReadUInt32LittleEndian(record[9..]));
        Assert.Equal(120U, BinaryPrimitives.ReadUInt32LittleEndian(record[13..]));
        Assert.Equal(2681725, BinaryPrimitives.ReadInt32LittleEndian(record[44..]));
        Assert.Equal(1, BinaryPrimitives.ReadInt32LittleEndian(record[49..]));
    }
}
