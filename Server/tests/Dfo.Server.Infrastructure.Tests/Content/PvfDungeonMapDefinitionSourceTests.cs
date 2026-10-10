using Dfo.Server.Abstractions.Content;
using Dfo.Server.Infrastructure.Content;
using Dfo.Pvf.Tests.Support;

namespace Dfo.Server.Infrastructure.Tests.Content;

public sealed class PvfDungeonMapDefinitionSourceTests
{
    [SkippableTheory]
    [InlineData(4602, 17, DungeonPathDirection.Up | DungeonPathDirection.Down, DungeonPathDirection.Down)]
    [InlineData(3408, 23, DungeonPathDirection.Right | DungeonPathDirection.Down, DungeonPathDirection.Right)]
    public async Task Map_connections_after_tile_and_actor_blocks_are_read(
        int mapId, int owner, DungeonPathDirection pattern, DungeonPathDirection entrance)
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var map = await new PvfDungeonMapDefinitionSource(provider).GetAsync(mapId, default);
        Assert.NotNull(map);
        Assert.Contains(owner, map.DungeonIds);
        Assert.Equal(new DungeonMapConnection(pattern, entrance), Assert.Single(map.Connections));
    }

    [Fact]
    public async Task Root_metadata_after_content_blocks_is_read_without_leaking_nested_metadata()
    {
        var builder = new PvfArchiveBuilder();
        builder.AddScript("map", "map.lst").Int(1).Quoted("test.map").End();
        builder.AddScript("map", "test.map")
            .Tag("[tile]").Quoted("tile.til").Tag("[/tile]")
            .Tag("[monster]").Tag("[dungeon]").Int(999).Tag("[/dungeon]")
            .Tag("[type]").Quoted("[boss]").Tag("[greed]").Quoted("PP PP")
            .Tag("[dungeon start area]").Int(1).Int(2).Int(3).Int(4).Tag("[/monster]")
            .Tag("[dungeon]").Int(23).Tag("[/dungeon]")
            .Tag("[type]").Quoted("[normal]").Tag("[greed]").Quoted("JJ BB").End();
        var path = Path.Combine(Path.GetTempPath(), $"pvf-map-metadata-{Guid.NewGuid():N}.pvf");
        try
        {
            File.WriteAllBytes(path, builder.Build());
            using var provider = new PvfArchiveProvider(new PvfContentOptions { ArchivePath = path });
            var source = new PvfDungeonMapDefinitionSource(provider);
            var map = await source.GetAsync(1, default);
            Assert.NotNull(map);
            Assert.Equal([23], map.DungeonIds);
            Assert.Equal(DungeonMapKind.Normal, map.Kind);
            Assert.False(map.HasStartArea);
            Assert.Equal(new DungeonMapConnection(DungeonPathDirection.Right | DungeonPathDirection.Down,
                DungeonPathDirection.Right), Assert.Single(map.Connections));
            Assert.Empty(await source.ListForDungeonAsync(999, default));
            Assert.Single(await source.ListForDungeonAsync(23, default));
        }
        finally { File.Delete(path); }
    }

    [SkippableFact]
    public async Task Historical_spider_start_map_uses_dungeon_start_area_marker()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfDungeonMapDefinitionSource(provider);
        var map = await source.GetAsync(4401, default);
        Assert.NotNull(map);
        Assert.True(map.HasStartArea);
        Assert.Contains(32, map.DungeonIds);
        Assert.Equal(DungeonPathDirection.Right, Assert.Single(map.Connections).Pattern);
    }

    [SkippableFact]
    public async Task Real_map_metadata_separates_pattern_and_entrance_and_indexes_ownership()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfDungeonMapDefinitionSource(provider);
        var map = await source.GetAsync(15305, default);
        Assert.NotNull(map);
        Assert.Contains(77, map.DungeonIds);
        Assert.Equal(DungeonMapKind.Normal, map.Kind);
        Assert.Equal(new DungeonMapConnection(DungeonPathDirection.Right | DungeonPathDirection.Left,
            DungeonPathDirection.Right | DungeonPathDirection.Left), Assert.Single(map.Connections));
        var pool = await source.ListForDungeonAsync(77, default);
        Assert.Contains(pool, value => value.MapId == 15305);
        Assert.Contains(pool, value => value.MapId == 15310);
        Assert.Contains(pool, value => value.MapId == 15269 && value.Kind == DungeonMapKind.Boss);
        Assert.All(pool, value => Assert.Contains(77, value.DungeonIds));
    }
}
