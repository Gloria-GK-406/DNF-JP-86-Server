using Dfo.Pvf;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Infrastructure.Content;

/// <summary>
/// Reads what a room's breakable objects yield, from the room's map and the dungeon's table.
/// </summary>
/// <remarks>
/// The room's <c>.map</c> declares each breakable object as five integers, a quoted kind and five
/// more integers: <c>code x y z ? [item] group count -1 -1 -1</c>. Only objects whose kind is
/// <c>[item]</c> yield anything, and the group is an index into the dungeon's own table.
/// <para>
/// The dungeon's <c>.dgn</c> holds that table under <c>[special passive object item]</c>, as a
/// flat run of groups: <c>group ? entryCount</c> and then <c>entryCount</c> pairs of item and
/// weight. A group may declare no entries at all, which is how the tutorial's third object is
/// written and why the run has to be read by its counts rather than in fixed strides.
/// </para>
/// <para>
/// Only the first entry of a group is used. A group with several is a weighted draw, and drawing
/// needs the room's own generator to stay reproducible across a re-send of the same room -- which
/// is a thing to build once a dungeon actually declares one. Every group the tutorial declares has
/// a single entry.
/// </para>
/// </remarks>
internal sealed class PvfDungeonRoomDropSource : IDungeonRoomDropSource
{
    private const string DungeonListPath = "dungeon/dungeon.lst";
    private const string DungeonDirectory = "dungeon";
    private const string MapListPath = "map/map.lst";
    private const string MapDirectory = "map";
    private const string EquipmentListPath = "equipment/equipment.lst";

    /// <summary>The kind a breakable object carries when it holds an item.</summary>
    private const string ItemKind = "[item]";

    /// <summary>Five integers before the kind, and the group and count after it.</summary>
    private const int ObjectIntegersBeforeKind = 5;

    private readonly PvfArchiveProvider _provider;
    private readonly Lock _gate = new();
    private readonly Dictionary<(int Dungeon, int Map), IReadOnlyList<DungeonRoomDrop>> _cache = [];
    private readonly Dictionary<int, IReadOnlyDictionary<int, int>> _groupsByDungeon = [];
    private IReadOnlyDictionary<int, string>? _dungeonPaths;
    private IReadOnlyDictionary<int, string>? _mapPaths;
    private IReadOnlyDictionary<int, string>? _equipmentPaths;

    public PvfDungeonRoomDropSource(PvfArchiveProvider provider) => _provider = provider;

    public ValueTask<IReadOnlyList<DungeonRoomDrop>> GetAsync(
        int dungeonId, int mapId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_cache.TryGetValue((dungeonId, mapId), out var cached))
                return ValueTask.FromResult(cached);

