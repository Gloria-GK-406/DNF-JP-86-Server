using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Abstractions.World;

/// <summary>What a dungeon clear pays, and what the run's rank adds on top of it.</summary>
/// <remarks>
/// The two travel together because the second is worked out from the first: a rank adds a
/// proportion of what the clear itself paid, so a caller holding one without the other holds half
/// an answer.
/// </remarks>
public readonly record struct ClearExperienceWorth(uint Base, uint RankBonus);

/// <summary>Values base experience and translates awards into character progression.</summary>
public interface IExperienceRuleService
{
    /// <summary>What a monster of this level is worth on the configured curve, before anything about the run.</summary>
    uint MobReward(int level);

    /// <summary>What one kill is worth before anything the character carries.</summary>
    uint WorthOfKill(in KillCircumstances kill);

    /// <summary>
    /// Whether an actor of this declared type is a monster, and therefore pays anything at all.
    /// </summary>
    bool IsMonster(byte declaredActorType);

    /// <summary>What this clear pays, and what its rank adds.</summary>
    ClearExperienceWorth WorthOfClear(in ClearCircumstances clear);

    /// <summary>How much a level costs, and the caps a run's experience is held to.</summary>
    DungeonExperienceProgressionPolicy Progression();

    /// <summary>The level thresholds of <see cref="Progression"/>, as a character's experience scale.</summary>
    CharacterExperienceScale CharacterScale();

    /// <summary>What this breakdown does to a character at <paramref name="before"/> on this scale.</summary>
    Dfo.Server.Abstractions.Effects.ExperienceAwardDecision DecideAward(
        Dfo.Server.Data.Characters.CharacterProgressionState before, ExperienceBreakdown breakdown);
}
