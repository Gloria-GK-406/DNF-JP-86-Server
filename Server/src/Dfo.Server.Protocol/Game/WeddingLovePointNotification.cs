using System.Buffers.Binary;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game;

internal sealed record WeddingLovePointNotification(int Character, int Points) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameWeddingLovePointNotification;
}

internal sealed class WeddingLovePointNotificationEncoder : GameNotiMessageEncoder<WeddingLovePointNotification>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(WeddingLovePointNotification message)
    {
        var body = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(body, message.Character);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(4), message.Points);
        return OnePayload(message, body);
    }
}
