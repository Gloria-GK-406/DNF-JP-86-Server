using System.Buffers.Binary;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.Trade;

internal sealed record TradePeerReply(ushort Type, byte[] Body) : IGameCmdMessage
{
    public ServerMessageType MessageType => (ServerMessageType)(0x00010000 | Type);
}

internal sealed record TradeNotice(ushort Type, byte[] Body) : IGameNotiMessage
{
    public ServerMessageType MessageType => (ServerMessageType)Type;
}

internal sealed class TradePeerReplyEncoder : GameCmdMessageEncoder<TradePeerReply>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(TradePeerReply message) =>
        OnePayload(message.Type, message.Body);
}

internal sealed class TradeNoticeEncoder : GameNotiMessageEncoder<TradeNotice>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(TradeNotice message) =>
        OnePayload(message.Type, message.Body);
}

internal static class TradePackets
{
    internal static TradeNotice Invitation(ushort actor, int peer)
    {
        var body = new byte[11];
        BinaryPrimitives.WriteUInt16LittleEndian(body, actor);
        body[2] = TradeProtocol.PeerType;
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(3), peer);
        return new(0x0007, body);
    }

    internal static TradePeerReply Accepted(ushort inviter) =>
        new(0x000B, [1, (byte)inviter, (byte)(inviter >> 8), TradeProtocol.PeerType, 1]);

    internal static TradeNotice Open(ushort invitee, int peer)
    {
        var body = new byte[12];
        BinaryPrimitives.WriteUInt16LittleEndian(body, invitee);
        body[2] = TradeProtocol.PeerType;
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(3), peer);
        body[7] = 1;
        return new(0x0008, body);
    }

    internal static TradeNotice Item(int index, int itemId, int count, ItemSnapshot? snapshot, DateTimeOffset now)
    {
        using var buffer = new MemoryStream(GameInventoryItemEntry.CommonLength);
        using var writer = new BinaryWriter(buffer);
        if (count == 0 || index == 0 || AccountMaterialCatalog.ContainsItem(itemId))
        {
            writer.Write(checked((short)index));
            writer.Write(index == 0 ? 0 : count == 0 ? -1 : itemId);
            writer.Write(count);
            writer.Write(new byte[GameInventoryItemEntry.CommonLength - 10]);
        }
        else
        {
            var item = snapshot ?? throw new InvalidOperationException("A trade quote is missing its snapshot.");
            item = item.IsPile ? item with { Count = count } : item;
            GameInventoryItemEntry.Write(writer, new InventoryItemState(null, index, item.ToCore()), now,
                InventoryContainerType.Main);
        }
        return new(0x000F, buffer.ToArray());
    }

    internal static TradeNotice State(ushort actor, byte state) =>
        new(0x0011, [(byte)actor, (byte)(actor >> 8), state]);

    internal static TradeNotice Registered() => new(0x028F, []);

    internal static TradeNotice Closed(bool settled) => new(settled ? (ushort)0x0012 : (ushort)0x0010, [0, 0]);
}
