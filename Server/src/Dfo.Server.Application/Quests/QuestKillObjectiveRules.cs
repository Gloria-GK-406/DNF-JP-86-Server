using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Application.Quests;

internal static class QuestKillObjectiveRules
{
    /// <summary>Legacy four-field parsing could persist a selector as a regional remaining count.</summary>
    public static int RepairRegionalResidual(QuestKillObjectiveDefinition definition, int trigger)
    {
        if (definition.Status != QuestKillParseStatus.Parsed || definition.Targets.Count == 0
            || definition.Targets.Any(target => target.Scope.Kind != QuestKillScopeKind.WorldMap))
            return trigger;
        var repaired = new QuestTrigger(unchecked((uint)trigger));
        foreach (var target in definition.Targets)
            if (repaired.GetChannel(target.ChannelIndex) > target.RequiredCount)
                repaired = repaired.ReplaceChannel(target.ChannelIndex, target.RequiredCount);
        return unchecked((int)repaired.PackedValue);
    }

    public static bool Matches(QuestKillObjective target, QuestEnemyDefeated death)
    {
        if (death.EnemyCode <= 0 || target.Target.EnemyType != death.EnemyType
            || target.Target.EnemyType == HuntEnemyTargetTypes.PassiveObject
            || !MatchesDifficulty(target, (int)death.Difficulty))
            return false;

        var matchesScope = target.Scope.Kind switch
        {
            QuestKillScopeKind.Dungeon => target.Scope.Id == death.DungeonId,
            QuestKillScopeKind.AnyDungeon => true,
            // Missing context is not evidence that a world-map/recommended target matched.
            QuestKillScopeKind.WorldMap => death.WorldMapId == target.Scope.Id,
            QuestKillScopeKind.RecommendedDungeon => death.IsRecommendedDungeon == true,
            _ => false,
        };
        return matchesScope && target.Target.Kind switch
        {
            QuestKillTargetKind.Exact => target.Target.Code == death.EnemyCode,
            QuestKillTargetKind.OrdinaryMonster => death.Rank == MonsterRank.Ordinary,
            QuestKillTargetKind.ChampionMonster => death.Rank == MonsterRank.Champion,
            QuestKillTargetKind.BossMonster => death.Rank == MonsterRank.Boss,
            _ => false,
        };
    }

    public static bool MatchesDifficulty(QuestKillObjective target, int difficulty) =>
        target.Difficulty < 0 || (target.DifficultyMatch == QuestKillDifficultyMatch.Exact
            ? difficulty == target.Difficulty : difficulty >= target.Difficulty);

    public static IReadOnlyList<QuestKillObjective>? Select(QuestKillObjectiveDefinition definition, byte selector)
    {
        if (definition.Status != QuestKillParseStatus.Parsed || !QuestTrigger.IsReportable(selector))
            return null;
        if (selector is 0 or 1)
            return definition.Targets.Count > 0 ? definition.Targets : null;

        var selected = new List<QuestKillObjective>();
        for (var channel = 0; channel < QuestTrigger.ChannelCount; channel++)
        {
            if ((selector & (0x10 << channel)) == 0)
                continue;
            var target = definition.Targets.SingleOrDefault(target => target.ChannelIndex == channel);
            if (target is null)
                return null;
            selected.Add(target);
        }
        return selected;
    }
}
