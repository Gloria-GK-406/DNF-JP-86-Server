using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Application.World;

internal static class LostLandFeverRules
{
    public static LostLandFeverState Award(LostLandFeverState current, DungeonCellActorState actor,
        DateTimeOffset at, TimeSpan duration)
    {
        if (!actor.IsMonster || actor.IsAiCharacter || !LostLandEnemyRules.IsEnemy(actor.Actor)
            || current.ActiveUntil > at) return current;
        // Server policy approved for this feature; these weights are not reconstructed original rules.
        var points = (MonsterRank)actor.Actor.ActorType switch
        {
            MonsterRank.Ordinary => 1,
            MonsterRank.Champion => 3,
            MonsterRank.SuperChampion => 5,
            MonsterRank.Boss => 10,
            _ => 0,
        };
        if (points == 0) return current;
        var percent = (current.ActiveUntil is null ? current.Percent : 0) + points;
        return percent >= 100 ? new(100, at + duration) : new(checked((byte)percent));
    }
}
