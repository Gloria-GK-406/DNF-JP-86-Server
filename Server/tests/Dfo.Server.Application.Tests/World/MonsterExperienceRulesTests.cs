using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Application.Tests.World;

/// <summary>
/// What a kill is worth, checked against the run that was captured.
/// </summary>
/// <remarks>
/// On 2026-08-27 the reference server was run against the real client through dungeon 144 at
/// difficulty 0, and its own log recorded every step of every award. Four combinations came out of
/// it, and they are the acceptance for this rule:
///
/// <code>
/// level 4 ordinary   30 x 1.3 x 1.45        = 56.55  -> 56   x1.12 = 62.72  -> 62
/// level 5 ordinary   35 x 1.3 x 1.45        = 65.97  -> 65   x1.12 = 72.80  -> 72
/// level 4 champion   30 x 1.3 x 1.45 x 2    = 113.1  -> 113  x1.12 = 126.56 -> 126
/// level 5 champion   35 x 1.3 x 1.45 x 2    = 131.95 -> 131  x1.12 = 146.72 -> 146
/// </code>
///
/// What reached the wire was three to seven higher than each -- 65, 75, 132 and 153 -- because a
/// channel contribution was added on top. That contribution is not this rule's, and these are the
/// numbers underneath it.
/// </remarks>
public sealed class MonsterExperienceRulesTests
{
    /// <summary>Dungeon 144's weight, as the archive declares it and as a single.</summary>
    private const double CapturedDungeonWeight = 1.45f;

    private static readonly ExperienceParameters CapturedRates = new(
        DifficultyRates: [1.3f, 2, 2.5f, 3, 4],
        PartyMemberRates: [1, 2, 3, 4],
        MonsterOverallRate: 1);

    /// <summary>The four captured combinations, exactly.</summary>
    [Theory]
    [InlineData(4, MonsterRank.Ordinary, 2, 62u)]
    [InlineData(5, MonsterRank.Ordinary, 2, 72u)]
    [InlineData(4, MonsterRank.Champion, 2, 126u)]
    [InlineData(5, MonsterRank.Champion, 2, 146u)]
    public void TheCapturedKillsAreWorthWhatWasCaptured(
        int monsterLevel, MonsterRank rank, int characterLevel, uint worth) =>
        Assert.Equal(worth, MonsterExperienceRules.WorthOf(Kill(monsterLevel, rank, characterLevel), CapturedRates));

    /// <remarks>
    /// The same run levelled the character mid-way, and the awards did not move: at character level
    /// 3 a level-5 monster still paid 72 and a level-4 one still 62, because both gaps are inside
    /// the one band the captures cover.
    /// </remarks>
    [Theory]
    [InlineData(5, 72u)]
    [InlineData(4, 62u)]
    public void ALevelGainInsideTheSameBandDoesNotChangeWhatAKillPays(int monsterLevel, uint worth)
    {
        Assert.Equal(
            worth,
            MonsterExperienceRules.WorthOf(Kill(monsterLevel, MonsterRank.Ordinary, 2), CapturedRates));
        Assert.Equal(
            worth,
            MonsterExperienceRules.WorthOf(Kill(monsterLevel, MonsterRank.Ordinary, 3), CapturedRates));
    }

    /// <summary>A champion is worth twice an ordinary monster of the same level.</summary>
    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    public void AChampionIsWorthTwiceAnOrdinaryMonster(int monsterLevel)
    {
        var ordinary = MonsterExperienceRules.WorthOf(
            Kill(monsterLevel, MonsterRank.Ordinary, 2), CapturedRates);
        var champion = MonsterExperienceRules.WorthOf(
            Kill(monsterLevel, MonsterRank.Champion, 2), CapturedRates);

        // Not exactly twice, and that is the point: the two roundings happen before the doubling
        // is visible, so a rule that multiplied the finished ordinary amount would answer 124 and
        // 144 rather than the captured 126 and 146.
        Assert.Equal(2.0, MonsterExperienceRules.RankRate(MonsterRank.Champion));
        Assert.True(champion > ordinary * 2, $"{champion} should exceed twice {ordinary}");
    }

    /// <remarks>
    /// Two roundings, in order. Asserted through a case where one rounding would differ: the
    /// level-5 ordinary kill floors 65.97 to 65 and only then applies 1.12, giving 72. Rounding
    /// once at the end -- 65.97 x 1.12 = 73.9 -- would pay 73.
    /// </remarks>
    [Fact]
    public void TheSharedAmountIsRoundedBeforeTheLevelGapIsApplied()
    {
        Assert.Equal(72u, MonsterExperienceRules.WorthOf(Kill(5, MonsterRank.Ordinary, 2), CapturedRates));
        Assert.Equal(73u, (uint)Math.Floor(35 * 1.3f * CapturedDungeonWeight * 1.12));
    }

