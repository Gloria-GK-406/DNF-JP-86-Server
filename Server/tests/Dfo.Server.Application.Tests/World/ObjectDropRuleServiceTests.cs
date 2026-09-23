using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.World;

namespace Dfo.Server.Application.Tests.World;

public sealed class ObjectDropRuleServiceTests
{
    [Fact]
    public void Cumulative_weights_keep_miss_tail_and_zero_weight_entries()
    {
        ObjectDropEntry[] entries = [new(1, 2000, 0), new(2, 0, 0), new(3, 3000, 0)];
        var results = Enumerable.Range(0, 10000).Select(t => ObjectDropRuleService.Select(entries, t)?.ItemId).ToArray();
        Assert.Equal(2000, results.Count(i => i == 1));
        Assert.Equal(3000, results.Count(i => i == 3));
        Assert.Equal(5000, results.Count(i => i is null));
        Assert.DoesNotContain(2, results);
        Assert.Equal(1, ObjectDropRuleService.Select([new(1, 10000, 0)], 9999)!.ItemId);
    }

    [Fact]
    public void Repeated_attempts_produce_separate_items_and_replay_is_stable()
    {
        var service = new ObjectDropRuleService(null!, null!, null!, new());
        ObjectDropDefinition[] definitions = [new(4, 49014, 0, -1, 3, 0,
            [new(10, 5000, 25), new(20, 5001, 30)])];
        var context = Context(1);
        var result = service.Draw(definitions, context);
        Assert.Equal(3, result.Count);
        Assert.All(result, d => { Assert.Equal(1, d.Count); Assert.Equal(4, d.ObjectIndex); });
        Assert.Equal(result, service.Draw(definitions, context));
        var combinations = Enumerable.Range(0, 100).Select(seed =>
            string.Join(',', service.Draw(definitions, Context(seed)).Select(d => d.ItemId))).Distinct().Count();
        Assert.True(combinations > 1);
    }

    [Fact]
    public void Zero_attempts_and_empty_candidates_do_not_create_rewards()
    {
        var service = new ObjectDropRuleService(null!, null!, null!, new());
        Assert.Empty(service.Draw([new(0, 1, 0, -1, 0, 0, [new(10, 10001, 0)]),
            new(1, 2, 0, -1, 3, 0, [])], Context(1)));
    }

    internal static DungeonCellCreationContext Context(int seed) => new(DungeonRunId.New(),
        new DungeonLayout(1, 0, 1, 1, new(0, 0), new(0, 0), 1, seed,
            new Dictionary<MazeCell, int> { [new(0, 0)] = 1 }, BasisLevel: 20),
        new(0, 0), 1, 0, DateTimeOffset.UnixEpoch, []);

    [Fact]
    public void Object_random_table_uses_override_level_and_keeps_random_and_specific_attempts()
    {
        var source = new Sources();
        var service = new ObjectDropRuleService(source, source, source, new() { RateBasis = 10000 });
        var result = service.Draw([new(2, 1, 0, 30, 2, 3, [new(77, 10000, 0)])], Context(1));
        Assert.Equal(8, result.Count);
        Assert.Equal(3, result.Count(r => r.ItemId == RolledDrop.GoldItemId && r.Count == 100));
        Assert.Equal(3, result.Count(r => r.ItemId == 88 && r.Durability == 40 && r.Count == 1));
        Assert.Equal(2, result.Count(r => r.ItemId == 77 && r.Count == 1));
        Assert.All(result, r => Assert.Equal(2, r.ObjectIndex));
        Assert.Empty(service.Draw([new(0, 1, 0, -1, 0, 1, [])], Context(1)));
    }

    private sealed class Sources : IDropRateTableSource, IGoldDropTableSource, IDropItemPoolSource
    {
        public DropRateTable Load(string path)
        {
            Assert.Equal(DropRateTables.BreakableObject, path);
            return new(path, [new(30, 30, [10000, 0, 10000, 0, 0])],
                [new([1, 2]), new([1, 2]), new([1, 2]), new([1, 2])], [], [], [],
                new Dictionary<int, ItemGradeBand> { [30] = new(30, 30) });
        }

        GoldDropTable IGoldDropTableSource.Load(string path)
        {
            Assert.Equal(DropRateTables.BreakableObject, path);
            return new(new Dictionary<int, int> { [30] = 100 }, new Dictionary<int, int>(), [1], (100, 100));
        }

        public DropItemPool Load(DropItemFamily family) => new(family,
            new Dictionary<(int, int), IReadOnlyList<DropPoolEntry>> { [(30, 0)] = [new(88, 1, 40)] });
    }
}
