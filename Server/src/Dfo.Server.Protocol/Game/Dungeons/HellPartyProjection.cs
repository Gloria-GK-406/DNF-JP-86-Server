using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;

namespace Dfo.Server.Protocol.Game.Dungeons;

/// <summary>APC resource index/level table consumed after the room's hidden actor templates.</summary>
internal sealed record HellPartyMonsterInfoNotification(IReadOnlyList<(int Index, int Level)> Actors) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameHellPartyMonsterInfo;
}

internal static class HellPartyProjection
{
    public static HellPartyMonsterInfoNotification? For(DungeonRunState run) =>
        run.Layout.Mode is DungeonRunMode.Hell { Plan: var hell } && run.CurrentCell == hell.Cell
            ? new(hell.Actors.Where(x => x.Actor.ActorType >= 5).GroupBy(x => x.Actor.Code)
                .Select(x => (x.Key, (int)x.First().Actor.Level)).ToArray()) : null;
}

internal sealed class HellPartyMonsterInfoEncoder : GameNotiMessageEncoder<HellPartyMonsterInfoNotification>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(HellPartyMonsterInfoNotification message)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(message.Actors.Count);
        foreach (var (index, level) in message.Actors) { writer.Write(index); writer.Write(level); }
        return OnePayload(0x02A7, stream.ToArray());
    }
}
