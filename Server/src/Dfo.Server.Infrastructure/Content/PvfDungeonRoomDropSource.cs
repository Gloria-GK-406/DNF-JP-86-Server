using Dfo.Pvf.Schema;
using System.Collections.Immutable;
using Dfo.Pvf;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Infrastructure.Content;

/// <summary>Caches declarations, never a random result shared by different dungeon runs.</summary>
internal sealed class PvfDungeonRoomDropSource(PvfArchiveProvider provider) : IDungeonRoomDropSource, IDungeonRoomObjectSource, Dfo.Server.Abstractions.Content.IContentWarmup
{
    private static class Fields
    {
        // These delimiters occur inside the quoted mixed object payload, not as Identifier sections.
        internal static readonly PvfField<IReadOnlyList<PvfToken>> HellpartyBlock = PvfField.Tokens("[hellparty]", optional: true, tokenKind: PvfTokenKind.QuotedString);
        internal static readonly PvfField<bool> HellpartyClose = PvfField.Marker("[/hellparty]", optional: true, tokenKind: PvfTokenKind.QuotedString);
        internal static readonly PvfField<IReadOnlyList<PvfToken>> SpecialPassiveObjectField = PvfField.Tokens("[special passive object]", optional: true);
        internal static readonly PvfField<IReadOnlyList<PvfToken>> SpecialPassiveObjectItemField = PvfField.Tokens("[special passive object item]", optional: true);
        internal static readonly PvfField<IReadOnlyList<PvfToken>> PassiveObjectField = PvfField.Tokens("[passive object]", optional: true);
    }

    private readonly Dictionary<int, IReadOnlyList<DungeonRoomObject>> objects = [];
    private readonly Lock gate = new();
    private readonly Dictionary<(int Dungeon, int Map), IReadOnlyList<ObjectDropDefinition>> cache = [];
    private readonly Dictionary<int, IReadOnlyDictionary<int, Group>> groups = [];
    private readonly Dictionary<int, IReadOnlyList<Action>> maps = [];
    private readonly Dictionary<string, IReadOnlyDictionary<int, string>> paths = [];

    private sealed record Group(int Level, IReadOnlyList<ObjectDropEntry> Entries);
    internal sealed record Action(byte ObjectIndex, int ObjectCode, int ActionIndex,
        int Group, int SpecificAttempts, int RandomAttempts);

