using Dfo.Server.Abstractions.Effects;
using System.Buffers.Binary;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.Forging;

public sealed record ForgingRequest(GameMessageMetadata Metadata,
    byte TargetListType, short TargetSlot, int TargetItemId,
    short MaterialSlot, byte[] ReportedNameBytes, byte Variant) : IEffectSettlementRequest
{
    public ReplayProjectionNeeds MinimumReplayNeeds => ReplayProjectionNeeds.Inventory;
    public ClientMessageType MessageType => ClientMessageType.GameSeparateUpgrade;
}

public sealed record ForgingResponse(bool Success, byte ErrorCode = 0,
    short MaterialSlot = -1, int MaterialRemaining = 0,
    byte OldLevel = 0, bool ForgeSucceeded = false, byte NewLevel = 0,
    byte TargetListType = 0, short TargetSlot = -1) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameSeparateUpgrade;
}

internal sealed class ForgingDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        if (body.Length < 15)
            throw new InvalidDataException("UPGRADE_ITEM_SEPARATE body is too short.");
        var nameLength = BinaryPrimitives.ReadInt32LittleEndian(body[9..]);
        if (nameLength is < 1 or > 128 || body.Length != 14 + nameLength)
            throw new InvalidDataException("UPGRADE_ITEM_SEPARATE has an invalid dynamic tail.");
        return new ForgingRequest(metadata, body[0],
            BinaryPrimitives.ReadInt16LittleEndian(body[1..]),
            BinaryPrimitives.ReadInt32LittleEndian(body[3..]),
            BinaryPrimitives.ReadInt16LittleEndian(body[7..]),
            body.Slice(13, nameLength).ToArray(), body[^1]);
    }
}

internal sealed class ForgingEncoder : GameCmdMessageEncoder<ForgingResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(ForgingResponse message) =>
        OnePayload(message, EncodeBody(message));

    internal static byte[] EncodeBody(ForgingResponse message)
    {
        if (!message.Success) return [0, message.ErrorCode];
        var body = new byte[13];
        body[0] = 1;
        BinaryPrimitives.WriteInt16LittleEndian(body.AsSpan(1), message.MaterialSlot);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(3), message.MaterialRemaining);
        body[7] = message.OldLevel;
        body[8] = message.ForgeSucceeded ? (byte)0 : (byte)1;
        body[9] = message.NewLevel;
        body[10] = message.TargetListType;
        BinaryPrimitives.WriteInt16LittleEndian(body.AsSpan(11), message.TargetSlot);
        return body;
    }
}
