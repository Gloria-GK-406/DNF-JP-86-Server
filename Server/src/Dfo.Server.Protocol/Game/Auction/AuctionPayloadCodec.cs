using System.Buffers.Binary;
using Dfo.Server.Abstractions.Auction;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.Auction;

/// <summary>Auction request and reply bodies.</summary>
internal static class AuctionPayloadCodec
{
    public const int NameField = 13;
    public const int ItemDisplaySize = 117;
    public const int SearchRecordSize = 168;
    public const int MyListingRecordSize = 151;
    public const int MyBidRecordSize = 159;
    private const uint None = uint.MaxValue;

    private static byte PayTypeOf(ReadOnlySpan<byte> body) => body.Length > 0 ? body[0] : byte.MaxValue;

    public static AuctionAveragePriceRequest DecodeAveragePrice(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        var pt = PayTypeOf(body);
        var ok = pt == 0 && body.Length == 8 || pt == 1 && body.Length == 6;
        return new(metadata, pt, !ok, ok ? I32(body, 1) : 0);
    }

    /// <remarks>
    /// Item layout (sender A21 VA 01E2F484 per the reference server, address less 0x400000):
    /// <c>pt, u8 list, u16 slot, u32 item, u32 count, u32 start, u32 buyout, u32 second, u32 hours,
    /// (u16,u8) ×3 search hints</c>. -1 is "not set". For a pile the second entry is the unit price:
    /// the buyout is either that unit price (the client's per-unit entry) or unit × count; either way
    /// the totals here are for the whole lot and the unit price is kept for buying by the piece.
    /// </remarks>
    public static AuctionRegisterRequest DecodeRegister(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        var pt = PayTypeOf(body);
        if (pt == 0 && body.Length == 37)
        {
            var list = body[1];
            var slot = BinaryPrimitives.ReadUInt16LittleEndian(body[2..]);
            var item = I32(body, 4);
            var count = I32(body, 8);
            var start = Price(body, 12);
            var buyout = Price(body, 16);
            var second = Price(body, 20);
            var hours = BinaryPrimitives.ReadUInt32LittleEndian(body[24..]);
            BusinessSlot business;
            try { business = A21InventoryLayout.ToBusinessSlot(new GameSlot((InventoryContainerType)list, slot)); }
            catch (InventorySlotMappingException) { return Malformed(); }
            if (count <= 0) return Malformed();
            long? unit = null;
            if (second is { } s && start is null && buyout is { } b && count > 1)
            {
                if (b == s) { buyout = s * count; unit = s; }
                else if (b == s * count) unit = s;
            }
            return new(metadata, pt, false, business, item, count, new(start, buyout, unit), hours);
        }
        return Malformed();

        AuctionRegisterRequest Malformed() => new(metadata, pt, true, default, 0, 0, new(null, null), 0);
    }

    public static AuctionCancelRequest DecodeCancel(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        var pt = PayTypeOf(body);
        var ok = pt is 0 or 1 && body.Length == 9;
        return new(metadata, pt, !ok, ok ? Listing(body, 1) : default);
    }

    public static AuctionBidRequest DecodeBid(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        var pt = PayTypeOf(body);
        if (pt == 0 && body.Length == 26)
            return new(metadata, pt, false, BinaryPrimitives.ReadUInt32LittleEndian(body[1..]), Listing(body, 5), Name(body[13..26]));
        if (pt == 1 && body.Length == 17)
            return new(metadata, pt, false, BinaryPrimitives.ReadUInt32LittleEndian(body[1..]),
                new AuctionListingId(BinaryPrimitives.ReadUInt32LittleEndian(body[5..])), []);
        return new(metadata, pt, true, 0, default, []);
    }