    /// <summary>The one level-gap band the captures reach, and the one above 1.</summary>
    [Theory]
    [InlineData(1, 1.12)]
    [InlineData(2, 1.12)]
    [InlineData(3, 1.12)]
    public void AMonsterALittleAboveTheCharacterPaysAPremium(int gap, double rate) =>
        Assert.Equal(rate, MonsterExperienceRules.LevelGapRate(10, 10 + gap));

    /// <remarks>
    /// The seven bands with no evidence behind them, asserted so that changing one is a deliberate
    /// act rather than a silent drift. What they assert is the transcription, not its truth.
    /// </remarks>
    [Theory]
    [InlineData(-20, 0.05)]
    [InlineData(-7, 0.05)]
    [InlineData(-6, 0.20)]
    [InlineData(-5, 0.50)]
    [InlineData(-4, 0.75)]
    [InlineData(-3, 1.00)]
    [InlineData(0, 1.00)]
    [InlineData(4, 1.00)]
    [InlineData(5, 1.00)]
    [InlineData(6, 0.75)]
    [InlineData(7, 0.70)]
    [InlineData(8, 0.60)]
    [InlineData(9, 0.50)]
    [InlineData(10, 0.05)]
    [InlineData(40, 0.05)]
    public void TheUnevidencedLevelGapBandsAreTranscribedAsTheyStand(int gap, double rate) =>
        Assert.Equal(rate, MonsterExperienceRules.LevelGapRate(20, 20 + gap));

    /// <summary>Outgrowing a monster costs far more than being outmatched by one gains.</summary>
    /// <remarks>
    /// The asymmetry is the whole shape of the curve and the reason it is not a formula: three
    /// levels above the character pays a premium, three levels below pays in full, and six below
    /// pays a fifth.
    /// </remarks>
    [Fact]
    public void TheCurveIsNotSymmetric()
    {
        Assert.Equal(1.12, MonsterExperienceRules.LevelGapRate(10, 13));
        Assert.Equal(1.00, MonsterExperienceRules.LevelGapRate(10, 7));
        Assert.Equal(0.20, MonsterExperienceRules.LevelGapRate(10, 4));
        Assert.Equal(0.05, MonsterExperienceRules.LevelGapRate(10, 3));
    }

    /// <summary>A dungeon weight below one pays less, as the tutorial's does.</summary>
    [Fact]
    public void ADungeonWeightBelowOneReducesWhatAKillPays()
    {
        var tutorial = new KillCircumstances(5, MonsterRank.Ordinary, 2, 0, 0.1f, 1);
        var captured = Kill(5, MonsterRank.Ordinary, 2);

        Assert.True(
            MonsterExperienceRules.WorthOf(tutorial, CapturedRates)
                < MonsterExperienceRules.WorthOf(captured, CapturedRates) / 10);
    }

    [Fact]
    public void AMonsterOfNoLevelIsWorthNothing() =>
        Assert.Equal(0u, MonsterExperienceRules.WorthOf(Kill(0, MonsterRank.Ordinary, 2), CapturedRates));

    /// <summary>A party of one is the only size the captures exercise, and it neither scales nor divides.</summary>
    [Fact]
    public void SoloPlayNeitherScalesNorDivides()
    {
        Assert.Equal(1.0, CapturedRates.PartyMemberRate(1));
        Assert.Equal(72u, MonsterExperienceRules.WorthOf(Kill(5, MonsterRank.Ordinary, 2), CapturedRates));
    }

    /// <summary>An AI character is not a monster and is worth nothing.</summary>
    /// <remarks>
    /// A room lists its companions beside its monsters and numbers them from the same sequence, so
    /// a companion's death arrives looking like a kill. The tutorial's escort is level 80, and
    /// billing it would pay a character's own level for standing next to the player.
    /// </remarks>
    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(5, false)]
    [InlineData(8, false)]
    public void OnlyTheMonsterRanksAreMonsters(byte declaredActorType, bool isMonster) =>
        Assert.Equal(isMonster, MonsterExperienceRules.IsMonster(declaredActorType));

    /// <remarks>
    /// Type 4 is where this repository's two readers disagree: the clear test skips anything above
    /// 3, the room frame's writer treats 5 and up as companions. Taking the first reading is what
    /// pays nothing for an actor whose nature is unsettled, and asserting it keeps a later reader
    /// from flipping it without noticing there was a choice.
    /// </remarks>
    [Fact]
    public void TheDisputedActorTypeIsNotPaidFor() =>
        Assert.False(MonsterExperienceRules.IsMonster(4));

    private static KillCircumstances Kill(
        int monsterLevel, MonsterRank rank, int characterLevel) =>
        new(monsterLevel, rank, characterLevel, Difficulty: 0, CapturedDungeonWeight, PartyMemberCount: 1);
}
