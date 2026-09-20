using Dfo.Server.Data.World;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Application.Tests.World;

/// <summary>
/// What clearing a dungeon is worth, priced from the archive's own tables.
/// </summary>
/// <remarks>
/// Three files meet here. The clear is priced at the dungeon's level out of the quest reward table
/// -- one table, two callers -- and scaled by the difficulty rate, the dungeon's own weight and the
/// world-wide clear rate. The rank bonus is a proportion of that, chosen by where the client's own
/// score falls in the rank table.
/// <para>
/// The rates below are this archive's, read out of it rather than transcribed from the reference
/// server, whose fallback for the rank rates is <c>0, 0.01, 0.02, 0.03, 0.05</c> -- an order of
/// magnitude under what the archive declares, and invisible in play.
/// </para>
/// </remarks>
public sealed class DungeonClearExperienceRulesTests
{
    /// <summary>Dungeon 144's weight, as the archive declares it and as a single.</summary>
    private const double CapturedDungeonWeight = 1.45f;

    /// <summary>The head of <c>n_quest/questParameter.etc</c>'s reward table, by level.</summary>
    private static readonly QuestParameters Quests = new(
        ExperienceByLevel: [100, 100, 126, 187, 293, 456, 683],
        DifficultyPermille: new Dictionary<char, int> { ['M'] = 10 },
        GreenPenaltyPercent: 100,
        GreyPenaltyPercent: 100,
        EpicGreenPenaltyPercent: 100,
        EpicGreyPenaltyPercent: 100);

    /// <summary>What <c>Etc/ServerParameter.etc</c> declares.</summary>
    private static readonly ExperienceParameters Rates = new(
        DifficultyRates: [1.3f, 2, 2.5f, 3, 4],
        PartyMemberRates: [1, 2, 3, 4],
        MonsterOverallRate: 1,
        ClearOverallRate: 1,
        ClearRankRates: [0.05, 0.1, 0.12, 0.15, 0.2]);

    /// <summary>What <c>Etc/RankSystemInfo.etc</c> declares.</summary>
    private static readonly DungeonRankGrades Grades = new([99, 90, 80, 60, 50, 30, 20, 10]);

    /// <summary>A clear is the table's value at the dungeon's level, scaled three ways.</summary>
    /// <remarks>
    /// Level 5 pays 293; the easiest difficulty is already a bonus at 1.3; dungeon 144 weighs 1.45.
    /// <c>293 x 1 x 1.3 x 1.45 = 552.3</c>, floored to 552.
    /// </remarks>
    [Fact]
    public void AClearIsPricedAtTheDungeonsOwnLevel() =>
        Assert.Equal(552u, Base(basisLevel: 5));

    /// <summary>The dungeon's weight is not a bonus dungeons may only add.</summary>
    /// <remarks>
    /// The tutorial's is 0.1 and its clear pays a tenth, which is the intent: <c>293 x 1.3 x 0.1 =
    /// 38.09</c>.
    /// </remarks>
    [Fact]
    public void AWeightBelowOnePaysLess() =>
        Assert.Equal(38u, Base(basisLevel: 5, weight: 0.1));

    /// <summary>A level the reward table does not reach pays nothing.</summary>
    /// <remarks>
    /// Nothing, not the last entry. A dungeon whose level is off the end of the table is one this
    /// server cannot price, and paying it the highest level's reward would be inventing a figure
    /// for exactly the dungeons where it would be largest.
    /// </remarks>
    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    public void ALevelTheTableDoesNotReachPaysNothing(int basisLevel) =>
        Assert.Equal(0u, Base(basisLevel));

    /// <summary>Each rank band pays the rate the archive pairs with it.</summary>
    /// <remarks>
    /// The two tables pair off from their opposite ends: the rank table is written best first
    /// (99, 90, 80, 60, 50) and the rate table best last (0.05 … 0.2), so 99 earns 0.2 and 50
    /// earns 0.05. Getting that reversal wrong pays the worst rate for the best clear and would
    /// look, from any single run, like the rank bonus simply being small.
    /// </remarks>
    [Theory]
    [InlineData(100, 110u)] // above the top grade, still the top rate: 552 x 0.20
    [InlineData(99, 110u)]
    [InlineData(90, 82u)] //  552 x 0.15
    [InlineData(80, 66u)] //  552 x 0.12
    [InlineData(60, 55u)] //  552 x 0.10
    [InlineData(50, 27u)] //  552 x 0.05
    public void EachRankBandPaysTheRateTheArchivePairsWithIt(int score, uint expected) =>
        Assert.Equal(expected, Rank(clearBase: 552, score));

    /// <summary>A score below every band that carries a rate earns nothing.</summary>
    /// <remarks>
    /// Nothing, not the smallest rate. Fifty is a rank in its own right and this run did not reach
    /// it; the grades below it -- 30, 20, 10 -- exist in the table but no rate is paired with them.
    /// </remarks>
    [Theory]
    [InlineData(49)]
    [InlineData(30)]
    [InlineData(0)]
    public void AScoreBelowEveryPayingBandEarnsNothing(int score) =>
        Assert.Equal(0u, Rank(clearBase: 552, score));

    /// <summary>A clear worth nothing earns no rank bonus either.</summary>
    [Fact]
    public void AClearWorthNothingEarnsNoRankBonus() =>
        Assert.Equal(0u, Rank(clearBase: 0, score: 99));

    /// <summary>Without the rank tables there is no rank bonus, and the clear still pays.</summary>
    /// <remarks>
    /// Both halves matter. A missing table must not be read as a rate of zero applied to a bonus
    /// nobody earned, nor as a reason to refuse the clear itself: the run happened and it is worth
    /// what it is worth.
    /// </remarks>
    [Fact]
    public void WithoutTheRankTablesTheClearStillPays()
    {
        var withoutRates = Rates with { ClearRankRates = [] };

        Assert.Equal(552u, DungeonClearExperienceRules.BaseWorth(
            Circumstances(basisLevel: 5), Quests, withoutRates));
        Assert.Equal(0u, DungeonClearExperienceRules.RankBonus(552, 99, Grades, withoutRates));
        Assert.Equal(0u, DungeonClearExperienceRules.RankBonus(
            552, 99, DungeonRankGrades.None, Rates));
    }

    /// <summary>A difficulty the archive does not list is worth one, not the easiest.</summary>
    [Fact]
    public void ADifficultyTheArchiveDoesNotListIsWorthOne() =>
        Assert.Equal(424u, Base(basisLevel: 5, difficulty: (DungeonDifficulty)9)); // 293 x 1 x 1.45

    private static uint Base(int basisLevel, double weight = CapturedDungeonWeight, DungeonDifficulty difficulty = 0) =>
        DungeonClearExperienceRules.BaseWorth(
            Circumstances(basisLevel, weight, difficulty), Quests, Rates);

    private static uint Rank(uint clearBase, int score) =>
        DungeonClearExperienceRules.RankBonus(
            clearBase, (byte)Math.Clamp(score, 0, 255), Grades, Rates);

    private static ClearCircumstances Circumstances(
        int basisLevel, double weight = CapturedDungeonWeight, DungeonDifficulty difficulty = 0) =>
        new(basisLevel, difficulty, weight, ClientRankPoint: 0);
}
