using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Infrastructure.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

public sealed class PvfWorldDropTableSourceTests
{
    private static WorldDropEntry Resolve(int id) => new(id, 0, DropRarity.Common, DropItemKind.Stackable, 0);

    [Theory]
    [InlineData(new int[] { 1, 0, 3030, 5 })]
    [InlineData(new int[] { 1, 0, 3030 })]
    [InlineData(new int[] { 1, 1, -1 })]
    [InlineData(new int[] { 1, 0, -1, 1, 0, -1 })]
    [InlineData(new int[] { 1, 0, 0, 5, -1 })]
    [InlineData(new int[] { 1, 0, 3030, -5, -1 })]
    public void Malformed_or_unsupported_rows_fail(int[] values) =>
        Assert.Throws<InvalidDataException>(() => PvfWorldDropTableSource.Parse(values, Resolve));

    [Fact]
    public void Zero_rows_are_disabled_and_duplicate_items_remain_separate_intervals()
    {
        var row = Assert.Single(PvfWorldDropTableSource.Parse([1, 0, 0, 0, 999, 0, 3030, 2, 3030, 3, -1],
            id => id == 3030 ? Resolve(id) : throw new InvalidDataException()));
        Assert.Equal(new[] { 2, 3 }, row.Entries.Select(entry => entry.Weight));
    }

    [Fact]
    public void Missing_active_item_is_not_a_silent_empty_drop() =>
        Assert.Throws<InvalidDataException>(() => PvfWorldDropTableSource.Parse([1, 0, 999, 1, -1],
            _ => throw new InvalidDataException("missing item")));

    [SkippableFact]
    public void A21_archive_resolves_all_active_references_and_preserves_level_values()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var rows = new PvfWorldDropTableSource(provider).Load();
        Assert.Equal(200, rows.Count);
        Assert.Equal(8947, rows.Single(row => row.Level == 85).Entries.Sum(entry => entry.Weight));
        Assert.Equal(850, rows.Single(row => row.Level == 3).Entries.Single(entry => entry.ItemId == 3030).Weight);
        Assert.DoesNotContain(rows.Single(row => row.Level == 2).Entries, entry => entry.ItemId == 3030);
    }
}
