using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.BossTowerMap;

internal sealed record BossTowerMapNotification(TowerRoomSnapshot Room) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameBossTowerMap;
}

internal static class BossTowerMapPayloadCodec
{
    public static byte[] Encode(TowerRoomSnapshot room)
    {
        if (room.Actors.Length > byte.MaxValue)
            throw new InvalidDataException("Tower actor count cannot be represented as u8.");
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(checked((ushort)room.Level));
        writer.Write(room.Seed);
        writer.Write(checked((uint)room.MapId));
        writer.Write(checked((byte)room.Actors.Length));
        foreach (var actor in room.Actors)
        {
            writer.Write(checked((uint)actor.Seed.ListIndex));
            writer.Write(actor.Uid);
            writer.Write(checked((uint)actor.Seed.TemplateId));
            writer.Write(actor.Seed.Level);
            writer.Write(actor.Seed.ActorType);
            writer.Write((byte)0);
            writer.Write((byte)0);
        }
        // Current tower maps declare no preplaced [item] rows. Actor drops are announced on death.
        writer.Write((byte)0);
        return stream.ToArray();
    }
}

internal sealed class BossTowerMapEncoder : GameNotiMessageEncoder<BossTowerMapNotification>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(BossTowerMapNotification message) =>
        OnePayload(message, BossTowerMapPayloadCodec.Encode(message.Room));
}
