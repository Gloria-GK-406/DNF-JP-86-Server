using Dfo.Server.Abstractions.Content;
using Dfo.Server.Infrastructure.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

public sealed class PvfDungeonMapDefinitionSourceTests
{
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
