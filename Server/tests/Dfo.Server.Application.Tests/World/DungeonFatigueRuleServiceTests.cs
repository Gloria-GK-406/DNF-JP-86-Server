using Dfo.Server.Application.World;

namespace Dfo.Server.Application.Tests.World;

[Trait("Category", "Unit")]
public sealed class DungeonFatigueRuleServiceTests
{
    [Theory]
    [InlineData(null, 3, false)]
    [InlineData(3, 3, false)]
    [InlineData(4, 4, true)]
    public void Charge_rounds_after_composition_and_requires_enough_fatigue_for_the_full_charge(
        int? remaining, ushort points, bool requireFullCharge)
    {
        var rule = new DungeonFatigueRuleService().Quote(1,
            [new(1.5m), new(1.5m), new(1.5m, RequireFullCharge: true)]);

        var charge = rule.Resolve(new(1), (ushort?)remaining);

        Assert.Equal(points, charge.Points);
        Assert.Equal(requireFullCharge, charge.RequireFullCharge);
    }
}
