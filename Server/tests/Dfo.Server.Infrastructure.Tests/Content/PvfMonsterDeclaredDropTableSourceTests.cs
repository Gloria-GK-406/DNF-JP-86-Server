using Dfo.Server.Abstractions.World;
using Dfo.Server.Infrastructure.Content;
namespace Dfo.Server.Infrastructure.Tests.Content;
public sealed class PvfMonsterDeclaredDropTableSourceTests
{
    private static RolledDrop Resolve(int id) => new(id, 1, DropRarity.Common, DropItemKind.Stackable);
    [Theory]
    [InlineData(new int[] { 1 })]
    [InlineData(new int[] { 1, -1 })]
    [InlineData(new int[] { 1, int.MaxValue, 2, 1 })]
    public void Invalid_distribution_fails(int[] values) =>
        Assert.Throws<InvalidDataException>(() => PvfMonsterDeclaredDropTableSource.Parse(1, values, Resolve));
    [Fact]
    public void Missing_active_item_fails() => Assert.Throws<InvalidDataException>(() =>
        PvfMonsterDeclaredDropTableSource.Parse(1, [99, 10], _ => throw new InvalidDataException()));
    [Fact]
    public void Over_basis_weights_are_preserved()
    {
        var row = PvfMonsterDeclaredDropTableSource.Parse(1, [3170, 7000, 3213, 5000], Resolve);
        Assert.Equal(new[] { 7000, 5000 }, row.Entries.Select(e => e.Weight));
    }
    [SkippableFact]
    public void A21_archive_loads_and_preserves_goblin_intervals()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var rows = new PvfMonsterDeclaredDropTableSource(provider).Load();
        var goblin = Assert.Single(rows, row => row.MonsterId == 1);
        Assert.Equal(new[] { 50, 200, 100 }, goblin.Entries.Select(e => e.Weight));
        Assert.Equal(new[] { 1000, 1047, 1004 }, goblin.Entries.Select(e => e.Item.ItemId));
    }
}
