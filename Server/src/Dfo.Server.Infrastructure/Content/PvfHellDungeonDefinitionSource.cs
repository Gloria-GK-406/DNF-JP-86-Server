using System.Collections.Immutable;
using Dfo.Pvf;
using Dfo.Pvf.Schema;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Infrastructure.Content;

/// <summary>Static WDM/DGN/MAP/hellparty declarations; no player state or random choices.</summary>
internal sealed class PvfHellDungeonDefinitionSource(PvfArchiveProvider provider) : IHellDungeonDefinitionSource, Dfo.Server.Abstractions.Content.IContentWarmup
{
    private readonly Lock gate = new();
    private readonly Dictionary<int, HellDungeonDefinition?> cache = [];
    private Dictionary<int, Area>? areas;
    private Dictionary<int, string>? dungeons;
    private Dictionary<int, string>? maps;
    private Dictionary<int, string>? aiCharacters;
    private HellPartyDefinition? party;
    private PvfArchive Archive => provider.Archive;

    public string Name => "hell-dungeons";

    public async ValueTask<int> WarmAsync(CancellationToken cancellationToken)
    {
        await GetPartyAsync(cancellationToken).ConfigureAwait(false);
        lock (gate)
        {
            DungeonIndexes();
            return areas.Count + dungeons.Count + maps.Count;
        }
    }

    [System.Diagnostics.CodeAnalysis.MemberNotNull(nameof(areas), nameof(dungeons), nameof(maps))]
    private void DungeonIndexes()
    {
        areas ??= ReadAreas();
        dungeons ??= Paths("dungeon");
        maps ??= Paths("map");
    }

