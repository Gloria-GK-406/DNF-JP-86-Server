using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Inventory;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class EquipmentReinforcementFailureRuleTests
{
    [Theory]
    [InlineData(200, 33)]
    [InlineData(30, 5)]
    public void Base_cube_count_obeys_value_adjustment_sale_rate_and_integer_truncation(int rate, int count)
    {
        var table = Table();
        var rewards = new List<EquipmentReinforcementFailureReward>();
        Assert.True(EquipmentReinforcementFailureRuleService.TryBaseRewards(
            new(20520, 57, 0, rate), table, new(null, 0, 0), rewards));
        Assert.Equal(new EquipmentReinforcementFailureReward(3037, count), Assert.Single(rewards));
    }

    [Theory]
    [InlineData(0, 4999, 3033, 28)]
    [InlineData(2500, 5000, 3034, 14)]
    [InlineData(5000, 9999, 3035, 14)]
    [InlineData(9999, 0, 3036, 28)]
    public void Additional_reward_uses_equipment_grade_and_truncates_without_npc_fraction_or_expand(
        int choice, int great, int itemId, int count)
    {
        var rewards = new List<EquipmentReinforcementFailureReward>();
        Assert.True(EquipmentReinforcementFailureRuleService.TryBaseRewards(
            new(20520, 57, 1, 200), Table(), new(null, choice, great), rewards));
        Assert.Equal(2, rewards.Count);
        Assert.Equal(new EquipmentReinforcementFailureReward(itemId, count), rewards[1]);
        Assert.DoesNotContain(rewards, reward => reward.ItemId == 10100115);
    }

    [Fact]
    public void Additional_count_rounds_the_float32_temporary_before_integer_truncation()
    {
        var table = Table() with { AdditionalRules = [new(1, 1, 0, 100), new(.3, .3, 0, 100)] };
        var rewards = new List<EquipmentReinforcementFailureReward>();
        Assert.True(EquipmentReinforcementFailureRuleService.TryBaseRewards(
            new(1000, 57, 1, 200), table, new(null, 0, 9999), rewards));
        Assert.Equal(190, rewards[1].Count);
    }

    [Fact]
    public void Float32_quantity_outside_int32_capacity_is_rejected_instead_of_turning_into_one()
    {
        var table = Table() with { AdditionalRules = [new(1, 1, 0, 100), new(1, 1, 0, 100)] };
        var rewards = new List<EquipmentReinforcementFailureReward>();
        Assert.False(EquipmentReinforcementFailureRuleService.TryBaseRewards(
            new(1000, int.MaxValue, 1, 200), table, new(null, 0, 9999), rewards));
    }

    [Fact]
    public void Fractional_great_chance_is_not_silently_treated_as_the_70_integer_percent_field()
    {
        var table = Table() with { AdditionalRules = [new(1, 1, 0, 100), new(2, 4, 1.5, 98.5)] };
        var rewards = new List<EquipmentReinforcementFailureReward>();
        Assert.False(EquipmentReinforcementFailureRuleService.TryBaseRewards(
            new(1000, 57, 1, 200), table, new(null, 0, 0), rewards));
    }

    [Theory]
    [InlineData(10, 0)]
    [InlineData(11, 1)]
    [InlineData(12, 4)]
    public async Task Reinforcement_bonus_uses_original_level(byte oldLevel, int count)
    {
        var service = new EquipmentReinforcementFailureRuleService(new Content(oldLevel), new Content(oldLevel),
            new ReinforcementDestructionTestContent(2), NullLogger<EquipmentReinforcementFailureRuleService>.Instance);
        var result = await service.GetResultAsync(Facts(oldLevel), new(null, 0, 0), default);
        Assert.Equal(EquipmentReinforcementOutcome.Destroyed, result.Outcome);
        Assert.Equal(count, result.Rewards.SingleOrDefault(reward => reward.ItemId == 3243)?.Count ?? 0);
    }

    [Theory]
    [InlineData(EquipmentReinforcementProtectionKind.ResetToZero, 0)]
    [InlineData(EquipmentReinforcementProtectionKind.RetainLevel, 10)]
    public async Task Protected_failure_plans_no_rewards_and_does_not_read_disjoint_content(
        EquipmentReinforcementProtectionKind kind, byte level)
    {
        var service = new EquipmentReinforcementFailureRuleService(new Content(11), new Content(11),
            new NoDisjointReads(), NullLogger<EquipmentReinforcementFailureRuleService>.Instance);
        var result = await service.GetResultAsync(Facts(11), new(new(kind, level), 0, 0), default);
        Assert.Equal(level, result.NewLevel);
        Assert.True(result.ConsumeProtection);
        Assert.Empty(result.Rewards);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(-1)]
    [InlineData(double.PositiveInfinity)]
    public void Invalid_cube_weight_does_not_produce_a_partial_material_result(double weight)
    {
        var rewards = new List<EquipmentReinforcementFailureReward>();
        Assert.False(EquipmentReinforcementFailureRuleService.TryBaseRewards(new(1000, 50, 0, 200),
            Table() with { CubeCreationMultipliers = [weight, 1.35] }, new(null, 0, 0), rewards));
        Assert.Empty(rewards);
    }

    [Theory]
    [InlineData(1, 0, EquipmentReinforcementOutcome.Retained, 7)]
    [InlineData(2, 2, EquipmentReinforcementOutcome.Downgraded, 5)]
    [InlineData(3, 0, EquipmentReinforcementOutcome.Destroyed, 0)]
    public async Task Medal_failure_uses_its_own_penalty_and_only_refunds_certificates_after_destruction(
        int penalty, int penaltyValue, EquipmentReinforcementOutcome outcome, byte level)
    {
        const int certificate = 10154439;
        var source = new Content(7)
        {
            MedalRow = new(8, [], 50_000, penalty, penaltyValue, certificate, 20),
        };
        var service = new EquipmentReinforcementFailureRuleService(source, source, new NoDisjointReads(),
            NullLogger<EquipmentReinforcementFailureRuleService>.Instance);
        var medal = TestItemReferences.FromCore(0, StackableItemCore.Create(12, 100380044, 123) with { Attribute = 7 },
            ItemWorldClass.Equipment).WithBusinessSlot(new(BusinessInventorySpace.GuildMedal, 0));
        var result = await service.GetResultAsync(medal, new(null, 0, 0), default);

        Assert.Equal(EquipmentReinforcementRejection.None, result.Rejection);
        Assert.Equal(outcome, result.Outcome);
        Assert.Equal(level, result.NewLevel);
        Assert.False(result.ConsumeProtection);
        if (outcome == EquipmentReinforcementOutcome.Destroyed)
            Assert.Equal(new EquipmentReinforcementFailureReward(certificate, 3), Assert.Single(result.Rewards));
        else
            Assert.Empty(result.Rewards);
    }

    private static ItemReference Facts(byte level) =>
        TestItemReferences.FromCore(0, StackableItemCore.Create(1, 3000, 123) with { Attribute = level }, ItemWorldClass.Equipment).WithBusinessSlot(new(BusinessInventorySpace.CarriedEquipment, 0));

    private static EquipmentDisjointTable Table() => new(3037, 150, [1.12, 1.35],
        [[], [3033, 3034, 3035, 3036]], [new(2, 4, 50, 50), new(2, 4, 50, 50)],
        [new(true, 10100115, 1, 100, 0)]);

    private sealed class Content(byte oldLevel) : IEquipmentReinforcementDefinitionSource, IEquipmentDefinitionSource
    {
        public EquipmentReinforcementRow? MedalRow { get; init; }

        public async ValueTask<EquipmentReinforcementTable?> GetGuildMedalTableAsync(CancellationToken ct) =>
            MedalRow is { } row && await GetNormalTableAsync(ct) is { } normal
                ? normal with { Rows = [row], MedalRefunds = [0, 0, 0, 3] }
                : null;

        public ValueTask<EquipmentReinforcementTable?> GetAmplificationTableAsync(CancellationToken ct) =>
            ValueTask.FromResult<EquipmentReinforcementTable?>(null);

        public ValueTask<EquipmentReinforcementTable?> GetNormalTableAsync(CancellationToken ct) =>
            ValueTask.FromResult<EquipmentReinforcementTable?>(new([new(oldLevel + 1, [], 100_000, 3, 0, 3037, 10)],
                [], [], [], new Dictionary<int, double>(), new Dictionary<int, int>(), new Dictionary<int, int>(),
                new(3243, 40, 10, new Dictionary<int, double> { [2] = 1 }, new Dictionary<int, int> { [2] = 2 }), 12));
        public ValueTask<int> InitializeAsync(CancellationToken ct) => ValueTask.FromResult(0);

        public ValueTask<EquipmentDefinition?> GetEquipmentAsync(int itemId, CancellationToken ct) =>
            ValueTask.FromResult<EquipmentDefinition?>(MedalRow is null ? new(1, 50, 2, "weapon", 45) : new(19, 50, 3, "flag", 0));
        public ValueTask<EquipmentReinforcementProtectionDefinition?> GetProtectionAsync(int itemId, CancellationToken ct, EquipmentUpgradeKind kind = EquipmentUpgradeKind.Reinforcement) =>
            throw new NotSupportedException("Protection is an explicit fact, not queried by failure planning.");
    }

    private sealed class NoDisjointReads : IEquipmentDisjointDefinitionSource
    {
        public ValueTask<EquipmentDestructionDefinition?> GetDestructionEquipmentAsync(int itemId, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<EquipmentDisjointDefinition?> GetEquipmentAsync(int itemId, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<PortableDisjointMachineDefinition?> GetPortableMachineAsync(int itemId, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<EquipmentDisjointTable?> GetSystemTableAsync(CancellationToken ct) => throw new NotSupportedException();
    }
}
