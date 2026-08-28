namespace Dfo.Server.Abstractions.World;

/// <summary>
/// What killing a monster of a given level is worth, before anything about the run or the
/// character is applied to it.
/// </summary>
/// <remarks>
/// The base of the kill-experience calculation and the only part of it that depends on nothing but
/// the monster's level. What a run does to this number -- its difficulty, the dungeon's own
/// weight, the monster's rank, the gap to the character's level -- belongs to the rule that scales
/// it, and what a player carries on top belongs after that again.
/// <para>
/// <b>A formula, recovered from a table.</b> The reference server carries a hundred hard-coded
/// numbers here. Ninety-nine of them are reproduced exactly by the expression below, and the
/// hundredth -- level 100, its table says 3575 where this says 3621 -- is a hand edit at the level
/// ceiling that this deliberately does not follow. That is not a small distinction: a hundred
/// numbers compressing into three parameters cannot be coincidence, so the arithmetic here rests
/// on its own structure rather than on the reference server's word, and the reference server is
/// not an authority. A one-cell exception with no source behind it does not earn a branch.
/// </para>
/// The two pieces the table looks like -- flat growth to level 15, then a curve -- are one
/// expression. The curve is not a second rule: it is the same linear base under a percentage that
/// stops being 100. Where they meet is exact rather than fitted, because
/// <c>100 + 3(n - 15)</c> is 100 precisely at 15.
/// </remarks>
public static class MonsterExperienceWorth
{
    /// <summary>What a monster of this level is worth, or zero when there is no such level.</summary>
    /// <param name="monsterLevel">The monster's level as the room declared it.</param>
    /// <param name="curve">
    /// The four numbers the shape is drawn with. They are configuration rather than constants
    /// because how fast experience grows is balance: the shape came out of a hundred-entry table,
    /// the values did not come from anywhere that can be checked.
    /// </param>
    /// <remarks>
    /// The rounding mode is stated rather than left to the default. With the recovered curve it
    /// never decides anything -- the numerator is <c>10(n+2)(100+3k)</c>, always a multiple of ten,
    /// so dividing by a hundred cannot land on a half -- but a configured curve has no such
    /// guarantee, so the tie-break is named rather than inherited.
    /// </remarks>
    public static uint For(int monsterLevel, MonsterWorthCurve curve)
    {
        ArgumentNullException.ThrowIfNull(curve);
        if (monsterLevel < 1)
            return 0;

        var baseWorth = curve.PerLevel * (monsterLevel + curve.LevelHeadStart);
        var growthPercent = 100
            + (curve.GrowthPercentPerLevel
                * Math.Max(0, monsterLevel - curve.GrowthBeginsAtLevel));
        if (baseWorth <= 0 || growthPercent <= 0)
            return 0;

        var worth = Math.Round(
            (double)baseWorth * growthPercent / 100.0, MidpointRounding.AwayFromZero);
        return worth >= uint.MaxValue ? uint.MaxValue : (uint)worth;
    }
}
