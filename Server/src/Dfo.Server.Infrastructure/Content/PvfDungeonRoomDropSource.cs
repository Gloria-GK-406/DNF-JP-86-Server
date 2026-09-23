using System.Collections.Immutable;
using Dfo.Pvf;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Infrastructure.Content;

/// <summary>Caches declarations, never a random result shared by different dungeon runs.</summary>
internal sealed class PvfDungeonRoomDropSource(PvfArchiveProvider provider) : IDungeonRoomDropSource
{
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
                maps[mapId] = actions = ParseActions(ReadSection("map", mapId, "[special passive object]"), mapId);
            if (!groups.TryGetValue(dungeonId, out var table))
                groups[dungeonId] = table = ParseGroups(ReadSection("dungeon", dungeonId,
                    "[special passive object item]").Select(t => int.Parse(t, System.Globalization.CultureInfo.InvariantCulture)).ToArray(), dungeonId,
                    item => PvfEquipmentDurability.Read(provider.Archive, Paths("equipment"), item));
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

    private IReadOnlyDictionary<int, string> Paths(string folder)
    {
        if (paths.TryGetValue(folder, out var value)) return value;
        var result = new Dictionary<int, string>();
        foreach (var (id, path) in PvfListFile.Read(provider.Archive, $"{folder}/{folder}.lst"))
            result.TryAdd(id, path);
        return paths[folder] = result;
    }

    private IReadOnlyList<string> ReadSection(string folder, int id, string tag)
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
                if (archive.ResolveString(token.Value) == tag) { inside = true; continue; }
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

    internal static IReadOnlyList<Action> ParseActions(IReadOnlyList<string> values, int mapId)
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
            if (values[at] == "[hellparty]")
            {
                at++;
                while (at < values.Count && values[at] != "[/hellparty]") ReadInt();
                if (at != values.Count - 1)
                    throw new InvalidDataException($"Map {mapId}: malformed trailing hellparty block.");
                break;
            }
            var code = ReadInt();
            ReadInt(); ReadInt(); ReadInt(); // Position, not drop operands.
            var actionCount = ReadInt();
            if (objectIndex > byte.MaxValue || actionCount < 0 || actionCount > values.Count - at)
                throw new InvalidDataException($"Map {mapId}: invalid object/action count.");
            for (var actionIndex = 0; actionIndex < actionCount; actionIndex++)
            {
                if (at >= values.Count) throw new InvalidDataException($"Map {mapId}: missing action.");
                var kind = values[at++];
                if (kind == "[hellparty]")
                {
                    // Variable-length enemy groups belong to the abyss system, not object loot.
                    while (at < values.Count && values[at] != "[/hellparty]") ReadInt();
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
