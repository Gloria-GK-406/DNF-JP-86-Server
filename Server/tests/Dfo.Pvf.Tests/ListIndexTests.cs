using Dfo.Pvf.Tests.Support;

namespace Dfo.Pvf.Tests;

public sealed class ListIndexTests
{
    [Fact]
    public void Preserves_rows_and_explicit_duplicate_policies_in_shared_read_only_views()
    {
        var builder = new PvfArchiveBuilder();
        builder.AddScript("items", "items.lst").Int(7).Quoted("a\\first.stk")
            .Int(7).Quoted("last.stk").Int(8).Quoted("other.stk").End();
        using var fixture = new SyntheticArchive(builder);
        var index = fixture.Archive.ReadList("items/items.lst");
        Assert.Same(index, fixture.Archive.ReadList("ITEMS/ITEMS.LST"));
        Assert.Equal(3, index.Entries.Count);
        Assert.Equal("a/first.stk", index.Entries[0].RelativePath);
        Assert.Throws<ArgumentException>(() => index.Paths());
        Assert.Throws<ArgumentException>(() => index.EntriesById());
        Assert.Equal("a/first.stk", index.Paths(PvfListDuplicates.First)[7]);
        Assert.Equal("last.stk", index.Paths(PvfListDuplicates.Last)[7]);
        var prefixed = index.Paths(PvfListDuplicates.First, "items/");
        Assert.Equal("items/a/first.stk", prefixed[7]);
        Assert.Same(prefixed, index.Paths(PvfListDuplicates.First, "items/"));
        Assert.Throws<NotSupportedException>(() => ((IDictionary<int, string>)prefixed).Add(9, "mutable"));
        Assert.Throws<NotSupportedException>(() => ((IList<(int, string)>)index.Entries).Clear());
    }

    [Fact]
    public async Task Concurrent_reads_share_one_index_and_views_without_crossing_archive_ownership()
    {
        var builder = new PvfArchiveBuilder();
        builder.AddScript("items", "items.lst").Int(7).Quoted("first.stk").End();
        using var first = new SyntheticArchive(builder);
        using var second = new SyntheticArchive(builder);
        var indexes = await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(() => first.Archive.ReadList("items/items.lst"))));
        Assert.All(indexes, index => Assert.Same(indexes[0], index));
        Assert.NotSame(indexes[0], second.Archive.ReadList("items/items.lst"));
        var views = await Task.WhenAll(indexes.Select(index => Task.Run(() => index.Paths(prefix: "items/"))));
        Assert.All(views, view => Assert.Same(views[0], view));
        Assert.Same(indexes[0].Paths(), indexes[0].Paths(PvfListDuplicates.First));
        Assert.Same(indexes[0].EntriesById(), indexes[0].EntriesById());
        first.Archive.Dispose();
        Assert.Throws<ObjectDisposedException>(() => first.Archive.ReadList("items/items.lst"));
    }
}
