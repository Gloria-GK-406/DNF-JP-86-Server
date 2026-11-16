using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Application.World;

/// <summary>
/// Whether a maze's <c>[clear condition]</c> entries are met, measured against the run's own
/// committed facts rather than a second counter that could drift from them.
/// </summary>
/// <remarks>
/// 70Reference <c>CClearCondition::ClearCondition</c> @ 0x082FEFCE keeps one counter per entry and
/// clears once every entry reaches its count. The same facts are already held by the run: a
/// destroyed object is a consumed room object (claimed through the 0x0027 object branch, which
/// only accepts codes declared in the current room, as 70 <c>DestroyPassiveObject</c> checks the
/// map's <c>[passive object]</c>), a hunted monster is a committed kill, and a cleared map is a
/// cleared room. Counting them here gives the same answer and survives a re-read.
/// <para>
/// Hunt targets match by monster code. The A21 client counts its own hunt boss copy without
/// comparing codes (0x017DE621), but that copy only drives the finale cutscene; the clear is the
/// server's. Kills are monster kills only, so a hunt apc entry cannot be met -- A21 declares none.
/// </para>
/// Called only through <see cref="IDungeonSettlementRuleService"/>.
/// </remarks>
internal static class DungeonClearConditionRules
{
    public static bool AreMet(IReadOnlyList<DungeonClearCondition> conditions, DungeonClearConditionFacts facts)
    {
        ArgumentNullException.ThrowIfNull(conditions);
        ArgumentNullException.ThrowIfNull(facts);
        var counted = conditions.Where(condition => condition.IsCounted).ToArray();
        return counted.Length > 0 && counted.All(condition => Progress(condition, facts) >= condition.Count);
    }

    public static int Progress(DungeonClearCondition condition, DungeonClearConditionFacts facts) => condition.Kind switch
    {
        DungeonClearConditionKind.DestroyObject => facts.DestroyedObjectCodes.Count(condition.Targets.Contains),
        DungeonClearConditionKind.ClearMap => facts.ClearedRoomMapIds.Any(condition.Targets.Contains) ? 1 : 0,
        DungeonClearConditionKind.HuntMonster or DungeonClearConditionKind.HuntApc or DungeonClearConditionKind.HuntBoss
            => facts.KilledMonsterCodes.Count(condition.Targets.Contains),
        _ => 0,
    };
}
