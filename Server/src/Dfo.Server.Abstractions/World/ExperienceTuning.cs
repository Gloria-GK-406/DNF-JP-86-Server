namespace Dfo.Server.Abstractions.World;

/// <summary>
/// What a monster of a level is worth, as four numbers.
/// </summary>
/// <remarks>
/// Recovered from a table rather than declared anywhere: the reference server carries a hundred
/// hard-coded values and ninety-nine of them are <c>10(n+2)</c> under a percentage of
/// <c>100 + 3(n-15)</c>. A hundred numbers compressing into four parameters is not coincidence, so
/// the shape rests on its own structure. The values are another matter -- nothing outside that
/// table says the game grows at three points a level, and how fast experience grows is a balance
/// decision, not a fact about the archive. Hence a dial.
/// </remarks>
/// <param name="PerLevel">What each level of a monster is worth before growth applies.</param>
/// <param name="LevelHeadStart">
/// Levels a monster is credited with beyond its own. Without it a level-1 monster is worth ten and
/// the first hour of the game pays almost nothing.
/// </param>
/// <param name="GrowthBeginsAtLevel">Where worth starts growing faster than level.</param>
/// <param name="GrowthPercentPerLevel">Percentage points each level past that adds.</param>
public sealed record MonsterWorthCurve(
    int PerLevel,
    int LevelHeadStart,
    int GrowthBeginsAtLevel,
    int GrowthPercentPerLevel)
{
    /// <summary>The curve the reference server's hundred-entry table describes.</summary>
    public static MonsterWorthCurve Recovered { get; } = new(10, 2, 15, 3);
}

/// <summary>One band of the level-difference penalty, and what a kill inside it is worth.</summary>
/// <param name="From">Lowest difference in the band, inclusive.</param>
/// <param name="To">Highest difference in the band, inclusive.</param>
/// <param name="Rate">What a kill in this band is multiplied by.</param>
public sealed record LevelGapBand(int From, int To, double Rate);

