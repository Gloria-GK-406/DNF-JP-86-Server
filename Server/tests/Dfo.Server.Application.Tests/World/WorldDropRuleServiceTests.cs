using System.Collections.Immutable;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Tests.World;

public sealed class WorldDropRuleServiceTests
{
    private static WorldDropEntry Item(int id, int weight) => new(id, weight, DropRarity.Uncommon, DropItemKind.Stackable, 0);

    [Fact]
    public void Every_ticket_matches_its_interval_or_the_empty_tail()
    {
        var distribution = WorldDropDistribution.Compile(new(20, [Item(1, 2), Item(2, 0), Item(3, 3)]), 10);
        Assert.Equal(new int?[] { 1, 1, 3, 3, 3, null, null, null, null, null },
            Enumerable.Range(0, 10).Select(ticket => distribution.Select(ticket)?.ItemId));
        Assert.Throws<ArgumentOutOfRangeException>(() => distribution.Select(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => distribution.Select(10));
    }

    [Fact]
    public void Invalid_weights_and_overfull_tables_fail_instead_of_normalizing()
    {
        Assert.Throws<InvalidDataException>(() => WorldDropDistribution.Compile(new(20, [Item(1, -1)]), 10));
        Assert.Throws<InvalidDataException>(() => WorldDropDistribution.Compile(new(20, [Item(1, 11)]), 10));
        Assert.Null(WorldDropDistribution.Compile(new(20, []), 10).Select(0));
    }

    [Fact]
    public void Cache_is_eager_idempotent_and_draws_are_level_scoped_and_replayable()
    {
        var source = new Source();
        var service = new WorldDropRuleService(source, new WorldDropOptions { ProbabilityBasis = 10 });
        var character = new CharacterId(1);
        var context = new MonsterDropContext(DungeonRunId.New(), 1, 123, new MazeCell(0, 0), 1,
            0, 30001, 20, 0, character, [new(new AccountId(1), character)]);
        var request = new MonsterDropRequest(17, context);
        Assert.Throws<InvalidOperationException>(() => service.Draw(request));
        Assert.Equal(1, service.Initialize());
        Assert.Equal(1, service.Initialize());
        var first = Assert.Single(service.Draw(request));
        Assert.Equal(first, Assert.Single(service.Draw(request)));
        Assert.Equal(1, first.Count);
        Assert.Equal(DropRarity.Uncommon, first.Rarity);
        Assert.Empty(service.Draw(request with { Context = context with { MonsterLevel = 21 } }));
        Assert.Equal(1, source.Reads);
    }

    private sealed class Source : IWorldDropTableSource
    {
        public int Reads { get; private set; }
        public IReadOnlyList<WorldDropLevel> Load()
        {
            Reads++;
            return [new(20, [Item(3030, 10)])];
        }
    }
}
