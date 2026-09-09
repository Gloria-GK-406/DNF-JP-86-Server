using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.Characters;
using Microsoft.Extensions.Logging;

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
    ICreatureEquipmentLifetimeSource creatureLifetimes,
    ILogger<CharacterEntryPreparationRuleService>? logger = null)
    : ICharacterEntryPreparationRuleService
{
    public CharacterSkillProtocolMirrorState RemainingPoints(
        SkillPointDefinitionSet definitions,
        byte characterLevel,
        IReadOnlyList<CharacterSkillPageState> pages,
        CharacterSkillPointBasisState basis)
    {
        var ledger = SkillPointLedgerRules.Calculate(definitions, characterLevel, pages, basis);
        if (!ledger.IsComplete)
            logger?.LogWarning("Skill point ledger is incomplete: missing definitions for skill ids {SkillIds}",
                string.Join(',', ledger.MissingSkillIds));
        return ledger.ToProtocolMirror();
    }

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
