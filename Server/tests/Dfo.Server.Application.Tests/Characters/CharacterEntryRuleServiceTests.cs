using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Application.Characters;

namespace Dfo.Server.Application.Tests.Characters;

/// <summary>
/// The rules a character's entry turns on, checked with no database in reach.
/// </summary>
/// <remarks>
/// Each of these used to live inside a SQLite class, which meant the only way to ask what it did
/// was to build a database, put rows in it, and read rows back out. What is asserted below is the
/// same behaviour those round trips asserted; what has gone is the round trip.
/// </remarks>
public sealed class CharacterEntryRuleServiceTests
{
    public sealed class GoldLimits
    {
        [Theory]
        [InlineData(499_999_999, 900_000_000, 0)]
        [InlineData(500_000_000, 900_000_000, 1)]
        [InlineData(599_999_999, 900_000_000, 1)]
        [InlineData(600_000_000, 900_000_000, 2)]
        [InlineData(699_999_999, 900_000_000, 2)]
        [InlineData(700_000_000, 900_000_000, 3)]
        [InlineData(799_999_999, 900_000_000, 3)]
        [InlineData(800_000_000, 900_000_000, 4)]
        public void TheBandTheLimitsFallIn(int carry, int auction, int expected) =>
            Assert.Equal(expected, new GoldLimitRuleService().UpgradeLevel(carry, auction));

        /// <remarks>
        /// Either limit can be the one that decides, so both orders are asked. A character who may
        /// carry a fortune but auction almost none of it is held to the auction limit.
        /// </remarks>
        [Theory]
        [InlineData(900_000_000, 499_999_999, 0)]
        [InlineData(499_999_999, 900_000_000, 0)]
        [InlineData(900_000_000, 700_000_000, 3)]
        [InlineData(700_000_000, 900_000_000, 3)]
        public void TheLowerOfTheTwoDecides(int carry, int auction, int expected) =>
            Assert.Equal(expected, new GoldLimitRuleService().UpgradeLevel(carry, auction));
    }

    public sealed class ExpertSkillContracts
    {
        private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch.AddDays(1000);

        private static readonly IReadOnlySet<int> OverSkill = new HashSet<int> { 580 };

        [Fact]
        public void OneContractStillRunningLeavesEverythingAlone() =>
            Assert.False(new ExpertSkillContractRuleService().HasRunOut(
                [(580, Now.AddHours(1).ToUnixTimeSeconds()), (580, 1)], OverSkill, Now));

        [Fact]
        public void EveryContractHavingRunOutBringsLevelsDown() =>
            Assert.True(new ExpertSkillContractRuleService().HasRunOut(
                [(580, 1), (580, 2)], OverSkill, Now));

        /// <remarks>
        /// Never having held a contract is not the same as one running out. Premiums of other
        /// kinds are not contracts and are passed over whatever their end times say.
        /// </remarks>
        [Fact]
        public void NeverHavingHeldOneIsNotTheSameAsItRunningOut() =>
            Assert.False(new ExpertSkillContractRuleService().HasRunOut(
                [(999, 1), (1000, Now.AddHours(1).ToUnixTimeSeconds())], OverSkill, Now));

        [Fact]
        public void OnlyLevelsAboveTheirOrdinaryCeilingComeDown()
        {
            var caps = new ExpertSkillContractRuleService().CapsFor(
                [(0, 1, 100, 4), (0, 2, 101, 1), (0, 3, 102, 5), (0, 4, 103, 3), (0, 5, 104, 2)],
                new Dictionary<int, SkillPointCostDefinition>
                {
                    [100] = Definition(100, ordinaryMaximum: 2),
                    [101] = Definition(101, ordinaryMaximum: 0),
                    [102] = Definition(102, ordinaryMaximum: null),
                    [103] = Definition(103, ordinaryMaximum: 0),
                    [104] = Definition(104, ordinaryMaximum: 2),
                },
                new Dictionary<int, byte> { [103] = 1 });

            Assert.Equal(
                [
                    new ExpertSkillCap(0, 1, 2),
                    new ExpertSkillCap(0, 2, null),
                    new ExpertSkillCap(0, 4, 1),
                ],
                caps);
        }

        /// <remarks>
        /// A skill whose level is fixed by advancement rather than bought was never held up by a
        /// contract, so nothing about it comes down when one runs out.
        /// </remarks>
        [Fact]
        public void ASkillWhoseLevelIsFixedIsLeftAlone() =>
            Assert.Empty(new ExpertSkillContractRuleService().CapsFor(
                [(0, 1, 100, 9)],
                new Dictionary<int, SkillPointCostDefinition>
                    { [100] = Definition(100, ordinaryMaximum: 2, isFixedLevel: true) },
                new Dictionary<int, byte>()));

        /// <remarks>
        /// A skill this advancement cannot learn at all has no ordinary ceiling to be held to.
        /// </remarks>
        [Fact]
        public void ASkillTheAdvancementCannotLearnIsLeftAlone() =>
            Assert.Empty(new ExpertSkillContractRuleService().CapsFor(
                [(0, 1, 100, 9)],
                new Dictionary<int, SkillPointCostDefinition>
                    { [100] = Definition(100, ordinaryMaximum: null) },
                new Dictionary<int, byte>()));

