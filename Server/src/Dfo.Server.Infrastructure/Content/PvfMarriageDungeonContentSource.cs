using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Marriage;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfMarriageDungeonContentSource(PvfArchiveProvider provider, IDungeonDefinitionSource dungeons,
    IDungeonMapDefinitionSource maps) : IMarriageDungeonContentSource
{
    public async ValueTask<bool> IsOrdinaryMapAsync(int dungeon, int map, CancellationToken ct)
    {
        var definition = await dungeons.GetAsync(dungeon, ct).ConfigureAwait(false);
        if (definition is null || definition.EntryMode != DungeonEntryMode.Maze
            || !PvfIndependentDropTableSource.IsGrowthBonusDungeon(PvfNpcShopDefinitionSource.Sections(provider.Archive, definition.ArchivePath))) return false;
        // Ordinary mazes can obtain every map through their dynamic greed table, leaving the
        // static cell's candidate arrays empty. Boss position is the producer's frozen fact;
        // this content predicate checks the actual map belongs to the ordinary dungeon.
        return (await maps.GetAsync(map, ct).ConfigureAwait(false))?.DungeonIds.Contains(dungeon) == true;
    }
}
