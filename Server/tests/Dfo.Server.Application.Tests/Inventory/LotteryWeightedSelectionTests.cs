using System.Collections.Immutable;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class LotteryWeightedSelectionTests
{
    private sealed class Cache(OpeningPackageDefinition definition) : ILotteryDefinitionCache
    {
        public ValueTask<int> InitializeAsync(CancellationToken token = default) => ValueTask.FromResult(1);
        public OpeningPackageDefinition GetRequired(int id) => id == definition.ItemId ? definition
            : throw new OpeningRejectedException(OpeningRejection.WrongItem, $"Missing pot {id}");
    }
    private sealed class Ticket(long value) : ILotteryRandomSource
    {
        public int Calls;
        public long Bound;
        public long NextInt64(long bound) { Calls++; Bound = bound; return value; }
    }
    private static OpeningPackageDefinition Definition(ImmutableArray<long> ends) => new(7110, OpeningPackageKind.Lottery,
        [], [new(1, [new(new(100, 1), 0), new(new(101, 1), 2), new(new(102, 1), 0), new(new(103, 1), 3)])],
        0, 0, 0, 1, false, []) { CumulativeWeights = ends };

    [Theory]
    [InlineData(0, 101)] [InlineData(1, 101)] [InlineData(2, 103)] [InlineData(4, 103)]
    public void Half_open_ranges_skip_zero_weights(long ticket, int expected)
    {
        var random = new Ticket(ticket);
        var selector = new LotteryRuleService(new Cache(Definition([0, 2, 2, 5])), random);
        Assert.Equal(expected, selector.Roll(7110).ItemId);
        Assert.Equal(5, random.Bound);
        Assert.Equal(1, random.Calls);
    }

    [Fact]
    public void Every_integer_ticket_has_exactly_its_weight_in_outcomes()
    {
        var results = Enumerable.Range(0, 5).Select(i => new LotteryRuleService(
            new Cache(Definition([0, 2, 2, 5])), new Ticket(i)).Roll(7110).ItemId).ToArray();
        Assert.Equal(2, results.Count(x => x == 101));
        Assert.Equal(3, results.Count(x => x == 103));
    }

    [Fact]
    public void Missing_id_throws_before_randomness()
    {
        var random = new Ticket(0);
        var selector = new LotteryRuleService(new Cache(Definition([0, 2, 2, 5])), random);
        Assert.Contains("999", Assert.Throws<OpeningRejectedException>(() => selector.Roll(999)).Message);
        Assert.Equal(0, random.Calls);
    }

    [Fact]
    public void Invalid_or_empty_pool_throws_before_randomness()
    {
        foreach (var definition in new[] { Definition([]), Definition([0, 0, 0, 0]),
            Definition([0, 2, 2, 5]) with { Diagnostics = ["Negative weight"] } })
        {
            var random = new Ticket(0);
            Assert.Throws<OpeningRejectedException>(() => new LotteryRuleService(new Cache(definition), random).Roll(7110));
            Assert.Equal(0, random.Calls);
        }
    }

    [Fact]
    public void Total_can_exceed_int32()
    {
        long total = (long)int.MaxValue * 2;
        var random = new Ticket(total - 1);
        Assert.Equal(103, new LotteryRuleService(new Cache(Definition([0, int.MaxValue, int.MaxValue, total])), random).Roll(7110).ItemId);
        Assert.Equal(total, random.Bound);
    }
}