    /// <remarks>
    /// By item, items market: <c>pt, u32 position, u8 upgrade min, u8 max, u8 n, u16 category, u32 × n,
    /// u16 ×3 option filters, u8 forging min, u8 max</c> = 18 + 4n. By criteria, 20 bytes:
    /// <c>pt, u32 position, u16 category, u8 rarity (7 any), u8 upgrade min, max, u8 level min, max (0 or 86
    /// any), u16 ×3, u8 forging min, max</c>. Random option filters are not applied.
    /// </remarks>
    public static AuctionSearchRequest DecodeSearchByItem(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        var pt = PayTypeOf(body);
        if (pt == 0 && body.Length >= 18)
        {
            var n = body[7];
            if (body.Length != 18 + 4 * n || n == 0) return new(metadata, pt, true, 0, null, false);
            var ids = new int[n];
            for (var i = 0; i < n; i++) ids[i] = I32(body, 10 + 4 * i);
            var tail = 10 + 4 * n + 6;
            var category = BinaryPrimitives.ReadUInt16LittleEndian(body[8..]);
            return new(metadata, pt, false, I32(body, 1), new(AuctionMarket.Items, ids, Category(category), null,
                body[5], Max(body[6]), null, null, body[tail], Max(body[tail + 1])), false);
        }
        if (pt == 1 && body.Length == 14)
            return new(metadata, pt, false, I32(body, 1),
                new(AuctionMarket.GoldConsignment, [I32(body, 10)], null, null, null, null, null, null, null, null), false);
        return new(metadata, pt, true, 0, null, false);
    }

    public static AuctionSearchRequest DecodeSearchByCriteria(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        var pt = PayTypeOf(body);
        if (pt != 0 || body.Length != 20) return new(metadata, pt, true, 0, null, true);
        var category = BinaryPrimitives.ReadUInt16LittleEndian(body[5..]);
        var rarity = body[7];
        var levelMax = body[11];
        return new(metadata, pt, false, I32(body, 1), new(AuctionMarket.Items, null, Category(category),
            rarity >= 7 ? null : rarity, body[8], Max(body[9]), body[10], levelMax is 0 or >= 86 ? null : levelMax,
            body[18], Max(body[19])), true);
    }

    public static AuctionMineRequest DecodeMine(GameMessageMetadata metadata, ReadOnlySpan<byte> body, bool bids)
    {
        var pt = PayTypeOf(body);
        return new(metadata, pt, !(body.Length == 1 && pt is 0 or 1), bids);
    }

    public static AuctionBuyPiecesRequest DecodeBuyPieces(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        body.Length == 33
            ? new(metadata, false, BinaryPrimitives.ReadUInt32LittleEndian(body), I32(body, 4), Listing(body, 8), Name(body[16..29]), I32(body, 29))
            : new(metadata, true, 0, 0, default, [], 0);

    /// <summary>Whether a request's 13-byte name names the stored seller: the bytes up to the first zero.</summary>
    public static bool SameName(byte[] requested, byte[] stored) => requested.AsSpan().SequenceEqual(stored);

    // ---- Replies ------------------------------------------------------------------------------

    public static byte[] Encode(AuctionReply reply)
    {
        if (reply.Refusal is { } code)
            return reply is AuctionBuyPiecesResponse ? [0, (byte)code] : [0, (byte)code, reply.PayType];
        return reply switch
        {
            AuctionBuyPiecesResponse => [1],
            AuctionAveragePriceResponse prices => AveragePrice(prices),
            AuctionSearchResponse search => Search(search),
            AuctionMyListingsResponse mine => MyListings(mine),
            AuctionMyBidsResponse bids => MyBids(bids),
            _ => [1, reply.PayType],
        };
    }

    private static byte[] AveragePrice(AuctionAveragePriceResponse reply)
    {
        var body = new byte[58];
        body[0] = 1; body[1] = reply.PayType;
        var prices = reply.Prices;
        var suggested = prices?.AverageUnitPrice ?? (prices is { LowestOffers.Count: > 0 } ? prices.LowestOffers[0].UnitPrice : (long?)null);
        U32(body, 2, suggested is { } s ? (uint)Math.Min(s, int.MaxValue) : None);
        for (var i = 0; i < 6 && prices is not null && i < prices.LowestOffers.Count; i++)
        {
            U32(body, 10 + 4 * i, (uint)Math.Min(prices.LowestOffers[i].UnitPrice, int.MaxValue));
            U32(body, 34 + 4 * i, (uint)prices.LowestOffers[i].Count);
        }
        return body;
    }

    private static byte[] Search(AuctionSearchResponse reply)
    {
        var body = new byte[8 + SearchRecordSize * reply.Listings.Count];
        body[0] = 1; body[1] = reply.PayType;
        U32(body, 2, (uint)reply.Total);
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(6), checked((ushort)reply.Listings.Count));
        for (var i = 0; i < reply.Listings.Count; i++)
        {
            var record = body.AsSpan(8 + SearchRecordSize * i, SearchRecordSize);
            var (listing, hours) = (reply.Listings[i].Listing, reply.Listings[i].HoursLeft);
            WireId(record, 0, listing);
            U32(record, 9, CurrentPrice(listing));
            U32(record, 13, BuyoutPrice(listing));
            U32(record, 17, None);
            NameTo(record.Slice(21, NameField), listing.SellerName);
            record[42] = hours;
            Display(record.Slice(43, ItemDisplaySize), listing);
        }
        return body;
    }

