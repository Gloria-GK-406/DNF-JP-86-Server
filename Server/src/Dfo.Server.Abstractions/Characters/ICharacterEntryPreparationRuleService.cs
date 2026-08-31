using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Abstractions.Characters;

/// <summary>
/// What the archive declares about a character's skill ledger and its creature's remaining life.
/// </summary>
/// <remarks>
/// Three declarations that entry has to reconcile against before a character can be published: what
/// its job and level are entitled to, which premium terms carry an over-skill contract, and how much
/// life an equipped creature has left at this moment. Each is content plus what it is handed; none
/// of them looks a character up.
/// </remarks>
public interface ICharacterEntryPreparationRuleService
{
    /// <summary>What this character's skill-point ledger is reconciled against.</summary>
    ValueTask<SkillPointDefinitionSet> SkillPointsAsync(
        byte job,
        byte packedGrowType,
        byte characterLevel,
        IReadOnlyCollection<int> learnedSkillIds,
        CancellationToken cancellationToken);

    /// <summary>Which premium terms carry an over-skill contract.</summary>
    IReadOnlySet<int> OverSkillPremiumTypes();

    /// <summary>How much life one equipped creature item has left at this moment.</summary>
    ValueTask<CreatureEquipmentLifetime> CreatureLifetimeAsync(
        int itemTemplateId,
        DateTimeOffset preparedAt,
        CancellationToken cancellationToken);
}
