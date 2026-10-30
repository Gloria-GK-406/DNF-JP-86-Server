using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.World;

namespace Dfo.Server.Application.Tests.World;

/// <summary>
/// What a monster of a given level is worth, and the evidence that it is a formula.
/// </summary>
/// <remarks>
/// The reference server carries a hundred hard-coded numbers where this carries an expression.
/// The corpus below is those hundred numbers, kept as the evidence rather than as the answer:
/// ninety-nine of them fall out of <c>10(n+2) x (100 + 3max(0, n-15))%</c> exactly, and a
/// hundred numbers collapsing into three parameters is not something that happens by accident.
/// That agreement is what the formula rests on -- not the reference server's authority, which it
/// does not have.
/// </remarks>
public sealed class MonsterExperienceWorthTests
{
    /// <summary>
    /// The reference server's table, transcribed. Levels 1 to 100 in order.
    /// </summary>
    /// <remarks>
    /// Here to be disagreed with in exactly one place, which is the point of keeping it.
    /// </remarks>
    private static readonly uint[] ReferenceTable =
    [
        30, 40, 50, 60, 70, 80, 90, 100, 110, 120,
        130, 140, 150, 160, 170, 185, 201, 218, 235, 253,
        271, 290, 310, 330, 351, 372, 394, 417, 440, 464,
        488, 513, 539, 565, 592, 619, 647, 676, 705, 735,
        765, 796, 828, 860, 893, 926, 960, 995, 1030, 1066,
        1102, 1139, 1177, 1215, 1254, 1293, 1333, 1374, 1415, 1457,
        1499, 1542, 1586, 1630, 1675, 1720, 1766, 1813, 1860, 1908,
        1956, 2005, 2055, 2105, 2156, 2207, 2259, 2312, 2365, 2419,
        2473, 2528, 2584, 2640, 2697, 2754, 2812, 2871, 2930, 2990,
        3050, 3111, 3173, 3235, 3298, 3361, 3425, 3490, 3555, 3575,
    ];

    /// <summary>The evidence: ninety-nine of a hundred, exactly.</summary>
    [Fact]
    public void TheFormulaReproducesEveryLevelTheReferenceTableAgreesOn()
    {
        var disagreements = Enumerable.Range(1, 100)
            .Where(level => MonsterExperienceWorth.For(level, MonsterWorthCurve.Recovered) != ReferenceTable[level - 1])
            .ToArray();

        Assert.Equal([100], disagreements);
    }

    /// <remarks>
    /// The one disagreement, asserted rather than tolerated. The reference server's table drops to
    /// 3575 at the level ceiling where its own first ninety-nine entries say 3621 -- a hand edit
    /// with nothing behind it. Following it would mean carrying a branch for one cell on the word
    /// of a server that is not an authority; this server does not, and says so here so that a
    /// future reader meeting the two numbers knows which is deliberate.
    /// </remarks>
    [Fact]
    public void TheLevelCeilingsHandEditIsNotFollowed()
    {
        Assert.Equal(3575u, ReferenceTable[99]);
        Assert.Equal(3621u, MonsterExperienceWorth.For(100, MonsterWorthCurve.Recovered));
    }

    /// <summary>Below the growth level it is flat: ten a level, plus two levels of head start.</summary>
    [Theory]
    [InlineData(1, 30)]
    [InlineData(2, 40)]
    [InlineData(10, 120)]
    [InlineData(15, 170)]
    public void EarlyLevelsAreTenALevelOverATwoLevelHeadStart(int level, uint worth)
    {
        Assert.Equal(worth, MonsterExperienceWorth.For(level, MonsterWorthCurve.Recovered));
        Assert.Equal((uint)(10 * (level + 2)), MonsterExperienceWorth.For(level, MonsterWorthCurve.Recovered));
    }

    /// <remarks>
    /// The two shapes the table looks like are one expression, and level 15 is where the percentage
    /// reaches exactly 100 -- so the flat rule and the growing rule agree there rather than being
    /// stitched together. Asserted because a fitted boundary would be off by a unit somewhere and
    /// this one is not.
    /// </remarks>
    [Fact]
    public void TheFlatAndGrowingRulesMeetExactlyAtTheGrowthLevel()
    {
        // At fifteen the percentage is exactly 100, so the growing rule gives the flat answer.
        Assert.Equal((uint)(10 * (15 + 2)), MonsterExperienceWorth.For(15, MonsterWorthCurve.Recovered));

        // At sixteen it is 103, and the flat answer alone is no longer enough.
        Assert.Equal((uint)(10 * (16 + 2)), 180u);
        Assert.Equal(185u, MonsterExperienceWorth.For(16, MonsterWorthCurve.Recovered));
        Assert.True(MonsterExperienceWorth.For(16, MonsterWorthCurve.Recovered) > 180u);
    }

    /// <summary>Each level past fifteen adds three percentage points, not three per cent.</summary>
    /// <remarks>
    /// The distinction matters and the table settles it: at level 50 the rate is 205 per cent, not
    /// 1.03^35 -- which would be 281 per cent and would have made every late-game monster worth
    /// half again what the table says.
    /// </remarks>
    [Theory]
    [InlineData(16, 103)]
    [InlineData(20, 115)]
    [InlineData(50, 205)]
    [InlineData(99, 352)]
    public void GrowthIsPercentagePointsAddedNotCompounded(int level, int expectedPercent)
    {
        var flat = 10 * (level + 2);

        Assert.Equal(
            (uint)Math.Round(flat * expectedPercent / 100.0),
            MonsterExperienceWorth.For(level, MonsterWorthCurve.Recovered));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void ALevelThatCannotExistIsWorthNothing(int level) =>
        Assert.Equal(0u, MonsterExperienceWorth.For(level, MonsterWorthCurve.Recovered));

    /// <summary>Past the table it keeps growing rather than falling off a cliff.</summary>
    /// <remarks>
    /// The reference table stops at 100 because its client stopped there. Nothing in the
    /// expression does, and a level the archive may raise later should not silently become worth
    /// nothing -- which is what a table lookup would have done.
    /// </remarks>
    [Fact]
    public void AboveTheReferenceTableTheFormulaStillAnswers()
    {
        Assert.True(MonsterExperienceWorth.For(110, MonsterWorthCurve.Recovered) > MonsterExperienceWorth.For(100, MonsterWorthCurve.Recovered));
        Assert.Equal(10u * 112 * (100 + (3 * 95)) / 100, MonsterExperienceWorth.For(110, MonsterWorthCurve.Recovered));
    }
}
