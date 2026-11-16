using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.World;

namespace Dfo.Server.Application.Tests.World;

[Trait("Category", "Unit")]
public sealed class DropMaterializationRuleTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void Distinct_capabilities_preserve_the_ordered_prefix_and_report_discarded_items(int count)
    {
        var rule = new DropMaterializationRuleService(new(2, 3, 4, 8)).Rule;
        var drops = Enumerable.Range(0, count).Select(i => new RolledDrop(1000 + i, 1,
            DropRarity.Common, DropItemKind.Stackable)).ToArray();
        var initial = rule.SelectInitial(drops.Select((drop, i) => new GroundDrop((ushort)(i + 1),
            new(0, drop.ItemId, 0))).ToArray());
        var death = rule.SelectDeath(drops);
        var independent = rule.SelectIndependent(drops);
        Assert.Equal(drops.Take(2).Select(x => x.ItemId), initial.Items.Select(x => x.Drop.ItemId));
        Assert.Equal(drops.Take(3), death.Items);
        Assert.Equal(drops.Take(4), independent.Items);
        Assert.Equal(new(count, Math.Min(count, 2)), initial.Count);
        Assert.Equal(new(count, Math.Min(count, 3)), death.Count);
        Assert.Equal(new(count, Math.Min(count, 4)), independent.Count);
        Assert.Equal(count - Math.Min(count, 3), death.Count.Discarded);
        Assert.Equal(death.Count, rule.InspectFrozenDeath(count));
        if (count > 0) drops[0] = drops[0] with { ItemId = 9999 };
        Assert.DoesNotContain(independent.Items, drop => drop.ItemId == 9999);
    }

    [Theory]
    [InlineData(1, 4, 4)]
    [InlineData(7, 4, 2)]
    [InlineData(8, 4, 1)]
    [InlineData(9, 4, 0)]
    [InlineData(1, 0, 0)]
    public void Number_allocation_is_bounded_by_the_explicit_last_number(int next, int requested, int expected)
    {
        var rule = new DropMaterializationRuleService(new(2, 3, 4, 8)).Rule;
        Assert.Equal(expected, rule.AdmitGroundItemNumbers(next, requested));
    }
}