        [Fact]
        public void ASkillWithNoDefinitionAtAllIsLeftAlone() =>
            Assert.Empty(new ExpertSkillContractRuleService().CapsFor(
                [(0, 1, 100, 9)],
                new Dictionary<int, SkillPointCostDefinition>(),
                new Dictionary<int, byte>()));

        private static SkillPointCostDefinition Definition(
            int skillId, int? ordinaryMaximum, bool isFixedLevel = false) =>
            new(skillId, false, [10], isFixedLevel, 1, 10, 0, 1, 1,
                OrdinaryMaximumLevel: ordinaryMaximum);
    }

    public sealed class DungeonEffects
    {
        private static readonly DungeonExperienceProgressionPolicy Policy = new(
            [.. Enumerable.Range(1, 85).Select(value => checked((uint)(value * 1000)))],
            1_000_000, 10_000, 100);

        [Fact]
        public void AnEffectThisServerKnowsIsCarriedOut() =>
            Assert.True(new DungeonEffectRuleService().CanCarryOut(
                1, 3, 3, 7, DungeonEffectKinds.LuckyStar, 3, 7));

        [Theory]
        [InlineData(2, 3, 3, 7, DungeonEffectKinds.LuckyStar)]
        [InlineData(1, 4, 3, 7, DungeonEffectKinds.LuckyStar)]
        [InlineData(1, 3, 1, 7, DungeonEffectKinds.LuckyStar)]
        [InlineData(1, 3, 3, 8, DungeonEffectKinds.LuckyStar)]
        [InlineData(1, 3, 3, 7, "something-else")]
        public void AnEffectThatDoesNotBelongHereIsNotCarriedOut(
            int version, int recordedAccount, int scope, long target, string kind) =>
            Assert.False(new DungeonEffectRuleService().CanCarryOut(
                version, recordedAccount, scope, target, kind, 3, 7));

        [Fact]
        public void EverySettlementKindIsCarriedOut()
        {
            var rules = new DungeonEffectRuleService();
            Assert.True(rules.CanCarryOut(1, 3, 3, 7, DungeonEffectKinds.SettlementExperience, 3, 7));
            Assert.True(rules.CanCarryOut(
                1, 3, 3, 7, DungeonEffectKinds.SettlementScoreExperience, 3, 7));
        }

        [Fact]
        public void ALuckyStarBelowTheCeilingIsGranted() =>
            Assert.Equal(new LuckyStarGrant(true, 5), new DungeonEffectRuleService().GrantLuckyStar(4));

        /// <remarks>
        /// At the ceiling it is refused rather than granted and discarded, so what the player is
        /// told matches what they keep.
        /// </remarks>
        [Fact]
        public void ALuckyStarAtTheCeilingIsRefused() =>
            Assert.Equal(
                new LuckyStarGrant(false, 999), new DungeonEffectRuleService().GrantLuckyStar(999));

        /// <remarks>
        /// The boundary is at the last level, not past it. This is the case nothing covered before
        /// the rule was moved, and the one that decides whether the whole gain becomes honour.
        /// </remarks>
        [Fact]
        public void AtTheLastLevelTheWholeGainBecomesHonour()
        {
            var split = new DungeonEffectRuleService().SplitExperience(
                new DungeonExperienceSettlement(86, 84_000, 25, NormalizeMaxLevelExp: true), Policy);

            Assert.Equal(new DungeonExperienceSplit(25, 0, 86, 85_000, true, true), split);
        }

        [Fact]
        public void BelowTheLastLevelAGainThatStaysBelowItIsAllOrdinary()
        {
            var split = new DungeonEffectRuleService().SplitExperience(
                new DungeonExperienceSettlement(85, 80_000, 25, NormalizeMaxLevelExp: false), Policy);

            Assert.Equal(new DungeonExperienceSplit(0, 25, 85, 80_025, false, true), split);
        }

        /// <remarks>Only what overshoots the last level's threshold becomes honour.</remarks>
        [Fact]
        public void CrossingIntoTheLastLevelSplitsTheGain()
        {
            var split = new DungeonEffectRuleService().SplitExperience(
                new DungeonExperienceSettlement(85, 84_990, 25, NormalizeMaxLevelExp: true), Policy);

            Assert.Equal(new DungeonExperienceSplit(15, 10, 86, 85_000, false, true), split);
        }

        [Fact]
        public void HonourAccumulatesAndEarnsCapsules() =>
            Assert.Equal(
                new DungeonHonourOutcome(30, 27, 25),
                new DungeonEffectRuleService().AccumulateHonour(25, 5, 2, Policy));

        /// <remarks>
        /// Any honour at all earns at least one capsule while the rate is above nothing, so a gain
        /// too small to divide is not rounded away to no reward.
        /// </remarks>
        [Fact]
        public void AnyHonourAtAllEarnsAtLeastOneCapsule()
        {
            var stingy = new DungeonExperienceProgressionPolicy(Policy.LevelThresholds, 1_000_000, 10_000, 1);

            Assert.Equal(
                new DungeonHonourOutcome(1, 1, 1),
                new DungeonEffectRuleService().AccumulateHonour(1, 0, 0, stingy));
        }

        [Fact]
        public void HonourAndCapsulesStopAtTheirCeilings() =>
            Assert.Equal(
                new DungeonHonourOutcome(1_000_000, 10_000, 0),
                new DungeonEffectRuleService().AccumulateHonour(50, 1_000_000, 10_000, Policy));
    }
}
