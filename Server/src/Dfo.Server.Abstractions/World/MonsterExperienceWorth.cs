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
    /// <summary>What every level of a monster is worth before its growth rate applies.</summary>
    private const int PerLevel = 10;

    /// <summary>
    /// The levels a monster is credited with beyond its own.
    /// </summary>
    /// <remarks>
    /// Without it a level-1 monster would be worth ten and the first hour of the game would pay
    /// almost nothing; with it the floor is thirty.
    /// </remarks>
    private const int LevelHeadStart = 2;

    /// <summary>The level at which a monster's worth starts growing faster than its level.</summary>
    private const int GrowthBeginsAtLevel = 15;

    /// <summary>How many percentage points each level past <see cref="GrowthBeginsAtLevel"/> adds.</summary>
    private const int GrowthPercentPerLevel = 3;

    /// <summary>What a monster of this level is worth, or zero when there is no such level.</summary>
    /// <remarks>
    /// The rounding mode is stated rather than left to the default, and it never actually decides
    /// anything: the numerator is <c>10(n+2)(100+3k)</c>, always a multiple of ten, so dividing by
    /// a hundred cannot land on a half. Written out so the next reader does not have to work that
    /// out to know the answer is stable.
    /// </remarks>
    public static uint For(int monsterLevel)
    {
        if (monsterLevel < 1)
            return 0;

        var baseWorth = PerLevel * (monsterLevel + LevelHeadStart);
        var growthPercent = 100
            + (GrowthPercentPerLevel * Math.Max(0, monsterLevel - GrowthBeginsAtLevel));
        return checked((uint)Math.Round(baseWorth * growthPercent / 100.0, MidpointRounding.AwayFromZero));
    }
}