    public ValueTask<IReadOnlyList<ObjectDropDefinition>> GetAsync(
        int dungeonId, int mapId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (cache.TryGetValue((dungeonId, mapId), out var existing)) return ValueTask.FromResult(existing);
            if (!maps.TryGetValue(mapId, out var actions))
                maps[mapId] = actions = ParseActions(ReadSection("map", mapId, Fields.SpecialPassiveObjectField), mapId);
            if (!groups.TryGetValue(dungeonId, out var table))
                groups[dungeonId] = table = ParseGroups(ReadSection("dungeon", dungeonId,
                    Fields.SpecialPassiveObjectItemField).Select(t => int.Parse(t, System.Globalization.CultureInfo.InvariantCulture)).ToArray(), dungeonId,
                    item => PvfEquipmentDurability.Read(provider.Archive, Paths("equipment"), item));
            // MAPs may retain item actions while the dungeon explicitly has no object loot table.
            // Parsing above still validates both declarations; an empty table disables only loot.
            if (table.Count == 0)
            {
                IReadOnlyList<ObjectDropDefinition> empty = ImmutableArray<ObjectDropDefinition>.Empty;
                cache[(dungeonId, mapId)] = empty;
                return ValueTask.FromResult(empty);
            }
            var result = ImmutableArray.CreateBuilder<ObjectDropDefinition>();
            foreach (var action in actions)
            {
                if (!table.TryGetValue(action.Group, out var group))
                    throw new InvalidDataException($"Dungeon {dungeonId}, map {mapId}, object {action.ObjectIndex}: missing group {action.Group}.");
                result.Add(new(action.ObjectIndex, action.ObjectCode, action.ActionIndex, group.Level,
                    action.SpecificAttempts, action.RandomAttempts, group.Entries));
            }
            IReadOnlyList<ObjectDropDefinition> answer = result.ToImmutable();
            cache[(dungeonId, mapId)] = answer;
            return ValueTask.FromResult(answer);
        }
    }

    public ValueTask<IReadOnlyList<DungeonRoomObject>> GetObjectsAsync(int mapId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (!objects.TryGetValue(mapId, out var result))
            {
                var declared = ParseOrdinaryObjects(ReadSection("map", mapId, Fields.PassiveObjectField), mapId).ToList();
                ParseActions(ReadSection("map", mapId, Fields.SpecialPassiveObjectField), mapId, declared);
                objects[mapId] = result = declared.Select(obj => obj with
                {
                    // A21 00FE locates the built-in object code 1113, not a dungeon ID.
                    Mechanism = obj.Code == 1113 && Paths("passiveobject").TryGetValue(obj.Code, out var path)
                        && path.Replace('\\', '/').Equals("MapObject/Obstacle/ElevatorControl.obj", StringComparison.OrdinalIgnoreCase)
                        ? Dfo.Server.Abstractions.World.DungeonRoomMechanismKind.Elevator : null,
                }).ToImmutableArray();
            }
            return ValueTask.FromResult(result);
        }
    }

    internal static IReadOnlyList<DungeonRoomObject> ParseOrdinaryObjects(IReadOnlyList<string> values, int mapId)
    {
        if (values.Count % 4 != 0)
            throw new InvalidDataException($"Map {mapId}: truncated passive object.");
        var result = new List<DungeonRoomObject>();
        for (var at = 0; at < values.Count; at += 4)
        {
            var row = values.Skip(at).Take(4).Select(value => int.Parse(value,
                System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            result.Add(new(new(DungeonObjectSection.Passive, at / 4), row[0], row[1], row[2], row[3]));
        }
        return result.ToImmutableArray();
    }

    public string Name => "dungeon-room-object-index";

    /// <remarks>The list files the room readers resolve ids through; the per-map reads stay keyed.</remarks>
    public ValueTask<int> WarmAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate) return ValueTask.FromResult(new[] { "map", "dungeon", "equipment", "passiveobject" }.Sum(folder => Paths(folder).Count));
    }

    private IReadOnlyDictionary<int, string> Paths(string folder)
    {
        if (paths.TryGetValue(folder, out var value)) return value;
        var result = new Dictionary<int, string>();
        foreach (var (id, path) in PvfListFile.Read(provider.Archive, $"{folder}/{folder}.lst"))
            result.TryAdd(id, path);
        return paths[folder] = result;
    }

    private IReadOnlyList<string> ReadSection(string folder, int id, PvfField<IReadOnlyList<PvfToken>> tag)
    {
        if (!Paths(folder).TryGetValue(id, out var path)) return [];
        var archive = provider.Archive;
        var file = archive.FindFile($"{folder}/{path}");
        if (file < 0) return [];
        var reader = archive.OpenScript(file);
        var result = new List<string>();
        var inside = false;
        while (reader.Read(out var token))
        {
            if (token.Kind == PvfTokenKind.Identifier)
            {
                if (archive.ResolveString(token.Value) == tag.Name) { inside = true; continue; }
                if (inside) break;
            }
            else if (inside)
            {
                if (token.IsString) result.Add(archive.ResolveString(token.Value));
                else if (token.Kind == PvfTokenKind.Int32)
                    result.Add(token.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
                else throw new InvalidDataException($"{folder}/{path}: unexpected token in {tag}.");
            }
        }
        return result;
    }

    internal static IReadOnlyList<Action> ParseActions(IReadOnlyList<string> values, int mapId,
        ICollection<DungeonRoomObject>? declaredObjects = null)
    {
        var result = ImmutableArray.CreateBuilder<Action>();
        var at = 0;
        int ReadInt()
        {
            if (at >= values.Count || !int.TryParse(values[at++], System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out var value))
                throw new InvalidDataException($"Map {mapId}: malformed special passive object.");
            return value;
        }
        for (var objectIndex = 0; at < values.Count; objectIndex++)
        {
            // Two old Amon maps append a standalone abyss block after all object records.
            if (values[at] == Fields.HellpartyBlock.Name)
            {
                at++;
                while (at < values.Count && values[at] != Fields.HellpartyClose.Name) ReadInt();
                if (at != values.Count - 1)
                    throw new InvalidDataException($"Map {mapId}: malformed trailing hellparty block.");
                break;
            }
            var code = ReadInt();
            var x = ReadInt(); var y = ReadInt(); var z = ReadInt();
            var actionCount = ReadInt();
            if (objectIndex > byte.MaxValue || actionCount < 0 || actionCount > values.Count - at)
                throw new InvalidDataException($"Map {mapId}: invalid object/action count.");
            declaredObjects?.Add(new(new(DungeonObjectSection.SpecialPassive, objectIndex), code, x, y, z));
            for (var actionIndex = 0; actionIndex < actionCount; actionIndex++)
            {
                if (at >= values.Count) throw new InvalidDataException($"Map {mapId}: missing action.");
                var kind = values[at++];
                if (kind == Fields.HellpartyBlock.Name)
                {
                    // Variable-length enemy groups belong to the abyss system, not object loot.
                    while (at < values.Count && values[at] != Fields.HellpartyClose.Name) ReadInt();
                    if (at == values.Count) throw new InvalidDataException($"Map {mapId}: unclosed hellparty action.");
                    at++;
                    continue;
                }
                if (kind is not ("[item]" or "[monster]" or "[trap]" or "[quest]"))
                    throw new InvalidDataException($"Map {mapId}: unknown object action {kind}.");
                var group = ReadInt(); var specific = ReadInt(); var random = ReadInt();
                ReadInt(); ReadInt();
                if (kind != "[item]") continue;
                if (group < 0 || specific < -1 || random < -1 || specific > 127 || random > 127)
                    throw new InvalidDataException($"Map {mapId}: invalid item action operands.");
                result.Add(new((byte)objectIndex, code, actionIndex, group, Math.Max(0, specific), Math.Max(0, random)));
            }
        }
        return result.ToImmutable();
    }

    private static IReadOnlyDictionary<int, Group> ParseGroups(
        IReadOnlyList<int> values, int dungeonId, Func<int, ushort> durability)
    {
        var result = new Dictionary<int, Group>();
        var at = 0;
        while (at < values.Count)
        {
            if (values.Count - at < 3) throw new InvalidDataException($"Dungeon {dungeonId}: truncated item group.");
            var id = values[at++]; var level = values[at++]; var count = values[at++];
            if (id < 0 || level < -1 || level > 200 || count < 0 || count > (values.Count - at) / 2)
                throw new InvalidDataException($"Dungeon {dungeonId}: invalid item group {id}.");
            var entries = ImmutableArray.CreateBuilder<ObjectDropEntry>();
            for (var i = 0; i < count; i++)
            {
                var item = values[at++]; var weight = values[at++];
                if (item <= 0 || weight < 0) throw new InvalidDataException($"Dungeon {dungeonId}: invalid reward {item}.");
                entries.Add(new(item, weight, durability(item)));
            }
            if (!result.TryAdd(id, new(level, entries.ToImmutable())))
                throw new InvalidDataException($"Dungeon {dungeonId}: duplicate item group {id}.");
        }
        return result;
    }
}
