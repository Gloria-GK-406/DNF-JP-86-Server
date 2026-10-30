using System.Buffers.Binary;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.Dungeons;
using Dfo.Server.Protocol.Game;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.CharacterDeath;

public sealed record CharacterDeathRequest(
    GameMessageMetadata Metadata, ushort First, ushort Second, ushort? Third) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameCharacterDeath;
}

internal sealed class CharacterDeathDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        // A21 has two native builders: two words, or three words. Field meanings remain unproven.
        if (body.Length is not (4 or 6))
            throw new InvalidDataException($"DIE_CHARACTER requires 4 or 6 bytes; received {body.Length}.");
        return new CharacterDeathRequest(metadata,
            BinaryPrimitives.ReadUInt16LittleEndian(body),
            BinaryPrimitives.ReadUInt16LittleEndian(body[2..]),
            body.Length == 6 ? BinaryPrimitives.ReadUInt16LittleEndian(body[4..]) : null);
    }
}

internal sealed record CharacterDeathResponse : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameCharacterDeath;
}

internal sealed class CharacterDeathResponseEncoder : GameCmdMessageEncoder<CharacterDeathResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(CharacterDeathResponse message)
        => OnePayload(message, new byte[] { 0x01 });
}

internal sealed record CharacterLifeStateNotification(ushort ActorId, byte State = 0) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameCharacterLifeState;
}

internal sealed class CharacterLifeStateEncoder : GameNotiMessageEncoder<CharacterLifeStateNotification>
{
    internal static byte[] EncodeDeath(ushort actorId)
    {
        byte[] body = new byte[4];
        BinaryPrimitives.WriteUInt16LittleEndian(body, actorId);
        // A21 NOTI 0020: actor, state 0 (dead), trailing byte 0. No countdown duration field.
        return body;
    }

    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(CharacterLifeStateNotification message)
    {
        var body = EncodeDeath(message.ActorId);
        body[2] = message.State;
        return OnePayload(message, body);
    }
}

public sealed class CharacterDeathHandler(ICommandBus commands)
    : ClientMessageHandler<CharacterDeathRequest>, Dfo.Server.Application.Dispatching.IProtocolCommandMessageHandler<GameCharacterDeathCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, CharacterDeathRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameCharacterDeathCommand(context, message), ct).ConfigureAwait(false);
}
