namespace Dfo.Server.Abstractions.Content;

public sealed record SkillPointGrantDefinition(int SkillId, byte Level, bool IsInitial = false);

public sealed record SkillPointCostDefinition(
    int SkillId,
    bool UsesTp,
    IReadOnlyList<int> Costs,
    bool IsFixedLevel,
    int RequiredLevel,
    int MaximumLevel,
    int FixedLevelBase,
    int FixedLevelInterval,
    int FixedLevelAddPerInterval,
    bool IsActive = false,
    int RawGroup = 0,
    int NumberOfGrowTypes = 0,
    // null means this advancement cannot learn the skill. Zero is a real, applicable
    // level-derived cap and therefore removes an expired-contract-only learned row.
    int? OrdinaryMaximumLevel = null);

public interface IExpertSkillContractDefinitionSource
{
    IReadOnlySet<int> GetOverSkillPremiumTypes();
}

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
