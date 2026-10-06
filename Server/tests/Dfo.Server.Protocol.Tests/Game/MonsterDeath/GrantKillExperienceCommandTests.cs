using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Quests;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Protocol.Game.MonsterDeath;

namespace Dfo.Server.Protocol.Tests.Game.MonsterDeath;

/// <summary>
/// One captured kill, priced and paid end to end.
/// </summary>
/// <remarks>
/// The rule that says what a kill is worth is checked next door against the same four samples, and
/// so is the rule that says what the channel adds. What only this can check is the join: that the
/// declared rate is applied to that worth, that the amount lands on the line the client draws, and
/// that the two together come to what the reference server actually paid on the wire.
/// <para>
/// On 2026-08-27, dungeon 144 at difficulty 0, the wire carried 65, 75, 132 and 153. Underneath
/// them the log recorded 62, 72, 126 and 146 -- and the difference each time is the five percent
/// the archive declares for the channel that carries dungeon 144.
/// </para>
/// </remarks>
public sealed class GrantKillExperienceCommandTests
{
    /// <summary>Dungeon 144's weight, as the archive declares it and as a single.</summary>
    private const double CapturedDungeonWeight = 1.45f;

    /// <summary>What the channel carrying dungeon 144 declares.</summary>
    private const double DeclaredChannelRate = 0.05;

    private const int CapturedDungeon = 144;

    /// <summary>Each captured award, from the monster that earned it.</summary>
    [Theory]
    [InlineData(4, MonsterRank.Ordinary, 62u, 3u, 65u)]
    [InlineData(5, MonsterRank.Ordinary, 72u, 3u, 75u)]
    [InlineData(4, MonsterRank.Champion, 126u, 6u, 132u)]
    [InlineData(5, MonsterRank.Champion, 146u, 7u, 153u)]
    public async Task ACapturedKillPaysWhatTheWireCarried(
        int monsterLevel, MonsterRank rank, uint worth, uint fromChannel, uint awarded)
    {
        var award = await Award(monsterLevel, rank, DeclaredChannelRate);

        Assert.NotNull(award);
        Assert.Equal(worth, award.Base);
        Assert.Equal(awarded, award.Gained);
        Assert.Equal(fromChannel, award.Breakdown.Channel);
    }

    /// <summary>
    /// The same kill on a channel that does not carry this dungeon pays the worth and no more.
    /// </summary>
    /// <remarks>
    /// The condition, seen from the far end. Before 2026-08-29 this case did not exist: the rate
    /// was applied to every kill on every map, and the four samples above -- all taken in a dungeon
    /// its channel carried -- agreed with that just as well as they agree with this.
    /// </remarks>
    [Fact]
    public async Task TheSameKillOutsideTheChannelsDungeonsPaysOnlyItsWorth()
    {
        var award = await Award(monsterLevel: 5, MonsterRank.Ordinary, channelRate: 0);

        Assert.NotNull(award);
        Assert.Equal(72u, award.Base);
        Assert.Equal(72u, award.Gained);
        Assert.Equal(new ExperienceBreakdown(Base: 72), award.Breakdown);
    }

    private static async Task<ExperienceAward?> Award(
        int monsterLevel, MonsterRank rank, double channelRate)
    {
        var handler = new GrantKillExperienceCommandHandler(new PricingGrant(new ExperienceCalculationDomainService(new ExperienceValuationDomainService(new ExperienceRuleService(
                new CapturedParameters(),
                new UngradedRanks(),
                new NoProgression(),
                new NoChannelExperience(),
                ExperienceTuning.Default,
                ExperienceEntitlementPolicy.None)),
            new FixedChannelBonus(channelRate)), characterLevel: 3), TimeProvider.System);

        var result = await handler.HandleAsync(
            new CommandExecutionContext(
                Guid.Empty,
                typeof(GrantKillExperienceCommand),
                typeof(GrantKillExperienceCommandHandler),
                CommandExecutionKind.Transactional),
            new GrantKillExperienceCommand(
                new AccountId(1),
                new CharacterId(60),
                new KilledMonster(Code: 63751, monsterLevel, rank),
                CapturedDungeon,
                Difficulty: 0,
                CapturedDungeonWeight),
            CancellationToken.None);

        return result.Award;
    }

    /// <summary>The archive's rates as they stood for the captured run.</summary>
    /// <summary>Nothing on this path reads a rank grade.</summary>
    private sealed class UngradedRanks : IDungeonRankGradeSource
    {
        public DungeonRankGrades Load() => throw new NotSupportedException();
    }

    /// <summary>Nothing on this path turns experience into a level.</summary>
    private sealed class NoProgression : IDungeonExperienceProgressionPolicySource
    {
        public DungeonExperienceProgressionPolicy Load() => throw new NotSupportedException();
    }

    /// <summary>Nothing on this path reads a channel's reward.</summary>
    private sealed class NoChannelExperience : IChannelExperienceSource
    {
        public ChannelExperienceCatalog Load() => throw new NotSupportedException();
    }

    /// <summary>Nothing on this path reads a quest.</summary>
    private sealed class NoQuestContent : IQuestContentRuleService
    {
        public ValueTask<IReadOnlyList<QuestDefinition>> AllAsync(CancellationToken ct) =>
            throw new NotSupportedException();

        public ValueTask<QuestDefinition?> FindAsync(int questId, CancellationToken ct) =>
            throw new NotSupportedException();

        public ValueTask<IReadOnlyList<TrainingQuestAvailability>> TrainingAvailabilityAsync(
            CancellationToken ct) => throw new NotSupportedException();

        public QuestParameters Parameters() => throw new NotSupportedException();
    }

    private sealed class CapturedParameters : IExperienceParameterSource
    {
        public ExperienceParameters Load() => new(
            DifficultyRates: [1.3f, 2, 2.5f, 3, 4],
            PartyMemberRates: [1, 2, 3, 4],
            MonsterOverallRate: 1);
    }

    /// <summary>A channel that adds this much, or nothing at all.</summary>
    private sealed class FixedChannelBonus(double rate) : IExperienceBonusDomainService
    {
        public ValueTask<IReadOnlyList<ExperienceModifier>> CollectAsync(
            ExperienceOccasion occasion, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<ExperienceModifier>>(rate > 0
                ? [new ExperienceModifier("channel", rate, ExperienceBonusKind.SeriaBlessing)]
                : []);
    }

    /// <summary>
    /// A grant that prices at a fixed level and reports the valuation rather than storing it.
    /// </summary>
    /// <remarks>
    /// Level 3 because that is what the captured character was: every one of the four samples has
    /// the monster one or two levels above them, which is the band the level-gap rate of 1.12
    /// covers. Where the award lands afterwards belongs to the grant's own tests.
    /// </remarks>
    private sealed class PricingGrant(IExperienceCalculationDomainService calculation, byte characterLevel) : ExperienceCharacters
    {
        /// <summary>No test on this path asks what a level costs.</summary>
        public uint? ThresholdForLevel(byte level) => throw new NotSupportedException();


        public override async ValueTask<ExperienceAward?> AddExpAsync(ExperienceCalculationContext source, DateTimeOffset at, Dfo.Server.Data.World.SelectedSessionIdentity? session, CancellationToken cancellationToken)
        {
            var breakdown = await calculation.CalculateAsync(source, characterLevel, cancellationToken);
            return new ExperienceAward(breakdown, characterLevel, breakdown.Total, LeveledUp: false);
        }
    }
}