    private static byte[] MyListings(AuctionMyListingsResponse reply)
    {
        var body = new byte[3 + MyListingRecordSize * reply.Listings.Count];
        body[0] = 1; body[1] = reply.PayType; body[2] = checked((byte)reply.Listings.Count);
        for (var i = 0; i < reply.Listings.Count; i++)
        {
            var record = body.AsSpan(3 + MyListingRecordSize * i, MyListingRecordSize);
            var (listing, hours) = (reply.Listings[i].Listing, reply.Listings[i].HoursLeft);
            WireId(record, 0, listing);
            U32(record, 8, CurrentPrice(listing));
            U32(record, 12, BuyoutPrice(listing));
            NameTo(record.Slice(16, NameField), listing.SellerName);
            record[29] = hours;
            Display(record.Slice(30, ItemDisplaySize), listing);
            U32(record, 147, None);
        }
        return body;
    }

    private static byte[] MyBids(AuctionMyBidsResponse reply)
    {
        var body = new byte[4 + MyBidRecordSize * reply.Listings.Count];
        body[0] = 1; body[1] = 0;
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(2), checked((ushort)reply.Listings.Count));
        for (var i = 0; i < reply.Listings.Count; i++)
        {
            var record = body.AsSpan(4 + MyBidRecordSize * i, MyBidRecordSize);
            var (listing, hours) = (reply.Listings[i].Listing, reply.Listings[i].HoursLeft);
            WireId(record, 0, listing);
            U32(record, 8, CurrentPrice(listing));
            U32(record, 12, BuyoutPrice(listing));
            NameTo(record.Slice(16, NameField), listing.SellerName);
            record[37] = hours;
            Display(record.Slice(38, ItemDisplaySize), listing);
            U32(record, 155, None);
        }
        return body;
    }

    /// <summary>The current bid, else the opening price; -1 when only a buyout is asked (no "current bid" shown).</summary>
    private static uint CurrentPrice(AuctionListing listing) =>
        (listing.CurrentBid ?? listing.Pricing.StartPrice) is { } price ? (uint)price : None;

    private static uint BuyoutPrice(AuctionListing listing) => listing.Pricing.BuyoutPrice is { } price ? (uint)price : None;

    /// <summary>The 8-byte order identity: the listing id in the low half, the high half zero.</summary>
    private static void WireId(Span<byte> target, int offset, AuctionListing listing) =>
        BinaryPrimitives.WriteUInt64LittleEndian(target[offset..], (ulong)listing.Id.Value);

    /// <summary>
    /// The 117-byte item display (83 core + 30 avatar sockets + 4 colors), field for field as the
    /// reference server's A21 projection writes it. A pile's count goes in the value at +6.
    /// </summary>
    internal static void Display(Span<byte> target, AuctionListing listing)
    {
        target.Clear();
        if (listing.DisplayCore is not { } core)
        {
            BinaryPrimitives.WriteInt32LittleEndian(target[1..], listing.ItemId);
            BinaryPrimitives.WriteInt32LittleEndian(target[6..], 1);
            return;
        }
        target[0] = core.SealFlag;
        BinaryPrimitives.WriteInt32LittleEndian(target[1..], core.DisplayItemId);
        target[5] = core.Attribute;
        // A pile's core value was its count when listed; equipment keeps its instance value.
        BinaryPrimitives.WriteInt32LittleEndian(target[6..], core.Value == listing.QuantityInitial ? listing.QuantityRemaining : core.Value);
        BinaryPrimitives.WriteUInt16LittleEndian(target[10..], core.Durability);
        BinaryPrimitives.WriteInt32LittleEndian(target[12..], core.EnchantCardId);
        target[16] = core.AmplifyType;
        BinaryPrimitives.WriteUInt16LittleEndian(target[17..], core.AmplifyValue);
        BinaryPrimitives.WriteInt32LittleEndian(target[29..], core.EmblemId1);
        BinaryPrimitives.WriteInt32LittleEndian(target[33..], core.EmblemId2);
        Option(target[37..], core.RandomOption0);
        Option(target[40..], core.RandomOption1);
        Option(target[43..], core.RandomOption2);
        target[46] = core.RandomOptionState;
        Option(target[47..], core.RandomOptionChange);
        target[50] = core.RandomOptionChangeState;
        target[51] = core.GenuineUpgrade;
        BinaryPrimitives.WriteInt32LittleEndian(target[54..], core.ExpireTime);
        target[61] = core.EnchantUpgradeCount;
        target[62] = core.TailUnknown2;
        target[63] = core.EmancipateEquipmentLevel;

        static void Option(Span<byte> at, Dfo.Server.Data.Inventory.RandomOptionState option)
        {
            at[0] = option.Type; at[1] = option.Value1; at[2] = option.Value2;
        }
    }

    private static void NameTo(Span<byte> field, byte[] name)
    {
        field.Clear();
        name.AsSpan(0, Math.Min(name.Length, field.Length - 1)).CopyTo(field);
    }

    private static byte[] Name(ReadOnlySpan<byte> field)
    {
        var end = field.IndexOf((byte)0);
        return field[..(end < 0 ? field.Length : end)].ToArray();
    }

    private static ushort? Category(ushort value) => value is 0 or 40119 ? null : value;
    private static byte? Max(byte value) => value == 0 ? null : value;
    private static int I32(ReadOnlySpan<byte> body, int offset) => BinaryPrimitives.ReadInt32LittleEndian(body[offset..]);
    private static AuctionListingId Listing(ReadOnlySpan<byte> body, int offset) =>
        new((long)(BinaryPrimitives.ReadUInt64LittleEndian(body[offset..]) & long.MaxValue));

    private static long? Price(ReadOnlySpan<byte> body, int offset)
    {
        var value = BinaryPrimitives.ReadInt32LittleEndian(body[offset..]);
        return value <= 0 ? null : value;
    }

    private static void U32(Span<byte> target, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(target[offset..], value);
}

