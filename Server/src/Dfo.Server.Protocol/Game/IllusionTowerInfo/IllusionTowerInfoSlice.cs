using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.IllusionTowerInfo;

internal sealed record IllusionTowerInfoNotification(int DungeonId, int TotalLevels) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameIllusionTowerInfo;
}

internal static class IllusionTowerInfoPayloadCodec
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

internal sealed class IllusionTowerInfoEncoder : GameNotiMessageEncoder<IllusionTowerInfoNotification>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(IllusionTowerInfoNotification message) =>
        OnePayload(GameProtocol.IllusionTowerInfoType,
            IllusionTowerInfoPayloadCodec.Encode(message.DungeonId, message.TotalLevels));
}
