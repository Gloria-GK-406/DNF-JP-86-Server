using System.Buffers.Binary;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.Warehouse;

public sealed record WarehouseRequest(GameMessageMetadata Metadata, int Value) : IEffectSettlementRequest
{
    public ReplayProjectionNeeds MinimumReplayNeeds => ReplayProjectionNeeds.Inventory;
    public ClientMessageType MessageType => (ClientMessageType)(0x00010000 | Metadata.Type);
}
public sealed record WarehouseResponse(ushort Type, bool Success, byte ErrorCode = 0, int Gold = 0) : IGameCmdMessage
{
    public ServerMessageType MessageType => (ServerMessageType)(0x00010000 | Type);
}
internal sealed class WarehouseDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        var length = metadata.Type switch { 0x0131 or 0x0132 => 0, 0x0133 or 0x0134 => 4, 0x0198 or 0x01AF => 2, _ => -1 };
        if (body.Length != length) throw new InvalidDataException($"Warehouse CMD {metadata.Type:X4} requires {length} body bytes.");
        return new WarehouseRequest(metadata, length == 0 ? 0 : length == 2
            ? BinaryPrimitives.ReadUInt16LittleEndian(body) : BinaryPrimitives.ReadInt32LittleEndian(body));
    }
}
internal sealed class WarehouseResponseEncoder : GameCmdMessageEncoder<WarehouseResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(WarehouseResponse message) => OnePayload(message.Type, EncodeBody(message));
    internal static byte[] EncodeBody(WarehouseResponse message)
    {
        if (message.Type is not (0x0131 or 0x0132 or 0x0133 or 0x0134 or 0x0198))
            throw new InvalidDataException("No A21 warehouse response handler for this command.");
        if (!message.Success) return [0, message.ErrorCode];
        if (message.Type is not (0x0133 or 0x0134)) return [1];
        var body = new byte[5]; body[0] = 1;
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(1), message.Gold);
        return body;
    }
}