internal sealed class AuctionAveragePriceDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) => AuctionPayloadCodec.DecodeAveragePrice(metadata, body);
}

internal sealed class AuctionRegisterDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) => AuctionPayloadCodec.DecodeRegister(metadata, body);
}

internal sealed class AuctionCancelDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) => AuctionPayloadCodec.DecodeCancel(metadata, body);
}

internal sealed class AuctionBidDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) => AuctionPayloadCodec.DecodeBid(metadata, body);
}

internal sealed class AuctionSearchByItemDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) => AuctionPayloadCodec.DecodeSearchByItem(metadata, body);
}

internal sealed class AuctionSearchByCriteriaDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) => AuctionPayloadCodec.DecodeSearchByCriteria(metadata, body);
}

internal sealed class AuctionMyListingsDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) => AuctionPayloadCodec.DecodeMine(metadata, body, bids: false);
}

internal sealed class AuctionMyBidsDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) => AuctionPayloadCodec.DecodeMine(metadata, body, bids: true);
}

internal sealed class AuctionBuyPiecesDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) => AuctionPayloadCodec.DecodeBuyPieces(metadata, body);
}

internal sealed class AuctionReplyEncoder<TReply> : GameCmdMessageEncoder<TReply> where TReply : AuctionReply
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(TReply message) =>
        OnePayload(message, AuctionPayloadCodec.Encode(message));
}
