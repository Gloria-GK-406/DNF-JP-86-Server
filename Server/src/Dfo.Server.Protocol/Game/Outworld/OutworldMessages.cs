using Dfo.Server.Abstractions.Effects;
using System.Buffers.Binary;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.Outworld;

public sealed record OutworldRequest(GameMessageMetadata Metadata, bool Purify,
    byte WireAction, short TargetSlot, int TargetItemId,
    short MaterialSlot, int MaterialItemId, byte SelectedOption)
    : IEffectSettlementRequest
{
    public ReplayProjectionNeeds MinimumReplayNeeds => ReplayProjectionNeeds.Inventory;
    public ClientMessageType MessageType => Purify
        ? ClientMessageType.GamePurifyItem : ClientMessageType.GameInvestAmplifyOption;
}

public sealed record PurifyItemResponse(bool Success,
    short MaterialSlot = -1, int MaterialRemaining = 0,
    short TargetSlot = -1, byte AmplifyType = 0, ushort AmplifyValue = 0)
    : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GamePurifyItem;
}

public sealed record InvestAmplifyOptionResponse(bool Success,
    byte WireAction = 0, short MaterialSlot = -1, int MaterialRemaining = 0,
    short TargetSlot = -1, byte AmplifyType = 0, ushort AmplifyValue = 0,
    byte ErrorCode = 0) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameInvestAmplifyOption;
}

internal sealed class PurifyItemDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        if (body.Length != 12)
            throw new InvalidDataException("PURIFY_ITEM expects twelve A21 bytes.");
        return new OutworldRequest(metadata, true, 0,
            BinaryPrimitives.ReadInt16LittleEndian(body),
            BinaryPrimitives.ReadInt32LittleEndian(body[2..]),
            BinaryPrimitives.ReadInt16LittleEndian(body[6..]),
            BinaryPrimitives.ReadInt32LittleEndian(body[8..]), 0);
    }
}

internal sealed class InvestAmplifyOptionDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        if (body.Length < 14 || body[0] is not 2 && body.Length != 14)
            throw new InvalidDataException("INVEST_ITEM_AMPLIFY_OPTION needs 14 fixed A21 bytes.");
        return new OutworldRequest(metadata, false, body[0],
            BinaryPrimitives.ReadInt16LittleEndian(body[1..]),
            BinaryPrimitives.ReadInt32LittleEndian(body[3..]),
            BinaryPrimitives.ReadInt16LittleEndian(body[7..]),
            BinaryPrimitives.ReadInt32LittleEndian(body[9..]), body[13]);
    }
}

internal sealed class PurifyItemEncoder : GameCmdMessageEncoder<PurifyItemResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(PurifyItemResponse message) =>
        OnePayload(message, EncodeBody(message));

    internal static byte[] EncodeBody(PurifyItemResponse message)
    {
        if (!message.Success) return [0, 0, 0, 0];
        var body = new byte[12];
        body[0] = 1;
        BinaryPrimitives.WriteInt16LittleEndian(body.AsSpan(1), message.MaterialSlot);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(3), message.MaterialRemaining);
        BinaryPrimitives.WriteInt16LittleEndian(body.AsSpan(7), message.TargetSlot);
        body[9] = message.AmplifyType;
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(10), message.AmplifyValue);
        return body;
    }
}

internal sealed class InvestAmplifyOptionEncoder
    : GameCmdMessageEncoder<InvestAmplifyOptionResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(
        InvestAmplifyOptionResponse message) =>
        OnePayload(message, EncodeBody(message));

    internal static byte[] EncodeBody(InvestAmplifyOptionResponse message)
    {
        if (!message.Success) return [0, message.ErrorCode];
        var body = new byte[13];
        body[0] = 1;
        body[1] = message.WireAction;
        BinaryPrimitives.WriteInt16LittleEndian(body.AsSpan(2), message.MaterialSlot);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(4), message.MaterialRemaining);
        BinaryPrimitives.WriteInt16LittleEndian(body.AsSpan(8), message.TargetSlot);
        body[10] = message.AmplifyType;
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(11), message.AmplifyValue);
        return body;
    }
}
