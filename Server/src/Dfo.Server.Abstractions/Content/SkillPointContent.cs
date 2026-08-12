namespace Dfo.Server.Abstractions.Content;

public sealed record SkillPointGrantDefinition(int SkillId, byte Level);

public sealed record SkillPointCostDefinition(
    int SkillId,
    bool UsesTp,
    IReadOnlyList<int> Costs,
    bool IsFixedLevel,
    int RequiredLevel,
    int MaximumLevel,
    int FixedLevelBase,
    int FixedLevelInterval,
    int FixedLevelAddPerInterval);

public sealed record SkillPointDefinitionSet(
    int TotalSp,
    int TotalTp,
    IReadOnlyList<SkillPointGrantDefinition> FreeGrants,
    IReadOnlyDictionary<int, SkillPointCostDefinition> Skills);

/// <summary>PVF-owned ingredients required by the pure character skill-point ledger.</summary>
public interface ISkillPointDefinitionSource
{
    ValueTask<SkillPointDefinitionSet> GetAsync(
        byte job,
        byte packedGrowType,
        byte characterLevel,
        IReadOnlyCollection<int> learnedSkillIds,
        CancellationToken cancellationToken);
}
