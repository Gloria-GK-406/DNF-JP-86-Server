using Dfo.Server.Abstractions.Content;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Tests.Inventory;

[Trait("Category", "Unit")]
public sealed class EquipmentForgingRulesTests
{
    [Theory]
    [InlineData(0, 0, true, 1, 39)]
    [InlineData(1, 4999, true, 2, 43)]
    [InlineData(1, 5000, false, 1, 43)]
    public void Pvf_weight_and_roll_determine_cost_and_level(
        byte oldLevel, int roll, bool success, byte newLevel, int materialCount)
    {
        var result = EquipmentForgingRules.Decide(Table(), Weapon(), oldLevel, 3326, roll);
        Assert.Equal(EquipmentForgingRejection.None, result.Rejection);
        Assert.Equal(3326, result.MaterialItemId);
        Assert.Equal(materialCount, result.MaterialCount);
        Assert.Equal(success, result.Succeeded);
        Assert.Equal(newLevel, result.NewLevel);
    }

    [Fact]
    public void Material_identity_and_max_level_reject_before_a_roll()
    {
        Assert.Equal(EquipmentForgingRejection.MaterialUnavailable,
            EquipmentForgingRules.Decide(Table(), Weapon(), 0, 3333, 0).Rejection);
        Assert.Equal(EquipmentForgingRejection.MaxLevel,
            EquipmentForgingRules.Decide(Table(), Weapon(), 8, 3326, 0).Rejection);
    }

    private static EquipmentDefinition Weapon() =>
        new(75, 77, 2, "weapon", 45);

    private static EquipmentForgingTable Table() => new(8,
        [new(1, 10000, 1), new(2, 5000, 1.11),
         new(3, 5000, 1.21), new(4, 3000, 1.32),
         new(5, 3000, 1.42), new(6, 1500, 1.53),
         new(7, 1500, 1.63), new(8, 750, 1.74)],
        new Dictionary<int, EquipmentForgingMaterial> { [77] = new(3326, 39) },
        [0.4, 0.7, 1, 1.25, 1.4, 1.1, 1.3]);
}