    public ValueTask<HellDungeonDefinition?> GetAsync(int dungeonId, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (!cache.TryGetValue(dungeonId, out var result)) cache[dungeonId] = result = ReadDungeon(dungeonId);
            return ValueTask.FromResult(result);
        }
    }

    public ValueTask<HellPartyDefinition> GetPartyAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        lock (gate) return ValueTask.FromResult(party ??= ReadParty());
    }

    private Dictionary<int, string> Paths(string directory) => PvfListFile.Read(Archive, $"{directory}/{directory}.lst")
        .GroupBy(x => x.Item1).ToDictionary(x => x.Key, x => x.First().Item2);

    private PvfDocument? Open(string path) => Archive.FindFile(path) < 0 ? null : PvfDocument.Open(Archive, path);
    private int[] Ints(PvfSection section, string path) => PvfField.Int32List(section.Name, optional: true)
        .Decode(Archive, section.Values, path).ToArray();
    private int[] Ints(PvfDocument doc, string name) => doc.Sections.FirstOrDefault(x => x.Name == name) is { } section
        ? Ints(section, name) : [];

    private Dictionary<int, Area> ReadAreas()
    {
        var result = new Dictionary<int, Area>();
        // Admission-only WDMs can be absent from the UI list (same distinction as upstream).
        var paths = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (_, relative) in PvfListFile.Read(Archive, "worldmap/worldmap.lst"))
            paths.Add(relative.StartsWith("worldmap/", StringComparison.OrdinalIgnoreCase) ? relative : "worldmap/" + relative);
        for (var i = 0; i < Archive.FileCount; i++)
        {
            var entry = Archive.GetEntry(i);
            var folder = Archive.ResolveString(entry.PathOffset);
            var name = Archive.ResolveString(entry.NameOffset);
            if (folder.Equals("worldmap", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".wdm", StringComparison.OrdinalIgnoreCase))
                paths.Add(folder + "/" + name);
        }
        foreach (var path in paths)
        {
            var doc = Open(path);
            if (doc is null || Ints(doc, "[hell dungeon]").FirstOrDefault() != 1) continue;
            var quests = Ints(doc, "[hell quest]").Where(x => x > 0).ToImmutableArray();
            var free = Ints(doc, "[hell freepass item]");
            if (free.Length % 2 != 0) throw new InvalidDataException($"{path}: incomplete freepass pair.");
            var passes = Enumerable.Range(0, free.Length / 2).Select(i => new HellTicketAlternative(free[i * 2], free[i * 2 + 1]))
                .ToImmutableArray();
            var area = new Area(quests, passes, Ints(doc, "[item condition]").Where(x => x > 0).ToImmutableArray());
            // The [dungeon] stream is dungeonId [in progress]? questId, not a flat ID list.
            var inside = false;
            var values = new List<PvfToken>();
            foreach (var section in doc.Sections)
            {
                if (section.Name == "[dungeon]") { inside = true; values.AddRange(section.Values); }
                else if (section.Name == "[/dungeon]") inside = false;
                else if (inside && section.Name == "[in progress]") values.AddRange(section.Values);
                else if (inside) throw new InvalidDataException($"{path}: unknown dungeon-list marker {section.Name}.");
            }
            var ids = values.Where(x => x.Kind == PvfTokenKind.Int32).Select(x => x.Value).ToArray();
            if (ids.Length % 2 != 0) throw new InvalidDataException($"{path}: incomplete dungeon/quest pair.");
            for (var at = 0; at < ids.Length; at += 2)
                if (ids[at] > 0) result.TryAdd(ids[at], area);
        }
        return result;
    }

    private HellDungeonDefinition? ReadDungeon(int id)
    {
        DungeonIndexes();
        if (!areas.TryGetValue(id, out var area) || !dungeons.TryGetValue(id, out var path)) return null;
        var doc = Open("dungeon/" + path);
        if (doc is null) return null;
        var rooms = new Dictionary<int, HellRoomDefinition>();
        var maze = -1; var enabled = false; var mapId = 0; int[] position = [];
        void Finish()
        {
            if (maze < 0 || !enabled || mapId <= 0 || position.Length != 2
                || position.Any(x => x < 0 || x > byte.MaxValue) || !maps.TryGetValue(mapId, out var mapPath)) return;
            var map = Open("map/" + mapPath);
            if (map is null) return;
            var candidates = ReadCandidates(map);
            if (candidates.Count > 0) rooms.Add(maze, new(mapId, new((byte)position[0], (byte)position[1]), candidates));
        }
        foreach (var section in doc.Sections)
        {
            if (section.Name == "[maze info]") { Finish(); maze++; enabled = false; mapId = 0; position = []; }
            else if (section.Name == "[hell dungeon]") enabled = Ints(section, path).FirstOrDefault() == 1;
            else if (section.Name == "[seal door map index]") mapId = Ints(section, path).FirstOrDefault();
            else if (section.Name == "[seal door pos]") position = Ints(section, path);
        }
        Finish();
        return new(rooms.Count > 0, area.Quests, area.FreePasses, area.Tickets, rooms.ToImmutableDictionary());
    }

    private IReadOnlyList<HellGroupCandidate> ReadCandidates(PvfDocument map)
    {
        var result = ImmutableArray.CreateBuilder<HellGroupCandidate>();
        foreach (var section in map.Sections.Where(x => x.Name == "[special passive object]"))
        {
            var inside = false;
            var values = new List<int>();
            foreach (var value in section.Values)
            {
                var text = value.IsString ? Archive.ResolveString(value.Value) : null;
                if (text == "[hellparty]") { inside = true; values.Clear(); }
                else if (text == "[/hellparty]")
                {
                    if (!inside || values.Count % 3 != 0) throw new InvalidDataException("Malformed map hellparty group triples.");
                    for (var at = 0; at < values.Count; at += 3)
                        result.Add(new(values[at], values[at + 1], checked((ushort)values[at + 2])));
                    inside = false;
                }
                else if (inside && value.Kind == PvfTokenKind.Int32) values.Add(value.Value);
                else if (inside) throw new InvalidDataException("Unexpected hellparty group token.");
            }
            if (inside) throw new InvalidDataException("Unclosed map hellparty block.");
        }
        return result.ToImmutable();
    }

    private HellPartyDefinition ReadParty()
    {
        const string path = "etc/hellparty.etc";
        var doc = Open(path) ?? throw new InvalidDataException($"Missing {path}.");
        var weights = ImmutableArray.CreateBuilder<HellDifficultyWeight>();
        var values = doc.Sections.Single(x => x.Name == "[difficulty]").Values;
        if (values.Count == 0 || values[0].Kind != PvfTokenKind.Int32 || values.Count != 1 + values[0].Value * 6)
            throw new InvalidDataException("Malformed hellparty difficulty rows.");
        for (var at = 1; at < values.Count; at += 6)
        {
            var difficulty = Difficulty(values[at]);
            if (values.Skip(at + 1).Take(5).Any(x => x.Kind != PvfTokenKind.Int32))
                throw new InvalidDataException("Malformed hellparty difficulty integers.");
            if (values[at + 1].Value is < 0 or > 255) throw new InvalidDataException("Invalid hell reward attempt count.");
            weights.Add(new(difficulty, values[at + 4].Value) { RewardRollCount = values[at + 1].Value });
        }
        var groups = ImmutableArray.CreateBuilder<HellActorGroup>();
        var groupId = 0;
        foreach (var section in doc.Sections)
        {
            if (section.Name == "[group index]") groupId = Ints(section, path).Single();
            else if (section.Name == "[group]" && groupId > 0)
            {
                var row = section.Values;
                if (row.Count < 3 || (row.Count - 1) % 2 != 0) throw new InvalidDataException("Malformed hellparty actor group.");
                var actors = ImmutableArray.CreateBuilder<DungeonRoomActor>();
                for (var at = 1; at < row.Count; at += 2)
                {
                    if (row[at].Kind != PvfTokenKind.Int32 || row[at + 1].Kind != PvfTokenKind.Int32 || row[at].Value <= 0)
                        throw new InvalidDataException("Malformed hellparty actor identity.");
                    var isAi = row[at + 1].Value == 1;
                    actors.Add(new(row[at].Value, isAi ? AiLevel(row[at].Value) : (byte)0,
                        isAi ? (byte)5 : (byte)0, RandomDropCount: 0));
                }
                groups.Add(new(groupId, Difficulty(row[0]), actors.ToImmutable()));
                groupId = 0;
            }
        }
        return new(weights.ToImmutable(), groups.ToImmutable());
    }

    private byte AiLevel(int code)
    {
        aiCharacters ??= Paths("aicharacter");
        if (!aiCharacters.TryGetValue(code, out var path) || Open("aicharacter/" + path) is not { } doc)
            throw new InvalidDataException($"Missing hell AI character {code}.");
        var values = doc.Sections.FirstOrDefault(x => x.Name == "[minimum info]")?.Values;
        // Same .aic schema as PvfDungeonRoomActorSource: name, job/grow/etc, level at token 5.
        if (values is null || values.Count != 16 || values[5].Kind != PvfTokenKind.Int32
            || values[5].Value is <= 0 or > byte.MaxValue)
            throw new InvalidDataException($"Invalid hell AI character level: {path}.");
        return (byte)values[5].Value;
    }

    private byte Difficulty(PvfToken value) => value.IsString ? Archive.ResolveString(value.Value) switch
    { "A" => (byte)1, "B" => (byte)2, "C" => (byte)3, "D" => (byte)4, var text => throw new InvalidDataException($"Unknown hell difficulty {text}.") }
        : throw new InvalidDataException("Hell difficulty must be a letter.");

    private sealed record Area(IReadOnlyList<int> Quests, IReadOnlyList<HellTicketAlternative> FreePasses, IReadOnlyList<int> Tickets);
}
