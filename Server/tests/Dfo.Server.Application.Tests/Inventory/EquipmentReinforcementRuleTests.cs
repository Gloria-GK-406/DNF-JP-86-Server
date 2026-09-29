using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class EquipmentReinforcementRuleTests
{
    [Theory]
    [InlineData(0, 0, EquipmentReinforcementOutcome.Succeeded, 5)]
    [InlineData(10000, 9999, EquipmentReinforcementOutcome.Retained, 4)]
    [InlineData(10000, 10000, EquipmentReinforcementOutcome.Succeeded, 5)]
    [InlineData(100000, 99999, EquipmentReinforcementOutcome.Retained, 4)]
    public void Failure_threshold_is_strictly_greater_than_the_draw(
        int failureWeight, int roll, EquipmentReinforcementOutcome expected, int expectedLevel)
    {
        var decision = EquipmentReinforcementRules.Decide(
            new(4, failureWeight, 1, 0, null), roll);

        Assert.Equal(expected, decision.Outcome);
        Assert.Equal(expectedLevel, decision.NewLevel);
        Assert.False(decision.ConsumeProtection);
    }

    [Theory]
    [InlineData(1, 0, null, EquipmentReinforcementOutcome.Retained, 11, false)]
    [InlineData(2, 1, null, EquipmentReinforcementOutcome.Downgraded, 10, false)]
    [InlineData(3, 0, null, EquipmentReinforcementOutcome.Destroyed, 0, false)]
    [InlineData(3, 0, 10, EquipmentReinforcementOutcome.Downgraded, 10, true)]
    [InlineData(3, 0, 0, EquipmentReinforcementOutcome.Downgraded, 0, true)]
    public void Failure_penalty_and_protection_have_distinct_outcomes(
        int penaltyType, int penaltyValue, int? protectionRetain,
        EquipmentReinforcementOutcome expected, int level, bool consumed)
    {
        var decision = EquipmentReinforcementRules.Decide(
            new(11, 100000, penaltyType, penaltyValue, protectionRetain), 0);

        Assert.Equal(expected, decision.Outcome);
        Assert.Equal(level, decision.NewLevel);
        Assert.Equal(consumed, decision.ConsumeProtection);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(100001, 0)]
    [InlineData(1, -1)]
    [InlineData(1, 100000)]
    public void Invalid_weight_or_draw_never_silently_turns_into_a_result(int weight, int roll) =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            EquipmentReinforcementRules.Decide(new(4, weight, 1, 0, null), roll));
}
