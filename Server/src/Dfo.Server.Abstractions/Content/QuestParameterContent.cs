namespace Dfo.Server.Abstractions.Content;

/// <summary>
/// The archive's shared quest-reward parameters: what a quest of a given level is worth, and how
/// that is bent by the quest's difficulty letter and by the gap to the character's own level.
/// </summary>
/// <remarks>
/// One table for every quest in the archive. A quest file declares which row it wants -- through
/// its level band, its difficulty letter and its grade -- but not the number; the number is here.
/// That is why so few quests declare an experience value of their own: almost none do.
/// </remarks>
/// <param name="ExperienceByLevel">
/// What a quest whose reward level is <c>n</c> pays before difficulty and level penalty, indexed
/// from level 1. The archive interleaves a <c>-1</c> after every value; those are not levels and
/// are dropped, which is what makes entry <c>n-1</c> level <c>n</c>'s.
/// </param>
/// <param name="DifficultyPermille">
/// The bonus each difficulty letter adds, in parts per thousand: <c>M</c> is 10, meaning +1%.
/// A letter the table does not list is worth <see cref="DefaultDifficultyPermille"/>.
/// </param>
/// <param name="GreenPenaltyPercent">
/// What a quest pays when the character has outgrown it by seven to eleven levels.
/// </param>
/// <param name="GreyPenaltyPercent">And by more than eleven.</param>
/// <param name="EpicGreenPenaltyPercent">
/// The same two bands for an epic quest, which are the archive's own separate numbers and are
/// larger rather than smaller -- an epic quest pays MORE once outgrown, not less.
/// </param>
public sealed record QuestParameters(
    IReadOnlyList<int> ExperienceByLevel,
    IReadOnlyDictionary<char, int> DifficultyPermille,
    int GreenPenaltyPercent,
    int GreyPenaltyPercent,
    int EpicGreenPenaltyPercent,
    int EpicGreyPenaltyPercent)
{
    /// <summary>What an unlisted difficulty letter is worth.</summary>
    public const int DefaultDifficultyPermille = 10;

    /// <summary>The grade the archive gives its own penalty pair to.</summary>
    public const string EpicGrade = "epic";

    /// <summary>
    /// What finishing one quest pays.
    /// </summary>
    /// <param name="characterLevel">The level of the character finishing it.</param>
    /// <param name="rewardLevel">
    /// The level the quest is paid at, which is the bottom of its own level band.
    /// </param>
    /// <param name="difficulty">The quest's difficulty letter.</param>
    /// <param name="isEpic">Whether the quest's grade takes the epic penalty pair.</param>
    /// <param name="ignoreLevel">
    /// When set, the quest pays at the character's level instead of its own and takes no penalty.
    /// The archive spells this <c>[ignore quest level 4 exp]</c>.
    /// </param>
    /// <remarks>
    /// Confirmed end to end against the reference server on 2026-08-27: quest 1016 at reward level
    /// 1, difficulty <c>M</c>, epic, on a character within the band, pays <c>100 x 1010/1000 x
    /// 100/100 = 101</c>, and the reference server's answer carries <c>65 00 00 00</c>.
    ///
    /// Integer division at both steps and in that order, which is the reference server's own
    /// arithmetic. It matters: rounding once at the end instead gives a different number wherever
    /// the intermediate is not a whole value.
    /// </remarks>
    public uint ExperienceFor(
        int characterLevel, int rewardLevel, char difficulty, bool isEpic, bool ignoreLevel)
    {
        var lookupLevel = ignoreLevel ? characterLevel : rewardLevel;
        var baseExperience = lookupLevel >= 1 && lookupLevel <= this.ExperienceByLevel.Count
            ? this.ExperienceByLevel[lookupLevel - 1]
            : 0;
        if (baseExperience <= 0)
            return 0;

        var permille = this.DifficultyPermille.TryGetValue(char.ToUpperInvariant(difficulty), out var found)
            ? found
            : DefaultDifficultyPermille;
        var penalty = ignoreLevel
            ? 100
            : this.PenaltyPercentFor(characterLevel - rewardLevel, isEpic);
        var weighted = (long)baseExperience * (1000L + permille) / 1000L;
        return checked((uint)Math.Max(0L, weighted * penalty / 100L));
    }

    /// <summary>
    /// What a character this far past a quest's level is paid, as a percentage.
    /// </summary>
    /// <remarks>
    /// Only outgrowing a quest is penalised. A character below the quest's level is paid in full --
    /// there is no band for it here, because a quest below the character's level band is one the
    /// offer rules never showed them.
    /// </remarks>
    public int PenaltyPercentFor(int levelsAboveQuest, bool isEpic) => levelsAboveQuest switch
    {
        > 11 => isEpic ? this.EpicGreyPenaltyPercent : this.GreyPenaltyPercent,
        > 6 => isEpic ? this.EpicGreenPenaltyPercent : this.GreenPenaltyPercent,
        _ => 100,
    };
}

/// <summary>The archive's shared quest-reward parameters.</summary>
public interface IQuestParameterSource
{
    QuestParameters Load();
}
