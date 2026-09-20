using Dfo.Server.Data.World;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Abstractions.World;

/// <summary>Everything about one finished run that decides what clearing it was worth.</summary>
/// <param name="BasisLevel">The dungeon's own level, which is what its clear is priced at.</param>
/// <param name="Difficulty">The difficulty the run was entered at.</param>
/// <param name="DungeonWeight">The dungeon's own experience weight.</param>
/// <param name="ClientRankPoint">
/// The score the client worked out for the run and reported. Taken from it rather than judged: it
/// has already drawn the letter on the screen the player is looking at.
/// </param>
public readonly record struct ClearCircumstances(
    int BasisLevel,
    DungeonDifficulty Difficulty,
    double DungeonWeight,
    byte ClientRankPoint);

/// <summary>
/// What clearing a dungeon is worth, before anything the clearing character carries.
/// </summary>
/// <remarks>
/// Two figures, and they come from three archive files between them. The clear itself is priced at
/// the dungeon's own level out of the same reward table a quest is paid from -- one table, two
/// callers, and the reason so few quests declare an experience value of their own. The rank bonus
/// is a proportion of that, chosen by where the client's own score falls in the rank table.
/// <para>
/// <b>Every number here is read, not carried.</b> The reference server's fallback for the rank
/// rates is <c>0, 0.01, 0.02, 0.03, 0.05</c>; this archive declares <c>0.05, 0.1, 0.12, 0.15,
/// 0.2</c>. Transcribing its constants would pay an order of magnitude less at every rank, and
/// nothing in a running server would say so -- the clear would simply be worth less than it should
/// be, which looks exactly like a clear.
/// </para>
/// </remarks>
public static class DungeonClearExperienceRules
{
    /// <summary>What the clear itself is worth.</summary>
    /// <remarks>
    /// The dungeon's basis level indexes the reward table; the difficulty rate, the dungeon's own
    /// weight and the world-wide clear rate scale it. The weight is not a bonus dungeons may only
    /// add -- the tutorial's is 0.1 and its clear pays a tenth, which is the intent.
    /// </remarks>
    public static uint BaseWorth(
        in ClearCircumstances clear, QuestParameters quest, ExperienceParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(quest);
        ArgumentNullException.ThrowIfNull(parameters);

        var atLevel = clear.BasisLevel >= 1 && clear.BasisLevel <= quest.ExperienceByLevel.Count
            ? quest.ExperienceByLevel[clear.BasisLevel - 1]
            : 0;
        if (atLevel <= 0)
            return 0;

        return Floor(
            atLevel
            * parameters.ClearOverallRate
            * parameters.DifficultyRate((int)clear.Difficulty)
            * clear.DungeonWeight);
    }

    /// <summary>What the run's rank adds to that.</summary>
    /// <remarks>
    /// The two archive tables pair off from their opposite ends: the rank table is written best
    /// first and the rate table best last, so the highest grade earns the highest rate. A score
    /// below every rank that carries a rate earns nothing -- not the smallest rate, which belongs
    /// to a rank this run did not reach.
    /// </remarks>
    public static uint RankBonus(
        uint clearBase,
        byte clientRankPoint,
        DungeonRankGrades grades,
        ExperienceParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(grades);
        ArgumentNullException.ThrowIfNull(parameters);
        if (clearBase == 0 || parameters.ClearRankRates.Count == 0)
            return 0;

        var band = grades.BandOf(clientRankPoint, parameters.ClearRankRates.Count);
        return band < 0 ? 0 : Floor(clearBase * parameters.ClearRankRates[band]);
    }

    private static uint Floor(double value) =>
        value <= 0 || double.IsNaN(value) ? 0
        : value >= uint.MaxValue ? uint.MaxValue
        : (uint)Math.Floor(value);
}
