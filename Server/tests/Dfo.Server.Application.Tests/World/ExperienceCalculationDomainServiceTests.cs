using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Tests.World;

public sealed class ExperienceCalculationDomainServiceTests
{
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
    public async Task ClearAndQuestKeepTheirExistingAmountsWithoutEnablingNewBonuses()
    {
        var service = new ExperienceCalculationDomainService(new FixedWorth(), null!);
        var clear = await service.CalculateAsync(new ClearExperienceContext(
            Occasion(ExperienceEvent.DungeonClear), new(5, 0, 1, 80)), 3, CancellationToken.None);
        var quest = await service.CalculateAsync(new QuestExperienceContext(
            Occasion(ExperienceEvent.QuestCompletion), 101), 3, CancellationToken.None);
        Assert.Equal(new ExperienceBreakdown(Base: 100, ClearRank: 20), clear);
        Assert.Equal(new ExperienceBreakdown(Base: 101), quest);
    }

    [Fact]
    public void UnrepresentableRewardsFailRatherThanLosingPartsOfTheBreakdown()
    {
        Assert.Throws<OverflowException>(() => new ExperienceBreakdown(Base: uint.MaxValue,
            Channel: 1).Total);
    }

    private static ExperienceOccasion Occasion(ExperienceEvent kind) =>
        new(kind, new AccountId(1), new CharacterId(2), 144);
    private sealed class FixedWorth : IExperienceValuationDomainService
    {
        public uint WorthOfKill(in KillCircumstances kill) => 75;
        public ClearExperienceWorth WorthOfClear(in ClearCircumstances clear) => new(100, 20);
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
