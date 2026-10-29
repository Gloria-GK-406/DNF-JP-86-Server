using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.DungeonEntry;
using Dfo.Server.Protocol.Game.DungeonClear;

namespace Dfo.Server.Protocol.Game.Dungeons;

internal static class DungeonRoomTransitionProjection
{
    public static IGameNotiMessage CompleteLostLand() => new EnableClearDungeonNotification();
    public static IGameNotiMessage StartMap(DungeonRoomVisit visit) => new StartMapNotification(
        visit.Run, visit.FirstEntry, visit.Actors, visit.FirstActorSequence,
        FloorPieceValues.Describe(visit.Run, visit.FloorDrops));
}
