using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Tests.World;

public sealed class ExperienceCalculationDomainServiceTests
{
    [Fact]
    public async Task Named_flag_reaches_valuation_and_bonuses_use_the_named_base()
    {
        var valuation = new NamedWorth();
        var service = new ExperienceCalculationDomainService(valuation, new Bonuses());
        var result = await service.CalculateAsync(new KillExperienceContext(
            Occasion(ExperienceEvent.MonsterKill), 5, MonsterRank.Champion, 0, 1, 1, true), 3, default);
        Assert.True(valuation.Last.IsNamedMonster);
        Assert.Equal(MonsterRank.Champion, valuation.Last.Rank);
        Assert.Equal(new ExperienceBreakdown(Base: 225, Channel: 22, GrowthContract: 45, FatigueBurning: 225), result);
    }

    private sealed class NamedWorth : IExperienceValuationDomainService
    {
        public KillCircumstances Last { get; private set; }
        public uint WorthOfKill(in KillCircumstances kill) { Last = kill; return kill.IsNamedMonster ? 225u : 75u; }
        public ClearExperienceWorth WorthOfClear(in ClearCircumstances clear) => throw new NotSupportedException();
    }

    [Fact]
    public async Task EachSourceRoundsSeparatelyAndRetainsItsDisplayAmount()
    {
        var service = new ExperienceCalculationDomainService(new FixedWorth(), new Bonuses());
        var result = await service.CalculateAsync(new KillExperienceContext(
            Occasion(ExperienceEvent.MonsterKill), 5, MonsterRank.Ordinary, 0, 1, 1),
            3, CancellationToken.None);
        Assert.Equal(new ExperienceBreakdown(Base: 75, Channel: 6, GrowthContract: 15,
            FatigueBurning: 75), result);
        Assert.Equal(171u, result.Total);
    }

    [Fact]
    public async Task BlackDiamondUsesScoredBaseWithoutCompoundingOtherBonuses()
    {
        var service = new ExperienceCalculationDomainService(new FixedWorth(), new ClearBonuses());
        var clear = await service.CalculateAsync(new ClearExperienceContext(
            Occasion(ExperienceEvent.DungeonClear), new(5, 0, 1, 80)), 3, CancellationToken.None);
        var quest = await service.CalculateAsync(new QuestExperienceContext(
            Occasion(ExperienceEvent.QuestCompletion), 101), 3, CancellationToken.None);
        Assert.Equal(new ExperienceBreakdown(Base: 100, ClearRank: 20, GrowthContract: 20, SpiritPotion: 100, BlackDiamond: 6), clear);
        Assert.Equal(246u, clear.Total);
        Assert.Equal(new ExperienceBreakdown(Base: 101), quest);
    }

    [Fact]
    public async Task BlackDiamondFloorsFractionalScoredExperience()
    {
        var service = new ExperienceCalculationDomainService(new FixedWorth(101, 22), new ClearBonuses());
        var result = await service.CalculateAsync(new ClearExperienceContext(
            Occasion(ExperienceEvent.DungeonClear), new(5, 0, 1, 80)), 3, CancellationToken.None);
        Assert.Equal(6u, result.BlackDiamond);
    }

    [Fact]
    public void UnrepresentableRewardsFailRatherThanLosingPartsOfTheBreakdown()
    {
        Assert.Throws<OverflowException>(() => new ExperienceBreakdown(Base: uint.MaxValue,
            Channel: 1).Total);
    }

    private static ExperienceOccasion Occasion(ExperienceEvent kind) =>
        new(kind, new AccountId(1), new CharacterId(2), 144);
    private sealed class FixedWorth(uint clearBase = 100, uint rank = 20) : IExperienceValuationDomainService
    {
        public uint WorthOfKill(in KillCircumstances kill) => 75;
        public ClearExperienceWorth WorthOfClear(in ClearCircumstances clear) => new(clearBase, rank);
    }
    private sealed class ClearBonuses : IExperienceBonusDomainService
    {
        public ValueTask<IReadOnlyList<ExperienceModifier>> CollectAsync(ExperienceOccasion occasion, CancellationToken token)
        {
            Assert.Equal(ExperienceEvent.DungeonClear, occasion.Event);
            return ValueTask.FromResult<IReadOnlyList<ExperienceModifier>>([
                new("growth", .2, ExperienceBonusKind.GrowthContract),
                new("potion", 1, ExperienceBonusKind.SpiritPotion),
                new("black diamond", .05, ExperienceBonusKind.BlackDiamond)]);
        }
    }
    private sealed class Bonuses : IExperienceBonusDomainService
    {
        public ValueTask<IReadOnlyList<ExperienceModifier>> CollectAsync(
            ExperienceOccasion occasion, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<ExperienceModifier>>([
                new("channel", .05, ExperienceBonusKind.SeriaBlessing),
                new("another channel source", .05, ExperienceBonusKind.SeriaBlessing),
                new("growth", .2, ExperienceBonusKind.GrowthContract),
                new("burning", 1, ExperienceBonusKind.FatigueBurn)]);
    }
}
