using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Quests;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Protocol.Game.DungeonClear;

namespace Dfo.Server.Protocol.Tests.Game.DungeonClear;

/// <summary>
/// One finished run, priced and paid.
/// </summary>
/// <remarks>
/// The arithmetic is checked against the archive's tables next door. What only this can check is
/// the join: that the two figures the result screen reports are the same two the character was
/// paid, and that nothing else reaches the total on the way past.
/// </remarks>
public sealed class GrantClearExperienceCommandTests
{
    private const int CapturedDungeon = 144;

    /// <summary>Dungeon 144's weight, as the archive declares it and as a single.</summary>
    private const double CapturedDungeonWeight = 1.45f;

    /// <summary>What the clear pays is the clear's worth plus what its rank added.</summary>
    /// <remarks>
    /// Level 5 monster worth is 70; ten kills, divisor 2, difficulty 1.3 and weight 1.45 give
    /// 659 base EXP. Score 80 adds 12%, floored to 79.
    /// </remarks>
    [Fact]
    public async Task AClearPaysItsWorthAndItsRank()
    {
        var result = await Settle(clientRankPoint: 80);

        Assert.Equal(659u, result.ClearBase);
        Assert.Equal(79u, result.RankBonus);
        Assert.NotNull(result.Award);
        Assert.Equal(659u, result.Award.Base);
        Assert.Equal(738u, result.Award.Gained);
    }

    /// <summary>
    /// Everything paid is one of the two figures the result screen reports.
    /// </summary>
    /// <remarks>
    /// The constraint this whole settlement was scoped around. The frame has twenty-three further
    /// reward slots and none of them is measured in A21; an entitlement paid into the total but
    /// declared in none of them would leave the screen short by the difference, and nothing but a
    /// player's own arithmetic would notice. So the sum of what the screen reports and the sum of
    /// what was granted are asserted equal, rather than each being asserted against a figure.
    /// </remarks>
    [Theory]
    [InlineData(100)]
    [InlineData(80)]
    [InlineData(50)]
    [InlineData(0)]
    public async Task NothingIsPaidThatTheResultScreenCannotAccountFor(int clientRankPoint)
    {
        var result = await Settle(clientRankPoint);

        Assert.NotNull(result.Award);
        Assert.Equal(result.ClearBase + result.RankBonus, result.Award.Gained);
    }

    /// <summary>The rank's share is reported as its own line rather than folded into the worth.</summary>
    /// <remarks>
    /// It belongs to no named line of the experience notice, so the client draws it as part of what
    /// the clear itself paid -- which is what it is. The result screen reports the two apart, and
    /// this keeps the award able to say which was which.
    /// </remarks>
    [Fact]
    public async Task TheRanksShareIsNamedInTheAward()
    {
        var result = await Settle(clientRankPoint: 80);

        Assert.Equal(result.RankBonus, result.Award!.Breakdown.ClearRank);
    }

    /// <summary>A run below every paying rank is paid its worth and nothing more.</summary>
    [Fact]
    public async Task ARunBelowEveryPayingRankIsPaidItsWorthAlone()
    {
        var result = await Settle(clientRankPoint: 49);

        Assert.Equal(659u, result.ClearBase);
        Assert.Equal(0u, result.RankBonus);
        Assert.Equal(0u, result.Award!.Breakdown.ClearRank);
    }

    /// <summary>A dungeon the reward table cannot price pays nothing at all.</summary>
    [Fact]
    public async Task ADungeonTheTableCannotPricePaysNothing()
    {
        var result = await Settle(clientRankPoint: 100, basisLevel: 0);

        Assert.Equal(0u, result.ClearBase);
        Assert.Equal(0u, result.RankBonus);
        Assert.Equal(0u, result.Award!.Gained);
    }

