using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Infrastructure.Content;

/// <summary>Recognizes complete numeric grammars. Length alone cannot distinguish the two five-field families.</summary>
internal static class QuestKillObjectiveParser
{
    public static QuestKillObjectiveDefinition Parse(string objectiveType, IReadOnlyList<int> operands)
    {
        ArgumentNullException.ThrowIfNull(operands);
        var type = objectiveType.Trim().Trim('[', ']').ToLowerInvariant();
        if (type is not ("hunt monster" or "hunt enemy"))
            return QuestKillObjectiveDefinition.NotApplicable;

        var raw = Array.AsReadOnly(operands.ToArray());
        if (raw.Count == 0)
            return Invalid(raw, "Missing kill target operands.");
        var regional = type == "hunt monster" && raw[0] == -2;
        var enemy = type == "hunt enemy";
        var width = regional || enemy ? 5 : 4;
        if (raw.Count % width != 0 || raw.Count / width > 3)
            return Invalid(raw, $"Expected one to three complete {width}-field targets.");

        var targets = new List<QuestKillObjective>();
        var unsupported = false;
        for (var offset = 0; offset < raw.Count; offset += width)
        {
            QuestKillScope scope;
            if (regional)
            {
                if (raw[offset] != -2 || raw[offset + 1] <= 0)
                    return Invalid(raw, "Regional targets require -2 and a positive world map ID.");
                scope = new(QuestKillScopeKind.WorldMap, raw[offset + 1]);
            }
            else
            {
                QuestKillScope? dungeonScope = raw[offset] switch
                {
                    > 0 => new(QuestKillScopeKind.Dungeon, raw[offset]),
                    -1 => new(QuestKillScopeKind.AnyDungeon),
                    -3 => new(QuestKillScopeKind.RecommendedDungeon),
                    _ => null,
                };
                if (dungeonScope is null)
                    return Invalid(raw, "Unknown dungeon scope marker.");
                scope = dungeonScope;
            }

            var difficulty = raw[offset + (regional ? 2 : 1)];
            var code = raw[offset + (regional ? 3 : 2)];
            var enemyType = enemy ? raw[offset + 3] : HuntEnemyTargetTypes.Monster;
            var count = raw[offset + width - 1];
            if (difficulty < -1 || count is <= 0 or > 511)
                return Invalid(raw, "Difficulty must be >= -1 and count must fit a nonzero nine-bit channel.");

            var kind = code switch
            {
                > 0 => QuestKillTargetKind.Exact,
                -1 when enemyType == HuntEnemyTargetTypes.Monster => QuestKillTargetKind.OrdinaryMonster,
                -2 when enemyType == HuntEnemyTargetTypes.Monster => QuestKillTargetKind.ChampionMonster,
                -3 when enemyType == HuntEnemyTargetTypes.Monster => QuestKillTargetKind.BossMonster,
                _ => QuestKillTargetKind.Unresolved,
            };
            if (code == 0)
                return Invalid(raw, "Zero is not a kill target.");
            unsupported |= kind == QuestKillTargetKind.Unresolved
                || enemyType is not (HuntEnemyTargetTypes.Monster or HuntEnemyTargetTypes.AiCharacter
                    or HuntEnemyTargetTypes.PassiveObject);
            targets.Add(new(scope, difficulty,
                enemy ? QuestKillDifficultyMatch.Exact : QuestKillDifficultyMatch.Minimum,
                new(kind, code, enemyType), count, offset / width));
        }

        return new(unsupported ? QuestKillParseStatus.Unsupported : QuestKillParseStatus.Parsed,
            Array.AsReadOnly(targets.ToArray()), raw,
            unsupported ? "Target selector or actor family has no verified matching rule." : null);
    }

    private static QuestKillObjectiveDefinition Invalid(IReadOnlyList<int> operands, string diagnostic) =>
        new(QuestKillParseStatus.Invalid, [], operands, diagnostic);
}
