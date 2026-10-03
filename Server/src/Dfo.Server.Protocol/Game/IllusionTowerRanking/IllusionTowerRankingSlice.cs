using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.IllusionTowerRanking;

internal sealed record IllusionTowerRankingNotification(
    int DungeonId, int CompletedLevels, int ElapsedMilliseconds) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameIllusionTowerRanking;
}

internal static class IllusionTowerRankingPayloadCodec
{
    public static byte[] Encode(int dungeonId, int completedLevels, int elapsedMilliseconds)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)0);
        writer.Write(Math.Max(0, elapsedMilliseconds));
        writer.Write(Math.Max(0, completedLevels));
        writer.Write((byte)0);
        writer.Write(checked((uint)dungeonId));
        writer.Write((byte)0);
        for (var group = 0; group < 5; group++)
        {
            for (var row = 0; row < 8; row++)
            {
                writer.Write(0u);
                writer.Write((byte)0);
                writer.Write((byte)0);
            }
            writer.Write((ushort)0);
            writer.Write(0u);
            writer.Write(0u);
        }
        var body = stream.ToArray();
        if (body.Length != 305)
            throw new InvalidOperationException($"Tower ranking baseline must be 305 bytes, got {body.Length}.");
        return body;
    }
}

internal sealed class IllusionTowerRankingEncoder : GameNotiMessageEncoder<IllusionTowerRankingNotification>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(IllusionTowerRankingNotification message) =>
        OnePayload(GameProtocol.IllusionTowerRankingType,
            IllusionTowerRankingPayloadCodec.Encode(
                message.DungeonId, message.CompletedLevels, message.ElapsedMilliseconds));
}
