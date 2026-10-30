using Dfo.Server.Data.World;
using Dfo.Server.Abstractions.Content;

using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Application.World;

/// <summary>70-style ordinary clear valuation using actual kills, not the quest reward table.</summary>
internal static class DungeonClearExperienceRules
{
    public static uint BaseWorth(in ClearCircumstances clear, ExperienceTuning tuning,
        ExperienceParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(tuning);
        ArgumentNullException.ThrowIfNull(parameters);
        if (clear.KilledMonsterCount <= 0 || clear.CharacterLevel <= 0) return 0;
        var count = Math.Max(1, clear.PartyMemberCount);
        var shared = Floor(MonsterExperienceWorth.For(clear.BasisLevel, tuning.Curve)
            * (double)clear.KilledMonsterCount / tuning.WorthDivisor
            * parameters.PartyMemberRate(count)
            * parameters.DifficultyRate((int)clear.Difficulty)
            * clear.DungeonWeight * parameters.ClearOverallRate);
        return Floor(shared * tuning.LevelGapRate(clear.CharacterLevel, clear.BasisLevel) / count);
    }

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
