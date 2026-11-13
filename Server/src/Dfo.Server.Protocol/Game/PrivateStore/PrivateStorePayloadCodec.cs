using System.Buffers.Binary;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.PrivateStore;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.PrivateStore;

internal static class PrivateStorePayloadCodec
{
    public const int TitleLimit = 40;
    private const ushort NoDoll = 0xFFFF;

    public static PrivateStoreCreateRequest DecodeCreate(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        if (body.Length != 8) return new(metadata, true, 0, 0, 0, 0, null);
        var doll = BinaryPrimitives.ReadUInt16LittleEndian(body[6..]);
        BusinessSlot? dollSlot = null;
        if (doll != NoDoll && !TrySlot(0, doll, out dollSlot)) return new(metadata, true, 0, 0, 0, 0, null);
        return new(metadata, false, body[0], body[1], BinaryPrimitives.ReadInt16LittleEndian(body[2..]),
            BinaryPrimitives.ReadInt16LittleEndian(body[4..]), dollSlot);
    }

    public static PrivateStoreDisplayRequest DecodeDisplay(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        var malformed = new PrivateStoreDisplayRequest(metadata, true, [], []);
        if (body.Length < 4) return malformed;
        var length = BinaryPrimitives.ReadInt32LittleEndian(body);
        if (length is < 0 or > TitleLimit || body.Length < 4 + length + 1) return malformed;
        var title = body.Slice(4, length).ToArray();
        var offset = 4 + length;
        var count = body[offset++];
        if (body.Length != offset + count * 13 + 2) return malformed;
        var lines = new List<PrivateStoreDisplayLine>(count);
        for (var i = 0; i < count; i++, offset += 13)
        {
            var entry = body.Slice(offset, 13);
            var display = BinaryPrimitives.ReadUInt16LittleEndian(entry);
            if (display > 13 || !TrySlot(entry[6], BinaryPrimitives.ReadInt16LittleEndian(entry[7..]), out var source)) return malformed;
            lines.Add(new((byte)display, BinaryPrimitives.ReadInt32LittleEndian(entry[2..]), source!.Value,
                BinaryPrimitives.ReadInt32LittleEndian(entry[9..])));
        }
        return new(metadata, false, title, lines);
    }

    public static PrivateStoreSimpleRequest DecodeSimple(GameMessageMetadata metadata, ReadOnlySpan<byte> body, ClientMessageType type)
    {
        if (type != ClientMessageType.GamePrivateStoreEnter) return new(metadata, false, type);
        // 70 reads a u16; the reference server's A21 build reads four bytes. Either names the owner's wire id.
        return body.Length switch
        {
            2 => new(metadata, false, type, BinaryPrimitives.ReadUInt16LittleEndian(body)),
            4 when BinaryPrimitives.ReadUInt32LittleEndian(body) <= ushort.MaxValue =>
                new(metadata, false, type, (ushort)BinaryPrimitives.ReadUInt32LittleEndian(body)),
            _ => new(metadata, true, type),
        };
    }

    public static PrivateStoreLineRequest DecodeLine(GameMessageMetadata metadata, ReadOnlySpan<byte> body, bool buy) =>
        body.Length == 6 && BinaryPrimitives.ReadUInt16LittleEndian(body) <= 13
            ? new(metadata, false, buy, (byte)BinaryPrimitives.ReadUInt16LittleEndian(body), BinaryPrimitives.ReadInt32LittleEndian(body[2..]))
            : new(metadata, true, buy, 0, 0);

    private static bool TrySlot(byte list, int slot, out BusinessSlot? business)
    {
        try
        {
            business = A21InventoryLayout.ToBusinessSlot(new GameSlot((InventoryContainerType)list, slot));
            return true;
        }
        catch (InventorySlotMappingException)
        {
            business = null;
            return false;
        }
    }

    // ---- Replies and notices ------------------------------------------------------------------

    public static byte[] Encode(PrivateStoreReply reply) =>
        reply.Body is { } body ? [1, .. body] : [0, reply.Code];