/// <summary>
/// Everything about a kill's worth that this server decides rather than reads.
/// </summary>
/// <remarks>
/// The dividing line is where the number comes from, not how important it is. The difficulty and
/// party rates, the dungeon's own weight and the level thresholds are all declared in the archive
/// and are content: changing them means changing the archive, and this server has no opinion. What
/// is here is the opposite -- numbers with no declaration anywhere, which this server had to
/// choose. They are defaulted to what was measured or recovered, and an operator who wants a
/// different game changes them without a rebuild.
/// <para>
/// <b>Two strictnesses on purpose.</b> The curve and the penalty bands have a right answer, and a
/// mistyped one shifts every award on the server without anybody noticing; those refuse to load.
/// The bonus rates -- premium terms, the channel -- are an operator's choice with no right answer,
/// and a server that will not boot because one of them was mistyped is worse than one that boots
/// paying the rates it could read.
/// </para>
/// </remarks>
/// <param name="Curve">What a monster of a level is worth.</param>
/// <param name="WorthDivisor">
/// What that worth is divided by before anything else applies.
/// <para>
/// <b>The one number no evidence in hand can check.</b> Halving the worth and doubling the curve
/// produce the same captured awards, so what was measured constrains the product and says nothing
/// about either factor. It is here as a dial rather than marked as a doubt because that is what it
/// is: how much a kill pays is balance, and the reference server has since changed its own mind
/// about this very number without changing the table beside it.
/// </para>
/// </param>
/// <param name="RankRates">
/// What each rank multiplies a kill by, indexed by <see cref="MonsterRank"/>.
/// <para>
/// Evidenced for its first two entries only: the captures cover an ordinary monster at 1 and a
/// champion at 2 and say nothing about the rest. The reference server's log names these four as
/// the fallback it uses when a dungeon declares nothing of its own.
/// </para>
/// </param>
/// <param name="LevelGapBands">
/// What the gap between a character's level and a monster's does to a kill's worth, as ordered
/// bands; the first band containing the difference wins.
/// <para>
/// One band is evidenced. Every captured sample has the monster one to three levels above the
/// character, which is the 1.12 rate; the rest are the reference server's word and nothing else,
/// and the archive has no table matching them. (<c>etc/worldmapexppenaltytable.etc</c> is a
/// different table for a different purpose and must not be confused for this one.)
/// </para>
/// <para>
/// The shape is not a formula and does not compress: twelve bands carrying eight distinct rates,
/// asymmetric, with a bump above 1 where the monster is a little above the character. It is a
/// hand-tuned curve discouraging both power-levelling and farming what a character has outgrown.
/// </para>
/// </param>
public sealed record ExperienceTuning(
    MonsterWorthCurve Curve,
    double WorthDivisor,
    IReadOnlyList<double> RankRates,
    IReadOnlyList<LevelGapBand> LevelGapBands)
{
    /// <summary>The bands as the reference server carries them.</summary>
    /// <remarks>
    /// Written as an unbroken run from <see cref="int.MinValue"/> to <see cref="int.MaxValue"/>,
    /// because a difference that falls in no band has no rate and there is no sensible answer to
    /// invent at that point. <see cref="Validate"/> holds a configured set to the same standard.
    /// </remarks>
    public static IReadOnlyList<LevelGapBand> DefaultLevelGapBands { get; } =
    [
        new(int.MinValue, -7, 0.05),
        new(-6, -6, 0.20),
        new(-5, -5, 0.50),
        new(-4, -4, 0.75),
        new(-3, 0, 1.00),
        // The only band the captures reach, and the only one above 1: a monster a little above the
        // character pays a premium.
        new(1, 3, 1.12),
        new(4, 5, 1.00),
        new(6, 6, 0.75),
        new(7, 7, 0.70),
        new(8, 8, 0.60),
        new(9, 9, 0.50),
        new(10, int.MaxValue, 0.05),
    ];

    /// <summary>What this server plays as when nothing is configured.</summary>
    public static ExperienceTuning Default { get; } = new(
        MonsterWorthCurve.Recovered,
        WorthDivisor: 2.0,
        RankRates: [1.0, 2.0, 3.0, 4.0],
        LevelGapBands: DefaultLevelGapBands);

    /// <summary>What this rank multiplies a kill by.</summary>
    public double RankRate(MonsterRank rank) =>
        (int)rank >= 0 && (int)rank < this.RankRates.Count
            ? this.RankRates[(int)rank]
            : this.RankRates[0];

    /// <summary>What this difference between character and monster level is worth.</summary>
    public double LevelGapRate(int characterLevel, int monsterLevel)
    {
        var difference = monsterLevel - characterLevel;
        foreach (var band in this.LevelGapBands)
        {
            if (difference >= band.From && difference <= band.To)
                return band.Rate;
        }

        // Unreachable for a validated set, and not silently 1.0 if it ever is: a missing band is a
        // misconfiguration, and paying full worth for a gap nobody described would hide it behind
        // awards that merely look large.
        throw new InvalidOperationException(
            $"No level-gap band covers a difference of {difference}.");
    }

    /// <summary>
    /// Refuses a set that would make some award unanswerable or silently wrong.
    /// </summary>
    /// <remarks>
    /// These are the values with a right answer, so this fails loudly rather than defaulting: a
    /// mistyped growth rate shifts every award on the server at once, and an award that is merely
    /// smaller than it should be looks exactly like an award.
    /// </remarks>
    /// <exception cref="InvalidDataException">The set cannot price every kill.</exception>
    public void Validate()
    {
        if (this.Curve.PerLevel <= 0)
            throw new InvalidDataException("Experience:Curve:PerLevel must be above zero.");
        if (this.Curve.LevelHeadStart < 0)
            throw new InvalidDataException("Experience:Curve:LevelHeadStart cannot be negative.");
        if (this.Curve.GrowthBeginsAtLevel < 1)
            throw new InvalidDataException("Experience:Curve:GrowthBeginsAtLevel must be at least 1.");
        if (this.Curve.GrowthPercentPerLevel < 0)
        {
            throw new InvalidDataException(
                "Experience:Curve:GrowthPercentPerLevel cannot be negative.");
        }

        if (this.WorthDivisor <= 0 || double.IsNaN(this.WorthDivisor)
            || double.IsInfinity(this.WorthDivisor))
        {
            throw new InvalidDataException(
                "Experience:WorthDivisor must be a positive, finite number.");
        }

        if (this.RankRates.Count == 0)
            throw new InvalidDataException("Experience:RankRates must name at least one rate.");
        foreach (var rate in this.RankRates)
        {
            if (rate < 0 || double.IsNaN(rate) || double.IsInfinity(rate))
                throw new InvalidDataException("Experience:RankRates holds a rate that is not a number at or above zero.");
        }

        ValidateBands(this.LevelGapBands);
    }

    /// <summary>
    /// Every level difference falls in exactly one band, from one end of the range to the other.
    /// </summary>
    /// <remarks>
    /// Checked as an unbroken, ordered, non-overlapping run rather than as a set of separate
    /// conditions, because the failure this prevents is a difference nobody thought about: a
    /// configuration that answers for the differences its author tried and throws for the one a
    /// player reaches at level 47.
    /// </remarks>
    private static void ValidateBands(IReadOnlyList<LevelGapBand> bands)
    {
        if (bands.Count == 0)
            throw new InvalidDataException("Experience:LevelGapBands must describe every level difference.");

        var expected = (long)int.MinValue;
        foreach (var band in bands)
        {
            if (band.From != expected)
            {
                throw new InvalidDataException(
                    $"Experience:LevelGapBands must run without gaps or overlaps: expected a band "
                    + $"starting at {expected}, found one starting at {band.From}.");
            }

            if (band.To < band.From)
            {
                throw new InvalidDataException(
                    $"Experience:LevelGapBands holds a band from {band.From} to {band.To}, which is empty.");
            }

            if (band.Rate < 0 || double.IsNaN(band.Rate) || double.IsInfinity(band.Rate))
            {
                throw new InvalidDataException(
                    $"Experience:LevelGapBands band from {band.From} carries a rate that is not a "
                    + "number at or above zero.");
            }

            expected = (long)band.To + 1;
        }

        if (expected != (long)int.MaxValue + 1)
        {
            throw new InvalidDataException(
                $"Experience:LevelGapBands stops at {expected - 1}; it must reach {int.MaxValue}.");
        }
    }
}
