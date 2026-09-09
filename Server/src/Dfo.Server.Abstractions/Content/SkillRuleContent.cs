using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.Content;

public sealed record SkillPrerequisite(int SkillId, int Level);

public sealed record SkillFreeGrant(int SkillId, int Level, int GrowType, int Awakening)
{
    public bool IsInitial => GrowType < 0;
}

public sealed record SkillRuleDefinition(
    int SkillId, string Name, string Description, bool UsesTp, IReadOnlyList<int> Costs,
    IReadOnlyList<int> MaximumLevels, IReadOnlyList<int> GrowTypeMaximumLevels,
    IReadOnlyList<int> AwakeningMaximumLevels, int RequiredLevel, int LevelInterval,
    IReadOnlyList<SkillPrerequisite> Prerequisites, bool IsFixedLevel,
    int FixedLevelBase, int FixedLevelInterval, int FixedLevelAddPerInterval,
    bool IsActive, int RawGroup, int NumberOfGrowTypes);

public sealed record SkillRuleCatalog(
    IReadOnlyDictionary<int, SkillRuleDefinition> Skills,
    IReadOnlyList<SkillFreeGrant> FreeGrants,
    IReadOnlyList<(int Level, int Sp)> SpTable,
    IReadOnlySet<CharacterCareer> SupportedCareers);

public interface ISkillRuleDefinitionSource
{
    SkillRuleCatalog Get(byte job);
}
