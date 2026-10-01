using Dfo.Server.Abstractions.Effects;
using System.Buffers.Binary;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.MagicSeal;

public sealed record MagicSealRequest(GameMessageMetadata Metadata, bool Change,
    short TargetSlot, short MaterialSlot, byte OptionIndex = 0) : IEffectSettlementRequest
{
    public ReplayProjectionNeeds MinimumReplayNeeds => ReplayProjectionNeeds.Inventory;
    public ClientMessageType MessageType => Change
        ? ClientMessageType.GameChangeRandomOption
        : ClientMessageType.GameUnsealRandomOption;
}

public sealed record ChangeRandomOptionResponse(bool Success, byte ErrorCode = 0)
    : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameChangeRandomOption;
}

internal sealed class UnsealRandomOptionDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        if (body.Length != 4)
            throw new InvalidDataException("UNSEAL_RANDOM_OPTION expects four A21 bytes.");
        return new MagicSealRequest(metadata, false,
            BinaryPrimitives.ReadInt16LittleEndian(body),
            BinaryPrimitives.ReadInt16LittleEndian(body[2..]));
    }
}

internal sealed class ChangeRandomOptionDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        if (body.Length != 5)
            throw new InvalidDataException("CHANGE_RANDOM_OPTION expects five A21 bytes.");
        return new MagicSealRequest(metadata, true,
            BinaryPrimitives.ReadInt16LittleEndian(body),
            BinaryPrimitives.ReadInt16LittleEndian(body[2..]), body[4]);
    }
}

internal sealed class ChangeRandomOptionEncoder
    : GameCmdMessageEncoder<ChangeRandomOptionResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(
        ChangeRandomOptionResponse message) =>
        OnePayload(GameProtocol.ChangeRandomOptionType,
            EncodeBody(message));

    internal static byte[] EncodeBody(ChangeRandomOptionResponse message) =>
        message.Success ? [1] : [0, message.ErrorCode];
}