            var drops = Read(_provider.Archive, dungeonId, mapId);
            _cache[(dungeonId, mapId)] = drops;
            return ValueTask.FromResult(drops);
        }
    }

    private IReadOnlyDictionary<int, string> Paths(
        ref IReadOnlyDictionary<int, string>? held, string listPath)
    {
        if (held is not null)
            return held;

        var paths = new Dictionary<int, string>();
        foreach (var (id, relativePath) in PvfListFile.Read(_provider.Archive, listPath))
            paths.TryAdd(id, relativePath);
        return held = paths;
    }

    private IReadOnlyList<DungeonRoomDrop> Read(PvfArchive archive, int dungeonId, int mapId)
    {
        var objects = RoomObjectGroups(archive, mapId);
        if (objects.Count == 0)
            return [];

        var groups = ItemGroups(archive, dungeonId);
        if (groups.Count == 0)
            return [];

        var equipment = Paths(ref _equipmentPaths, EquipmentListPath);
        var drops = new List<DungeonRoomDrop>();
        for (var index = 0; index < objects.Count; index++)
        {
            if (objects[index] is not { Group: { } group } declared
                || !groups.TryGetValue(group, out var itemId))
            {
                continue;
            }

            drops.Add(new DungeonRoomDrop(
                checked((byte)index),
                itemId,
                PvfEquipmentDurability.Read(archive, equipment, itemId),
                declared.Count));
        }

        return drops;
    }

    /// <summary>What one breakable object declares: which reward group, and how many.</summary>
    /// <remarks>
    /// The count is the second integer after the kind and it is read rather than assumed. Every
    /// object the tutorial declares says one, so an assumption would have looked right for as long
    /// as nothing declared otherwise -- and been wrong on the floor the moment something did.
    /// </remarks>
    private readonly record struct RoomObject(int? Group, int Count);

    /// <summary>
    /// One entry per breakable object the room declares, in order: its item group, or none when
    /// the object holds no item.
    /// </summary>
    /// <remarks>
    /// The objects that hold nothing keep their place. The client names the object it broke by its
    /// position among all of them, so dropping the empty ones would renumber the rest.
    /// </remarks>
    private IReadOnlyList<RoomObject> RoomObjectGroups(PvfArchive archive, int mapId)
    {
        if (!Paths(ref _mapPaths, MapListPath).TryGetValue(mapId, out var relativePath))
            return [];

        var fileIndex = archive.FindFile($"{MapDirectory}/{relativePath}");
        if (fileIndex < 0)
            return [];

        var open = archive.GetTag("[special passive object]").Offset;
        var close = archive.GetTag("[/special passive object]").Offset;
        var objects = new List<RoomObject>();
        var inside = false;
        var integers = new List<int>();
        string? kind = null;

        var reader = archive.OpenScript(fileIndex);
        while (reader.Read(out var token))
        {
            if (token.Kind == PvfTokenKind.Identifier)
            {
                if (token.Value == open)
                {
                    inside = true;
                }
                else if (inside)
                {
                    break;
                }

                integers.Clear();
                kind = null;
                continue;
            }

            if (!inside)
                continue;

            if (token.IsString)
            {
                kind = archive.ResolveString(token.Value);
                continue;
            }

            if (token.Kind != PvfTokenKind.Int32)
                continue;

            integers.Add(token.Value);
            if (kind is null || integers.Count != ObjectIntegersBeforeKind + 5)
                continue;

            objects.Add(
                string.Equals(kind, ItemKind, StringComparison.OrdinalIgnoreCase)
                    ? new RoomObject(
                        integers[ObjectIntegersBeforeKind],
                        // At least one: a declaration of zero or less would put a stack of nothing
                        // on the floor, and the object is only here because it holds something.
                        Math.Max(1, integers[ObjectIntegersBeforeKind + 1]))
                    : new RoomObject(null, 1));
            integers.Clear();
            kind = null;
        }

        _ = close;
        return objects;
    }

    /// <summary>The dungeon's reward groups: group index to the item its first entry names.</summary>
    private IReadOnlyDictionary<int, int> ItemGroups(PvfArchive archive, int dungeonId)
    {
        if (_groupsByDungeon.TryGetValue(dungeonId, out var cached))
            return cached;

        var groups = ReadItemGroups(archive, dungeonId);
        _groupsByDungeon[dungeonId] = groups;
        return groups;
    }

    private IReadOnlyDictionary<int, int> ReadItemGroups(PvfArchive archive, int dungeonId)
    {
        var groups = new Dictionary<int, int>();
        if (!Paths(ref _dungeonPaths, DungeonListPath).TryGetValue(dungeonId, out var relativePath))
            return groups;

        var fileIndex = archive.FindFile($"{DungeonDirectory}/{relativePath}");
        if (fileIndex < 0)
            return groups;

        var open = archive.GetTag("[special passive object item]").Offset;
        var numbers = new List<int>();
        var inside = false;

        var reader = archive.OpenScript(fileIndex);
        while (reader.Read(out var token))
        {
            if (token.Kind == PvfTokenKind.Identifier)
            {
                if (token.Value == open)
                {
                    inside = true;
                    continue;
                }

                if (inside)
                    break;
                continue;
            }

            if (inside && token.Kind == PvfTokenKind.Int32)
                numbers.Add(token.Value);
        }

        var at = 0;
        while (at + 3 <= numbers.Count)
        {
            var group = numbers[at];
            var entries = numbers[at + 2];
            at += 3;
            if (entries < 0 || at + (entries * 2) > numbers.Count)
                break;

            if (entries > 0)
                groups.TryAdd(group, numbers[at]);
            at += entries * 2;
        }

        return groups;
    }
}
