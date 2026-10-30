using System.Buffers.Binary;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.DungeonEntry;

namespace Dfo.Server.Protocol.Game.MirrorDungeonEntry;

internal sealed class MirrorDungeonEntryDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        var dungeon = body.Length == 8 ? BinaryPrimitives.ReadUInt32LittleEndian(body) : 0;
        var quest = body.Length == 8 ? BinaryPrimitives.ReadUInt32LittleEndian(body[4..]) : 0;
        return new SelectDungeonRequest(metadata, dungeon <= int.MaxValue ? (int)dungeon : 0,
            DungeonDifficulty.Normal, 0, 0, quest <= ushort.MaxValue ? (int)quest : 0);
    }
}

internal sealed record MirrorDungeonMissionNotification(int QuestId) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameMirrorDungeonMission;
}

internal sealed class MirrorDungeonMissionEncoder : GameNotiMessageEncoder<MirrorDungeonMissionNotification>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(MirrorDungeonMissionNotification message)
    {
        var body = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(body, checked((uint)message.QuestId));
        return OnePayload(message, body);
    }
}