    /// <summary>An award outside a transaction is refused rather than half-written.</summary>
    [Fact]
    public async Task AnAwardOutsideATransactionIsRefused()
    {
        var handler = Handler();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await handler.HandleAsync(
                Context(CommandExecutionKind.Direct), Command(80, 5), CancellationToken.None));
    }

    private static async Task<ClearExperienceResult> Settle(
        int clientRankPoint, int basisLevel = 5) =>
        await Handler().HandleAsync(
            Context(CommandExecutionKind.Transactional),
            Command(clientRankPoint, basisLevel),
            CancellationToken.None);

    private static GrantClearExperienceCommandHandler Handler() =>
        new(
            new ExperienceCalculationDomainService(new ExperienceValuationDomainService(new ExperienceRuleService(
                new ArchiveRates(),
                new ArchiveGrades(),
                new NoProgression(),
                new NoChannelExperience(),
                ExperienceTuning.Default,
                ExperienceEntitlementPolicy.None)), new NoBonuses()),
            new PricingGrant());

    private sealed class NoBonuses : IExperienceBonusDomainService
    {
        public ValueTask<IReadOnlyList<ExperienceModifier>> CollectAsync(ExperienceOccasion occasion, CancellationToken token) =>
            ValueTask.FromResult<IReadOnlyList<ExperienceModifier>>([]);
    }

    private static CommandExecutionContext Context(CommandExecutionKind kind) =>
        new(Guid.Empty,
            typeof(GrantClearExperienceCommand),
            typeof(GrantClearExperienceCommandHandler),
            kind);

    private static GrantClearExperienceCommand Command(int clientRankPoint, int basisLevel) =>
        new(new AccountId(1),
            new CharacterId(60),
            CapturedDungeon,
            new ClearCircumstances(
                basisLevel, Difficulty: 0, CapturedDungeonWeight, (byte)clientRankPoint, KilledMonsterCount: 10));

    /// <summary>A settlement asks what a clear pays, never what a quest is.</summary>
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

    private sealed class NoQuestDefinitions : IQuestDefinitionSource
    {
        public ValueTask<IReadOnlyList<QuestDefinition>> GetAllAsync(
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<QuestDefinition?> GetAsync(
            int questId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<TrainingQuestAvailability>> GetTrainingAvailabilityAsync(
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class ArchiveQuests : IQuestParameterSource
    {
        public QuestParameters Load() => new(
            ExperienceByLevel: [100, 100, 126, 187, 293, 456, 683],
            DifficultyPermille: new Dictionary<char, int> { ['M'] = 10 },
            GreenPenaltyPercent: 100,
            GreyPenaltyPercent: 100,
            EpicGreenPenaltyPercent: 100,
            EpicGreyPenaltyPercent: 100);
    }

    private sealed class ArchiveRates : IExperienceParameterSource
    {
        public ExperienceParameters Load() => new(
            DifficultyRates: [1.3f, 2, 2.5f, 3, 4],
            PartyMemberRates: [1, 2, 3, 4],
            MonsterOverallRate: 1,
            ClearOverallRate: 1,
            ClearRankRates: [0.05, 0.1, 0.12, 0.15, 0.2]);
    }

    private sealed class ArchiveGrades : IDungeonRankGradeSource
    {
        public DungeonRankGrades Load() => new([99, 90, 80, 60, 50, 30, 20, 10]);
    }

    /// <summary>
    /// A grant that reports the valuation rather than storing it.
    /// </summary>
    /// <remarks>
    /// Where the award lands afterwards belongs to the grant's own tests. The level it prices at is
    /// irrelevant here and is passed as such: unlike a kill, nothing about a clear's worth depends
    /// on who cleared it.
    /// </remarks>
    private sealed class PricingGrant : ICharacterExperienceDomainService
    {
        /// <summary>No test on this path asks what a level costs.</summary>
        public uint? ThresholdForLevel(byte level) => throw new NotSupportedException();

        /// <summary>Pricing stand-ins never set progress outright; the tutorial is the only path that does.</summary>
        public ValueTask<bool> TrySetProgressAsync(
            CharacterId characterId, byte level, uint experience, CancellationToken cancellationToken) =>
            throw new NotSupportedException("This stand-in prices awards; it does not set progress.");

        public async ValueTask<ExperienceAward?> AwardAsync(
            CharacterId characterId,
            Func<byte, ValueTask<ExperienceBreakdown>> valuation,
            CancellationToken cancellationToken)
        {
            var breakdown = await valuation(1);
            return new ExperienceAward(breakdown, 1, breakdown.Total, LeveledUp: false);
        }
    }
}