    /// <summary>CMD 0x53 body: <c>u16 owner, u8 n, n × line</c>.</summary>
    public static byte[] Lines(ushort owner, IReadOnlyList<PrivateStoreLine> lines)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(owner);
        writer.Write(checked((byte)lines.Count));
        foreach (var line in lines) WriteLine(writer, line.DisplaySlot, line.UnitPrice, line.Snapshot);
        return stream.ToArray();
    }

    /// <summary>CMD 0x195 body: one line with its price.</summary>
    public static byte[] Line(PrivateStoreLine line)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        WriteLine(writer, line.DisplaySlot, line.UnitPrice, line.Snapshot);
        return stream.ToArray();
    }

    /// <summary>CMD 0x52 body: <c>i32 gold, i32 0, i32 0</c> and the bought item at the slot it landed in, without a price.</summary>
    public static byte[] Bought(int buyerGold, ushort destination, ItemSnapshot item)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(buyerGold);
        writer.Write(0);
        writer.Write(0);
        WriteLine(writer, destination, null, item);
        return stream.ToArray();
    }

    /// <summary>NOTI 0x4E: <c>u16 owner, u8 town, u8 area, i16 x, i16 y, u8 doll</c>.</summary>
    public static byte[] Opened(ushort owner, PrivateStoreSpot spot, bool doll)
    {
        var body = new byte[9];
        BinaryPrimitives.WriteUInt16LittleEndian(body, owner);
        body[2] = spot.TownId; body[3] = spot.AreaId;
        BinaryPrimitives.WriteInt16LittleEndian(body.AsSpan(4), spot.X);
        BinaryPrimitives.WriteInt16LittleEndian(body.AsSpan(6), spot.Y);
        body[8] = doll ? (byte)1 : (byte)0;
        return body;
    }

    /// <summary>NOTI 0x51: <c>u16 owner, i32 length, title</c>.</summary>
    public static byte[] Title(ushort owner, byte[] title)
    {
        var body = new byte[2 + 4 + title.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(body, owner);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(2), title.Length);
        title.CopyTo(body.AsSpan(6));
        return body;
    }

    /// <summary>NOTI 0x4F: <c>u16 owner</c>.</summary>
    public static byte[] Closed(ushort owner) => BitConverter.GetBytes(owner);

    /// <summary>NOTI 0x53: <c>u16 display slot, i32 left</c>.</summary>
    public static byte[] LineLeft(byte slot, int left)
    {
        var body = new byte[6];
        BinaryPrimitives.WriteUInt16LittleEndian(body, slot);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(2), left);
        return body;
    }

    /// <summary>NOTI 0x54, one stall: <c>u8 1, u16 owner, i32 length, title, u8 1, u8 town, u8 area, i16 x, i16 y, u8 doll</c>.</summary>
    public static byte[] AreaListing(ushort owner, PrivateStoreView store)
    {
        var title = store.Title ?? [];
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)1);
        writer.Write(owner);
        writer.Write(title.Length);
        writer.Write(title);
        writer.Write((byte)1);
        writer.Write(store.Spot.TownId);
        writer.Write(store.Spot.AreaId);
        writer.Write(store.Spot.X);
        writer.Write(store.Spot.Y);
        writer.Write(store.Doll ? (byte)1 : (byte)0);
        return stream.ToArray();
    }

    /// <summary>
    /// One stall line as the A21 client reads it (the reference server's A21 writer, field for field):
    /// <c>u16 slot, [i32 price], i32 item, i32 value, u8 attr, u16 durability, i32 card, u8 upgrade,
    /// u8 amplify, u16 amplify value, i32 expiry, u8 n + n × i32 emblem, u16 rune, random options,
    /// u8 genuine, u8 emancipate, u8 trade, u16, u8, u8 sort lock, u8, u8, u8 uses, u32 4, u32 0, u32 8, u16 × 4, u8</c>.
    /// </summary>
    private static void WriteLine(BinaryWriter writer, ushort slot, long? price, ItemSnapshot item)
    {
        var core = item.ToCore();
        writer.Write(slot);
        if (price is { } unit) writer.Write(checked((int)unit));
        writer.Write(core.ItemId);
        writer.Write(core.Value);
        writer.Write(core.Attribute);
        writer.Write(core.Durability);
        writer.Write(core.EnchantCardId);
        writer.Write(core.EnchantUpgradeCount);
        writer.Write(core.AmplifyType);
        writer.Write(core.AmplifyValue);
        writer.Write(core.ExpireTime);
        var emblems = new[] { core.EmblemId1, core.EmblemId2 }.Take(Math.Min(2, (int)core.EmblemSocketCount)).ToArray();
        writer.Write((byte)emblems.Length);
        foreach (var emblem in emblems) writer.Write(emblem);
        writer.Write(core.Rune);
        var random = new[] { core.RandomOption0, core.RandomOption1, core.RandomOption2 }.Where(value => value.Type != 0).ToArray();
        writer.Write((byte)random.Length);
        foreach (var value in random) { writer.Write(value.Type); writer.Write(value.Value1); writer.Write(value.Value2); }
        if (random.Length > 0)
        {
            writer.Write(core.RandomOptionState); writer.Write(core.RandomOptionChangedIndex);
            if (core.RandomOptionChangedIndex != 0xFF)
            {
                writer.Write(core.RandomOptionChangeState); writer.Write(core.RandomOptionChange.Type);
                writer.Write(core.RandomOptionChange.Value1); writer.Write(core.RandomOptionChange.Value2);
            }
        }
        writer.Write(core.GenuineUpgrade);
        writer.Write(core.EmancipateEquipmentLevel);
        writer.Write(core.TradeRestriction);
        writer.Write(core.GrowthExperience);
        writer.Write(core.GrowthMaintainedFatigue);
        writer.Write(core.SortLockFlag);
        writer.Write(core.TailUnknown2);
        writer.Write(core.TailUnknown3);
        writer.Write(core.RemainUseCount);
        writer.Write(4u);
        writer.Write(0u);
        writer.Write(8u);
        writer.Write(new byte[8]);
        writer.Write((byte)0);
    }
}

