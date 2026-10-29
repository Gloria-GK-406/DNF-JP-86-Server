using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Application.World;

internal static class LostLandEnemyRules
{
    // Forest MAPs use 150/151 for opposing enemy tribes, independently of player allegiance.
    public static bool IsForestTribe(DungeonRoomActor actor) => actor.Declaration?.MonsterTeam is 150 or 151;

    public static bool IsEnemy(DungeonRoomActor actor) => actor.ActorType <= 3 && actor.IsHostile
        && actor.Declaration?.IsDummy != true
        && (actor.Declaration?.MonsterTeam is null or 100 || IsForestTribe(actor));
}
