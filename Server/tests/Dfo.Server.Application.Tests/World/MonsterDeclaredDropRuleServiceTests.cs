using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.World;
namespace Dfo.Server.Application.Tests.World;
public sealed class MonsterDeclaredDropRuleServiceTests
{
    private static readonly MonsterDeclaredDropTable Table = new(1, [
        new(new(1000, 1, DropRarity.Common, DropItemKind.Stackable), 50),
        new(new(1047, 1, DropRarity.Common, DropItemKind.Stackable), 200),
        new(new(1004, 1, DropRarity.Common, DropItemKind.Stackable), 100)]);
    [Theory]
    [InlineData(0, 1000)] [InlineData(49, 1000)]
    [InlineData(50, 1047)] [InlineData(249, 1047)]
    [InlineData(250, 1004)] [InlineData(349, 1004)]
    [InlineData(350, null)] [InlineData(9999, null)]
    public void Fixed_basis_has_exact_boundaries_and_no_drop_tail(int ticket, int? expected) =>
        Assert.Equal(expected, MonsterDeclaredDropRuleService.Select(Table, ticket)?.ItemId);
    [Fact]
    public void Exhaustive_tickets_preserve_absolute_probability()
    {
        var drops = Enumerable.Range(0, 10000).Select(t => MonsterDeclaredDropRuleService.Select(Table, t)).ToArray();
        Assert.Equal(9650, drops.Count(d => d is null));
        Assert.Equal(50, drops.Count(d => d?.ItemId == 1000));
        Assert.All(drops.Where(d => d.HasValue), d => Assert.Equal(1, d!.Value.Count));
    }
    [Fact]
    public void Over_basis_always_drops_exactly_one_preserving_relative_weights()
    {
        var table = new MonsterDeclaredDropTable(55021, [
            new(new(3170, 1, DropRarity.Common, DropItemKind.Stackable), 7000),
            new(new(3213, 1, DropRarity.Common, DropItemKind.Stackable), 5000)]);
        Assert.Equal(12000, MonsterDeclaredDropRuleService.Basis(table));
        var results = Enumerable.Range(0, 12000).Select(t => MonsterDeclaredDropRuleService.Select(table, t)).ToArray();
        Assert.All(results, result => { Assert.True(result.HasValue); Assert.Equal(1, result.Value.Count); });
        Assert.Equal(7000, results.Count(d => d?.ItemId == 3170));
        Assert.Equal(5000, results.Count(d => d?.ItemId == 3213));
    }
    [Fact]
    public void Cache_loads_once_and_party_size_does_not_change_draws()
    {
        var source = new Source();
        var service = new MonsterDeclaredDropRuleService(source);
        var character = new Dfo.Server.Data.Characters.CharacterId(1);
        MonsterDropParticipant first = new(new Dfo.Server.Data.Accounts.AccountId(1), character);
        MonsterDropParticipant second = new(new Dfo.Server.Data.Accounts.AccountId(2), new(2));
        var context = new MonsterDropContext(DungeonRunId.New(), 1, 123, new MazeCell(0, 0), 1,
            0, 30001, 20, 0, character, [first]);
        Assert.Throws<InvalidOperationException>(() => service.Draw(new(1, context)));
        service.Initialize();
        service.Initialize();
        for (var seed = 0; seed < 500; seed++)
        {
            var solo = service.Draw(new(1, context with { Seed = seed }));
            var party = service.Draw(new(1, context with { Seed = seed, Participants = [first, second] }));
            Assert.Equal(solo, party);
            Assert.InRange(solo.Count, 0, 1);
        }
        Assert.Empty(service.Draw(new(999, context)));
        Assert.Equal(1, source.Reads);
    }
    private sealed class Source : IMonsterDeclaredDropTableSource
    {
        public int Reads { get; private set; }
        public IReadOnlyList<MonsterDeclaredDropTable> Load() { Reads++; return [Table]; }
    }
}
