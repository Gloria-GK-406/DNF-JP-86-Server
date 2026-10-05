using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.Accounts;

namespace Dfo.Server.Abstractions.World;

/// <summary>What a dungeon clear pays, and what the run's rank adds on top of it.</summary>
/// <remarks>
/// The two travel together because the second is worked out from the first: a rank adds a
/// proportion of what the clear itself paid, so a caller holding one without the other holds half
/// an answer.
/// </remarks>
public readonly record struct ClearExperienceWorth(uint Base, uint RankBonus);

/// <summary>
/// What an occasion pays in experience, what a character's own terms add to it, and how much of it
/// a level costs.
/// </summary>
/// <remarks>
/// Four archive declarations and two operator dials answer to one subject. The kill parameters and
/// the tuning decide what a monster is worth; the quest table and the rank grades decide what a
/// clear is worth; the premium rates and the channel reward decide what is added on top; and the
/// progression policy decides when the total becomes a level. None of them is a fact about a
/// character, which is why they are all here and none of them is read where a character is written.
/// <para>
/// Everything this answers is a function of what it is handed plus what the archive declares. It
/// holds no world state and reads none: a caller that knows the character's premiums passes them.
/// </para>
/// </remarks>
public interface IExperienceRuleService
{
    /// <summary>What one kill is worth before anything the character carries.</summary>
    uint WorthOfKill(in KillCircumstances kill);

    /// <summary>
    /// Whether an actor of this declared type is a monster, and therefore pays anything at all.
    /// </summary>
    bool IsMonster(byte declaredActorType);

    /// <summary>What this clear pays, and what its rank adds.</summary>
    ClearExperienceWorth WorthOfClear(in ClearCircumstances clear);

    /// <summary>
    /// What the character's own terms and the channel they are on add to this award.
    /// </summary>
    /// <param name="premiums">
    /// The account's premium terms as the published session carries them. Handed in rather than
    /// looked up: which account is playing is world state, and the rules may not go and find it.
    /// </param>
    IReadOnlyList<ExperienceModifier> ModifiersFor(
        ExperienceEvent occasion,
        IReadOnlyList<AccountPremiumEntryState> premiums,
        DateTimeOffset now,
        int channelId,
        int dungeonId);

    /// <summary>How much a level costs, and the caps a run's experience is held to.</summary>
    DungeonExperienceProgressionPolicy Progression();

    CharacterExperienceScale CharacterScale() => new(Progression().LevelThresholds);

    Dfo.Server.Abstractions.Effects.ExperienceAwardDecision DecideAward(
        Dfo.Server.Data.Characters.CharacterProgressionState before, ExperienceBreakdown breakdown) => ExperienceProgressionRules.Decide(before, breakdown, CharacterScale());
}
