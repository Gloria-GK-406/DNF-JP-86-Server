using System.Collections.ObjectModel;
using Dfo.Pvf.Schema;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Infrastructure.Content;

/// <summary>Indexes MAP ownership and connection metadata, without selecting any runtime map.</summary>
internal sealed class PvfDungeonMapDefinitionSource(PvfArchiveProvider provider) : IDungeonMapDefinitionSource
{
    private static readonly PvfField<IReadOnlyList<int>> Dungeon = PvfField.Int32List("[dungeon]", optional: true);
    private static readonly PvfField<string> Type = PvfField.String("[type]", optional: true);
    private static readonly PvfField<string> Greed = PvfField.String("[greed]", optional: true);
    private static readonly PvfField<IReadOnlyList<int>> StartArea = PvfField.Int32List("[start area]", optional: true);
    private static readonly PvfField<IReadOnlyList<int>> DungeonStartArea = PvfField.Int32List("[dungeon start area]", optional: true);
    private readonly Lock gate = new();
    private readonly Dictionary<int, DungeonMapDefinition?> cache = [];
    private IReadOnlyDictionary<int, string>? paths;
    private IReadOnlyDictionary<int, IReadOnlyList<int>>? byDungeon;

    public ValueTask<DungeonMapDefinition?> GetAsync(int mapId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate) return ValueTask.FromResult(Read(mapId));
    }

    public ValueTask<IReadOnlyList<DungeonMapDefinition>> ListForDungeonAsync(int dungeonId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (byDungeon is null)
            {
                var index = new Dictionary<int, List<int>>();
                foreach (var id in Paths().Keys.Order())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var path = "map/" + Paths()[id];
                    if (provider.Archive.FindFile(path) < 0) continue;
                    var metadata = Metadata(path);
                    foreach (var owner in (Decode(metadata, Dungeon, path) ?? []).Distinct())
                    {
                        if (!index.TryGetValue(owner, out var maps)) index.Add(owner, maps = []);
                        maps.Add(id);
                    }
                }
                byDungeon = new ReadOnlyDictionary<int, IReadOnlyList<int>>(
                    index.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<int>)pair.Value.AsReadOnly()));
            }
            var result = new List<DungeonMapDefinition>();
            foreach (var id in byDungeon.GetValueOrDefault(dungeonId) ?? [])
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (Read(id) is { } map) result.Add(map);
            }
            return ValueTask.FromResult<IReadOnlyList<DungeonMapDefinition>>(result.AsReadOnly());
        }
    }

    private IReadOnlyDictionary<int, string> Paths() => paths ??= PvfListFile.Read(provider.Archive, "map/map.lst")
        .GroupBy(value => value.Id).ToDictionary(group => group.Key, group => group.First().RelativePath);

    private DungeonMapDefinition? Read(int mapId)
    {
        if (cache.TryGetValue(mapId, out var cached)) return cached;
        if (!Paths().TryGetValue(mapId, out var relativePath)) return null;
        var path = "map/" + relativePath;
        if (provider.Archive.FindFile(path) < 0) return null;
        // MAP fields may follow tile/actor blocks. Read every root field while excluding
        // nested declarations that reuse [type], [dungeon], [greed] or start-area tags.
        var metadata = Metadata(path);
        var owners = Decode(metadata, Dungeon, path) ?? [];
        var kindText = Decode(metadata, Type, path);
        var kind = kindText switch
        {
            null or "[normal]" => DungeonMapKind.Normal,
            "[boss]" => DungeonMapKind.Boss,
            "[dummy]" => DungeonMapKind.Dummy,
            _ => throw new InvalidDataException($"PVF '{path}', field '[type]': unsupported map kind '{kindText}'."),
        };
        var text = Decode(metadata, Greed, path);
        var connections = new List<DungeonMapConnection>();
        if (text is null) connections.Add(new(DungeonPathDirection.All, DungeonPathDirection.All));
        else
        {
            var values = PvfDungeonConnections.Decode(text, path);
            if (values.Count % 2 != 0) throw new InvalidDataException($"PVF '{path}', field '[greed]': incomplete pattern/entrance pair.");
            for (var offset = 0; offset < values.Count; offset += 2)
                connections.Add(new(values[offset], values[offset + 1]));
        }
        var sections = metadata;
        var hasStart = sections.Any(section => StartArea.Matches(section.Name)
                && StartArea.Decode(provider.Archive, section.Values, path).Count > 0
            || DungeonStartArea.Matches(section.Name)
                && DungeonStartArea.Decode(provider.Archive, section.Values, path).Count >= 4);
        var result = new DungeonMapDefinition(mapId, owners, kind, connections.AsReadOnly(), hasStart);
        cache.Add(mapId, result);
        return result;
    }

    private IReadOnlyList<PvfSection> Metadata(string path) => PvfDocument.Open(provider.Archive, path)
        .AtRoot().Sections;

    private T? Decode<T>(IReadOnlyList<PvfSection> sections, PvfField<T> field, string path) where T : class
    {
        var matches = sections.Where(section => field.Matches(section.Name)).ToArray();
        if (matches.Length > 1) throw new InvalidDataException($"PVF '{path}', field '{field}': duplicate map metadata.");
        return matches.Length == 0 ? null : field.Decode(provider.Archive, matches[0].Values, path);
    }
}
