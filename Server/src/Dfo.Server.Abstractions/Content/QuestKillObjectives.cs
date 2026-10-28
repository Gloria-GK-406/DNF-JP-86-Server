using Dfo.Server.Abstractions.Quests;

namespace Dfo.Server.Abstractions.Content;

public enum QuestKillParseStatus { NotApplicable, Parsed, Unsupported, Invalid }
public enum QuestKillScopeKind { Dungeon, AnyDungeon, RecommendedDungeon, WorldMap }
public enum QuestKillTargetKind { Exact, OrdinaryMonster, ChampionMonster, BossMonster, Unresolved }
public enum QuestKillDifficultyMatch { Minimum, Exact }

public sealed record QuestKillScope(QuestKillScopeKind Kind, int Id = 0);

/// <summary>Negative selectors without verified semantics remain explicit, never treated as any actor.</summary>
public sealed record QuestKillTarget(QuestKillTargetKind Kind, int Code, int EnemyType);

/// <summary>A normalized target; its channel is independent of its position in a consumer's list.</summary>
public sealed record QuestKillObjective(
    QuestKillScope Scope,
    int Difficulty,
    QuestKillDifficultyMatch DifficultyMatch,
    QuestKillTarget Target,
    int RequiredCount,
    int ChannelIndex);

/// <summary>One parse result, shared by acceptance and progress. Operands preserve unsupported declarations.</summary>
public sealed record QuestKillObjectiveDefinition(
    QuestKillParseStatus Status,
    IReadOnlyList<QuestKillObjective> Targets,
    IReadOnlyList<int> Operands,
    string? Diagnostic = null)
{
    public static QuestKillObjectiveDefinition NotApplicable { get; } =
        new(QuestKillParseStatus.NotApplicable, [], []);

    public uint? InitialTrigger => Status is QuestKillParseStatus.Parsed or QuestKillParseStatus.Unsupported
        ? QuestTrigger.FromRemainingCounts(Targets.Select(target => (target.ChannelIndex, target.RequiredCount))).PackedValue
        : null;
}