internal sealed class PrivateStoreCreateDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) => PrivateStorePayloadCodec.DecodeCreate(metadata, body);
}

internal sealed class PrivateStoreDisplayDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) => PrivateStorePayloadCodec.DecodeDisplay(metadata, body);
}

internal sealed class PrivateStoreEnterDecoder() : PrivateStoreSimpleDecoderBase(ClientMessageType.GamePrivateStoreEnter);
internal sealed class PrivateStoreExitDecoder() : PrivateStoreSimpleDecoderBase(ClientMessageType.GamePrivateStoreExit);
internal sealed class PrivateStoreRemoveDecoder() : PrivateStoreSimpleDecoderBase(ClientMessageType.GamePrivateStoreRemove);
internal sealed class PrivateStoreMoveToGateDecoder() : PrivateStoreSimpleDecoderBase(ClientMessageType.GamePrivateStoreMoveToGate);

internal abstract class PrivateStoreSimpleDecoderBase(ClientMessageType type) : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) => PrivateStorePayloadCodec.DecodeSimple(metadata, body, type);
}

internal sealed class PrivateStoreBuyDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) => PrivateStorePayloadCodec.DecodeLine(metadata, body, buy: true);
}

internal sealed class PrivateStoreVerifyDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) => PrivateStorePayloadCodec.DecodeLine(metadata, body, buy: false);
}

internal sealed class PrivateStoreReplyEncoder : GameCmdMessageEncoder<PrivateStoreReply>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(PrivateStoreReply message) =>
        OnePayload(message, PrivateStorePayloadCodec.Encode(message));
}

internal sealed class PrivateStoreNoticeEncoder : GameNotiMessageEncoder<PrivateStoreNotice>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(PrivateStoreNotice message) =>
        OnePayload(message, message.Body);
}
