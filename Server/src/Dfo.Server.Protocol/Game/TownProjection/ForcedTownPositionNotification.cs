using System.Buffers.Binary;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.TownProjection;

/// <summary>A21 NOTI 008A sets actor coordinates, including the locally controlled actor.</summary>
public sealed record ForcedTownPositionNotification(ushort UserId, short X, short Y, byte Direction) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameForcedTownPosition;
}

internal sealed class ForcedTownPositionEncoder : GameNotiMessageEncoder<ForcedTownPositionNotification>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(ForcedTownPositionNotification message)
    {
        var body = new byte[7];
        BinaryPrimitives.WriteUInt16LittleEndian(body, message.UserId);
        BinaryPrimitives.WriteInt16LittleEndian(body.AsSpan(2), message.X);
        BinaryPrimitives.WriteInt16LittleEndian(body.AsSpan(4), message.Y);
        body[6] = message.Direction;
        return OnePayload(message, body);
    }
}
