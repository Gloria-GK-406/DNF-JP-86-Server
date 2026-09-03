using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.Characters;

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

    /// <summary>
    /// What the character has left to spend, from what the archive grants and what they have spent.
    /// </summary>
    /// <remarks>
    /// A character's remaining points are not stored anywhere: the total comes from the archive by
    /// level, the bonus from storage, and the spend from walking the learned skills against the
    /// archive's cost table. So it has to be worked out every time it is asked for, and it has to
    /// be worked out the same way every time -- which is why the arithmetic lives here, in the one
    /// place that already holds the archive's answers, rather than beside each caller.
    /// <para>
    /// Content plus the facts it is handed, and nothing else: the level, the learned pages and the
    /// stored bonus all arrive as arguments. This looks nothing up.
    /// </para>
    /// </remarks>
    CharacterSkillProtocolMirrorState RemainingPoints(
        SkillPointDefinitionSet definitions,
        byte characterLevel,
        IReadOnlyList<CharacterSkillPageState> pages,
        CharacterSkillPointBasisState basis);

    /// <summary>Which premium terms carry an over-skill contract.</summary>
    IReadOnlySet<int> OverSkillPremiumTypes();

    /// <summary>How much life one equipped creature item has left at this moment.</summary>
    ValueTask<CreatureEquipmentLifetime> CreatureLifetimeAsync(
        int itemTemplateId,
        DateTimeOffset preparedAt,
        CancellationToken cancellationToken);
}
