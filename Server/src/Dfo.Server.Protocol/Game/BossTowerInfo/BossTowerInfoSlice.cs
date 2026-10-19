using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.BossTowerInfo;

internal sealed record BossTowerInfoNotification(int DungeonId, int TotalLevels) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameBossTowerInfo;
}

internal static class BossTowerInfoPayloadCodec
{
    public static byte[] Encode(int dungeonId, int totalLevels)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(checked((uint)dungeonId));
        writer.Write(checked((ushort)totalLevels));
        writer.Write((byte)0);
        writer.Write((byte)0);
        return stream.ToArray();
    }
}

internal sealed class BossTowerInfoEncoder : GameNotiMessageEncoder<BossTowerInfoNotification>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(BossTowerInfoNotification message) =>
        OnePayload(GameProtocol.BossTowerInfoType,
            BossTowerInfoPayloadCodec.Encode(message.DungeonId, message.TotalLevels));
}
