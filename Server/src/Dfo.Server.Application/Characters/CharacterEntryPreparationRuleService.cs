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
    ILogger<CharacterEntryPreparationRuleService> logger)
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
            logger.LogWarning("Skill point ledger is incomplete: missing definitions for skill ids {SkillIds}",
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

    public async ValueTask<IReadOnlyList<CharacterSkillPageState>> InitialDuelSkillPagesAsync(
        byte job, byte packedGrowType, byte characterLevel, CancellationToken cancellationToken)
    {
        var definitions = await SkillPointsAsync(job, packedGrowType, characterLevel, [], cancellationToken)
            .ConfigureAwait(false);
        var grants = definitions.FreeGrants.GroupBy(grant => grant.SkillId)
            .Select(group => group.OrderByDescending(grant => grant.Level).First())
            .OrderByDescending(grant => grant.IsInitial);
        var occupied = new HashSet<int>();
        var skills = new List<CharacterSkillState>();
        foreach (var grant in grants)
        {
            definitions.Skills.TryGetValue(grant.SkillId, out var definition);
            var level = definition is null ? grant.Level : checked((byte)Math.Clamp(
                SkillPointLedgerRules.FreeLevel(definition, grant.Level, characterLevel), 0, byte.MaxValue));
            var slot = CharacterSkillRule.FindDuelSeedSlot(occupied, job, definition, grant.IsInitial)
                ?? throw new InvalidDataException("PvP initial skill profile has no free slot.");
            occupied.Add(slot);
            skills.Add(new CharacterSkillState(slot, checked((ushort)grant.SkillId), level, []));
        }
        return [new CharacterSkillPageState(0, skills), new CharacterSkillPageState(1, skills)];
    }

    public IReadOnlySet<int> OverSkillPremiumTypes() => expertContracts.GetOverSkillPremiumTypes();

    public ValueTask<CreatureEquipmentLifetime> CreatureLifetimeAsync(
        int itemTemplateId,
        DateTimeOffset preparedAt,
        CancellationToken cancellationToken) =>
        creatureLifetimes.GetAsync(itemTemplateId, preparedAt, cancellationToken);
}
