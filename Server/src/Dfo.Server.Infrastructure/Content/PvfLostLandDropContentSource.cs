using System.Collections.Immutable;
using Dfo.Pvf;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfLostLandDropContentSource(PvfArchiveProvider provider,
    IDungeonDefinitionSource dungeons, IDungeonRoomDropSource objectDrops) : ILostLandDropContentSource
{
    public async ValueTask<LostLandDropContent> ReadAsync(CancellationToken cancellationToken)
    {
        var dungeon = await dungeons.GetAsync(4000, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("Missing LostLand dungeon.");
        var paths = PvfListFile.Read(provider.Archive, "map/map.lst").ToDictionary(row => row.Id, row => row.RelativePath);
        var maps = ImmutableDictionary.CreateBuilder<int, int>();
        foreach (var id in dungeon.Mazes.SelectMany(maze => maze.Cells.Values)
            .SelectMany(cell => cell.MapCandidates.Concat(cell.BossMapCandidates)).Distinct())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!paths.TryGetValue(id, out var path)) throw new InvalidDataException($"Missing LostLand map {id}.");
            maps.Add(id, CrystalForPath(path.Replace('\\', '/').ToLowerInvariant()));
        }
        var reader = new PvfDropItemReader(provider.Archive);
        var items = Enumerable.Range(10092844, 6).Append(10092856).ToImmutableDictionary(id => id,
            id => reader.Get(id) ?? throw new InvalidDataException($"Missing LostLand drop item {id}."));
        // Group 4 is the existing LostLand special-object pool containing its three exclusive equips.
        // Reuse the pool reader; never copy the pool's IDs or interpret its weights as monster rates.
        var pool = await objectDrops.GetItemPoolAsync(4000, 4, cancellationToken).ConfigureAwait(false);
        var equipment = pool.Where(entry => entry.Weight > 0).Select(entry => reader.Get(entry.ItemId))
            .Where(item => item?.Kind == DropItemKind.Equipment).Select(item => item!.Value)
            .DistinctBy(item => item.ItemId).OrderBy(item => item.ItemId).ToImmutableArray();
        if (equipment.IsEmpty) throw new InvalidDataException("LostLand exclusive equipment pool is empty.");
        return new(maps.ToImmutable(), items, equipment);
    }

    private static int CrystalForPath(string path) => path switch
    {
        _ when path.StartsWith("forgotten_land/", StringComparison.Ordinal)
            || path.StartsWith("forgotten_land_hiden/", StringComparison.Ordinal) => 10092844,
        _ when path.Contains("/distraction_forest/", StringComparison.Ordinal) => 10092845,
        _ when path.Contains("/snowflower_mountain/", StringComparison.Ordinal) => 10092846,
        _ when path.Contains("/disappeared_town/", StringComparison.Ordinal) => 10092847,
        _ when path.StartsWith("forgotten_land_jungle/", StringComparison.Ordinal)
            || path.StartsWith("forgotten_land_jungle_h/", StringComparison.Ordinal) => 10092848,
        _ when path.Contains("/lasttemple/", StringComparison.Ordinal) => 0,
        _ => throw new InvalidDataException($"Unclassified LostLand map '{path}'."),
    };
}
