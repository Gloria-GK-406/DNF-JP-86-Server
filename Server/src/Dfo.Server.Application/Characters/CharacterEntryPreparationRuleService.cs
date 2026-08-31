using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Application.Characters;

/// <summary>
/// The one reader of what entry reconciles a character against.
/// </summary>
/// <remarks>
/// Three archive declarations that used to be threaded through the preparation flow as three
/// separate parameters, each passed down again into whichever helper needed it. What they answer is
/// one subject -- what this character is entitled to on the way in -- so they arrive as one seam.
/// </remarks>
internal sealed class CharacterEntryPreparationRuleService(
    ISkillPointDefinitionSource skillPoints,
    IExpertSkillContractDefinitionSource expertContracts,
    ICreatureEquipmentLifetimeSource creatureLifetimes)
    : ICharacterEntryPreparationRuleService
{
    public ValueTask<SkillPointDefinitionSet> SkillPointsAsync(
        byte job,
        byte packedGrowType,
        byte characterLevel,
        IReadOnlyCollection<int> learnedSkillIds,
        CancellationToken cancellationToken) =>
        skillPoints.GetAsync(job, packedGrowType, characterLevel, learnedSkillIds, cancellationToken);

    public IReadOnlySet<int> OverSkillPremiumTypes() => expertContracts.GetOverSkillPremiumTypes();

    public ValueTask<CreatureEquipmentLifetime> CreatureLifetimeAsync(
        int itemTemplateId,
        DateTimeOffset preparedAt,
        CancellationToken cancellationToken) =>
        creatureLifetimes.GetAsync(itemTemplateId, preparedAt, cancellationToken);
}
