using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.Characters;

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
    public CharacterSkillProtocolMirrorState RemainingPoints(
        SkillPointDefinitionSet definitions,
        byte characterLevel,
        IReadOnlyList<CharacterSkillPageState> pages,
        CharacterSkillPointBasisState basis)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentNullException.ThrowIfNull(basis);
        var baseline = definitions.FreeGrants.GroupBy(value => value.SkillId)
            .ToDictionary(group => group.Key, group => group.Max(value => value.Level));

        ushort RemainingFor(byte pageIndex, bool tp)
        {
            var spent = 0;
            var page = pages.SingleOrDefault(value => value.PageIndex == pageIndex);
            foreach (var learned in page?.Skills ?? [])
            {
                if (!definitions.Skills.TryGetValue(checked((int)learned.SkillId), out var skill))
                    continue; // Legacy treats an absent PVF skill definition as unchargeable.
                baseline.TryGetValue(skill.SkillId, out var freeLevel);
                if (skill.IsFixedLevel && characterLevel >= skill.RequiredLevel)
                {
                    var fixedLevel = skill.FixedLevelBase
                        + (characterLevel - skill.RequiredLevel) / Math.Max(1, skill.FixedLevelInterval)
                        * skill.FixedLevelAddPerInterval;
                    freeLevel = checked((byte)Math.Min(byte.MaxValue,
                        Math.Max(freeLevel, Math.Min(fixedLevel, skill.MaximumLevel))));
                }
                if (skill.UsesTp != tp || learned.Level <= freeLevel || skill.Costs.Count == 0)
                    continue;
                for (var level = freeLevel; level < learned.Level; level++)
                    spent = checked(spent + skill.Costs[Math.Min(level, skill.Costs.Count - 1)]);
            }
            var total = (tp ? definitions.TotalTp : definitions.TotalSp)
                + (tp ? basis.BonusTp : basis.BonusSp);
            return checked((ushort)Math.Clamp(total - spent, 0, ushort.MaxValue));
        }

        return new CharacterSkillProtocolMirrorState(
            RemainingFor(0, false), RemainingFor(1, false),
            RemainingFor(0, true), RemainingFor(1, true));
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
