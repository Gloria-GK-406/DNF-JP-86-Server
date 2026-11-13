using Dfo.Server.Testing;
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
        var service = new ExperienceCalculationDomainService(valuation, new Bonuses(), tower: TestRequired.Absent<Dfo.Server.Abstractions.World.IllusionTower.IIllusionTowerRewardRuleService>(), bossTower: TestRequired.Absent<Dfo.Server.Abstractions.World.BossTower.IBossTowerRewardRuleService>());
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
        var service = new ExperienceCalculationDomainService(new FixedWorth(), new Bonuses(), tower: TestRequired.Absent<Dfo.Server.Abstractions.World.IllusionTower.IIllusionTowerRewardRuleService>(), bossTower: TestRequired.Absent<Dfo.Server.Abstractions.World.BossTower.IBossTowerRewardRuleService>());
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
        var service = new ExperienceCalculationDomainService(new FixedWorth(), new ClearBonuses(), tower: TestRequired.Absent<Dfo.Server.Abstractions.World.IllusionTower.IIllusionTowerRewardRuleService>(), bossTower: TestRequired.Absent<Dfo.Server.Abstractions.World.BossTower.IBossTowerRewardRuleService>());
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
        var service = new ExperienceCalculationDomainService(new FixedWorth(101, 22), new ClearBonuses(), tower: TestRequired.Absent<Dfo.Server.Abstractions.World.IllusionTower.IIllusionTowerRewardRuleService>(), bossTower: TestRequired.Absent<Dfo.Server.Abstractions.World.BossTower.IBossTowerRewardRuleService>());
        var result = await service.CalculateAsync(new ClearExperienceContext(
            Occasion(ExperienceEvent.DungeonClear), new(5, 0, 1, 80)), 3, CancellationToken.None);
        Assert.Equal(6u, result.BlackDiamond);
    }

    [Fact]
    public async Task AdventureGroupIsItsOwnClearShareOfTheUnscoredBase()
    {
        var service = new ExperienceCalculationDomainService(new FixedWorth(101, 22), new AdventureBonuses(),
            tower: TestRequired.Absent<Dfo.Server.Abstractions.World.IllusionTower.IIllusionTowerRewardRuleService>(),
            bossTower: TestRequired.Absent<Dfo.Server.Abstractions.World.BossTower.IBossTowerRewardRuleService>());
        var clear = await service.CalculateAsync(new ClearExperienceContext(
            Occasion(ExperienceEvent.DungeonClear), new(5, 0, 1, 80)), 3, CancellationToken.None);

        // floor(101 x 0.2) = 20 growth, floor(101 x 0.1) = 10 adventure group; rank stays out of both.
        Assert.Equal(new ExperienceBreakdown(Base: 101, ClearRank: 22, GrowthContract: 20, AdventureGroup: 10), clear);
        Assert.Equal(153u, clear.Total);
    }

    [Theory]
    [InlineData(500_000u, 100_000u, 50_000u)]     // under the cap: the whole 10%
    [InlineData(900_000u, 180_000u, 20_000u)]     // adventure cut to what growth left under the cap
    [InlineData(1_000_000u, 100_000u, 0u)]        // growth fills the cap; adventure gets nothing
    public async Task BossTowerKeepsItsCapAndCutsTheAdventureGroupLast(uint bossBase, uint growth, uint adventure)
    {
        var service = new ExperienceCalculationDomainService(new FixedWorth(), new AdventureBonuses(),
            tower: TestRequired.Absent<Dfo.Server.Abstractions.World.IllusionTower.IIllusionTowerRewardRuleService>(),
            bossTower: TestRequired.Absent<Dfo.Server.Abstractions.World.BossTower.IBossTowerRewardRuleService>((method, _) =>
                method.Name == "ClearExperienceAsync" ? ValueTask.FromResult(bossBase) : null));
        var result = await service.CalculateAsync(new TowerClearExperienceContext(
            Occasion(ExperienceEvent.TowerClear), DungeonParticipationMode.BossTower, 1), 70, CancellationToken.None);

        Assert.Equal(bossBase, result.Base);
        Assert.Equal(growth, result.GrowthContract);
        Assert.Equal(adventure, result.AdventureGroup);
        Assert.True(result.Total <= 1_100_000u);
    }

    [Fact]
    public async Task AdventureGroupLeavesKillsAndIllusionTowersAlone()
    {
        var service = new ExperienceCalculationDomainService(new FixedWorth(), new AdventureBonuses(),
            tower: TestRequired.Absent<Dfo.Server.Abstractions.World.IllusionTower.IIllusionTowerRewardRuleService>((method, _) =>
                method.Name == "ClearExperienceAsync" ? ValueTask.FromResult(5000u) : null),
            bossTower: TestRequired.Absent<Dfo.Server.Abstractions.World.BossTower.IBossTowerRewardRuleService>());
        var kill = await service.CalculateAsync(new KillExperienceContext(
            Occasion(ExperienceEvent.MonsterKill), 5, MonsterRank.Ordinary, 0, 1, 1), 3, CancellationToken.None);
        var illusion = await service.CalculateAsync(new TowerClearExperienceContext(
            Occasion(ExperienceEvent.TowerClear), DungeonParticipationMode.IllusionTower, 1), 70, CancellationToken.None);

        Assert.Equal(0u, kill.AdventureGroup);
        Assert.Equal(new ExperienceBreakdown(Base: 5000), illusion);
    }

    /// <summary>What the bonus service hands a clear when the adventure group pays: it comes last.</summary>
    /// <remarks>The adventure group is only ever collected for a clear; a kill gets growth alone.</remarks>
    private sealed class AdventureBonuses : IExperienceBonusDomainService
    {
        public ValueTask<IReadOnlyList<ExperienceModifier>> CollectAsync(ExperienceOccasion occasion, CancellationToken token) =>
            ValueTask.FromResult<IReadOnlyList<ExperienceModifier>>(occasion.Event == ExperienceEvent.DungeonClear
                ? [new("growth", .2, ExperienceBonusKind.GrowthContract), new("adventure group", .1, ExperienceBonusKind.AdventureGroup)]
                : [new("growth", .2, ExperienceBonusKind.GrowthContract)]);
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
