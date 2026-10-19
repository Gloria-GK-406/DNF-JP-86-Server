using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dfo.Pvf;
using Dfo.Pvf.Schema;
using Dfo.Pvf.Tokens;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
Console.OutputEncoding = Encoding.UTF8;

if (args.Length != 2)
{
    Console.Error.WriteLine("usage: PvfContentProbe <Script.pvf> <output.json>");
    return 2;
}

var archivePath = Path.GetFullPath(args[0]);
var outputPath = Path.GetFullPath(args[1]);
using var archive = PvfArchive.Open(archivePath);

string TextOf(PvfToken token) => token.Kind == PvfTokenKind.Identifier || token.IsString
    ? archive.ResolveString(token.Value)
    : token.Kind == PvfTokenKind.Single
        ? BitConverter.Int32BitsToSingle(token.Value).ToString("R", System.Globalization.CultureInfo.InvariantCulture)
        : token.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);

List<(int Id, string Path)> ReadList(string listPath)
{
    var result = new List<(int, string)>();
    var reader = archive.OpenScript(listPath);
    var pending = int.MinValue;
    while (reader.Read(out var token))
    {
        if (token.Kind == PvfTokenKind.Int32) { pending = token.Value; continue; }
        if (!token.IsString) continue;
        result.Add((pending, archive.ResolveString(token.Value).Trim('`')));
        pending = int.MinValue;
    }
    return result;
}

string FullPath(int index)
{
    var entry = archive.GetEntry(index);
    var folder = archive.ResolveString(entry.PathOffset);
    var name = archive.ResolveString(entry.NameOffset);
    return folder.Length == 0 ? name : folder + "/" + name;
}

object Token(PvfToken token) => new { kind = token.Kind.ToString(), text = TextOf(token), raw = token.Value };

object? FileDump(string path, IEnumerable<string>? selected = null)
{
    var index = archive.FindFile(path);
    if (index < 0) return null;
    var names = selected?.ToHashSet(StringComparer.OrdinalIgnoreCase);
    var document = PvfDocument.Open(archive, path);
    var sections = document.Sections
        .Where(section => names is null || names.Contains(section.Name))
        .Select(section => new
        {
            tag = section.Name,
            values = section.Values.Select(Token).ToArray(),
        }).ToArray();
    return new { path, sections };
}

var dungeonList = ReadList("dungeon/dungeon.lst");
var mapList = ReadList("map/map.lst");
var monsterList = ReadList("monster/monster.lst");
var objectList = ReadList("passiveobject/passiveobject.lst");
var stackableList = ReadList("stackable/stackable.lst");
var questList = ReadList("n_quest/quest.lst");

var mapIds = Enumerable.Range(25313, 25).ToArray();
var relevantMapTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    "[dungeon]", "[type]", "[greed]", "[pathgate pos]", "[dungeon start area]", "[start area]",
    "[passive object]", "[special passive object]", "[monster]", "[monster condition]",
    "[conditional summon monster]", "[monster team]", "[ai character]", "[NPC]", "[map name]",
};

var usedMonsters = new HashSet<int>();
var usedObjects = new HashSet<int>();
var mapRecords = new List<object>();
foreach (var id in mapIds)
{
    var relative = mapList.First(value => value.Id == id).Path;
    var path = "map/" + relative;
    var doc = PvfDocument.Open(archive, path);
    var passiveRows = new List<object>();
    var monsterRows = new List<object>();
    var specialRows = new List<object>();
    foreach (var section in doc.Sections)
    {
        if (section.Name.Equals("[passive object]", StringComparison.OrdinalIgnoreCase))
        {
            for (var at = 0; at + 3 < section.Values.Count; at += 4)
                if (section.Values[at].Kind == PvfTokenKind.Int32)
                {
                    usedObjects.Add(section.Values[at].Value);
                    passiveRows.Add(new
                    {
                        code = section.Values[at].Value,
                        x = section.Values[at + 1].Value,
                        y = section.Values[at + 2].Value,
                        z = section.Values[at + 3].Value,
                    });
                }
        }
        if (section.Name.Equals("[special passive object]", StringComparison.OrdinalIgnoreCase))
        {
            var at = 0;
            while (at + 4 < section.Values.Count)
            {
                if (section.Values[at].Kind != PvfTokenKind.Int32) break;
                var code = section.Values[at++].Value;
                usedObjects.Add(code);
                var x = section.Values[at++].Value;
                var y = section.Values[at++].Value;
                var z = section.Values[at++].Value;
                var actions = section.Values[at++].Value;
                var actionRows = new List<object>();
                for (var action = 0; action < actions && at < section.Values.Count; action++)
                {
                    var kind = TextOf(section.Values[at++]);
                    if (at + 4 >= section.Values.Count) break;
                    var operands = section.Values.Skip(at).Take(5).Select(value => value.Value).ToArray();
                    if (kind.Equals("[monster]", StringComparison.OrdinalIgnoreCase)) usedMonsters.Add(operands[0]);
                    actionRows.Add(new { index = action, kind, operands });
                    at += 5;
                }
                specialRows.Add(new { code, x, y, z, actions = actionRows });
            }
        }
        if (section.Name.Equals("[monster]", StringComparison.OrdinalIgnoreCase))
        {
            var at = 0;
            while (at + 9 < section.Values.Count)
            {
                if (section.Values[at].Kind != PvfTokenKind.Int32) break;
                var values = section.Values.Skip(at).Take(8).Select(value => value.Value).ToArray();
                usedMonsters.Add(values[0]);
                at += 8;
                if (at >= section.Values.Count) break;
                var placement = TextOf(section.Values[at++]);
                var modifiers = new List<string>();
                while (at < section.Values.Count && TextOf(section.Values[at]) is "[dummy]" or "[npc]")
                {
                    var modifier = TextOf(section.Values[at++]);
                    modifiers.Add(modifier);
                    if (modifier == "[npc]" && at < section.Values.Count) at++;
                }
                if (at >= section.Values.Count) break;
                var rank = TextOf(section.Values[at++]);
                var features = new List<string>();
                if (rank is "[champion]" or "[super champion]" && at < section.Values.Count
                    && section.Values[at].Kind == PvfTokenKind.Int32)
                {
                    var count = section.Values[at++].Value;
                    for (var feature = 0; feature < Math.Max(0, count) && at < section.Values.Count; feature++)
                        features.Add(TextOf(section.Values[at++]));
                }
                monsterRows.Add(new
                {
                    code = values[0],
                    relativeLevel = values[1],
                    level = values[2],
                    x = values[3],
                    y = values[4],
                    z = values[5],
                    randomDrops = values[6],
                    fixedDrops = values[7],
                    placement,
                    modifiers,
                    rank,
                    championFeatures = features,
                });
            }
        }
    }
    mapRecords.Add(new
    {
        id,
        path,
        passiveObjects = passiveRows,
        monsters = monsterRows,
        specialObjects = specialRows,
        relevantSections = FileDump(path, relevantMapTags),
    });
}

var resolvedMonsters = usedMonsters.Order().Select(id =>
{
    var entry = monsterList.FirstOrDefault(value => value.Id == id);
    return new { id, relativePath = entry.Path, dump = entry.Path is null ? null : FileDump("monster/" + entry.Path) };
}).ToArray();
var resolvedObjects = usedObjects.Order().Select(id =>
{
    var entry = objectList.FirstOrDefault(value => value.Id == id);
    return new { id, relativePath = entry.Path, dump = entry.Path is null ? null : FileDump("passiveobject/" + entry.Path) };
}).ToArray();

object? ListedDump(List<(int Id, string Path)> list, string folder, int id)
{
    var entry = list.FirstOrDefault(value => value.Id == id);
    return entry.Path is null ? null : new { id, relativePath = entry.Path, dump = FileDump(folder + "/" + entry.Path) };
}

var customFiles = new List<object>();
for (var index = 0; index < archive.FileCount; index++)
{
    var entry = archive.GetEntry(index);
    if (!entry.IsScript) continue;
    var path = FullPath(index);
    if (!path.Contains("chn_species_dungeon", StringComparison.OrdinalIgnoreCase)) continue;
    if (!(path.EndsWith(".act", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".obj", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".mob", StringComparison.OrdinalIgnoreCase))) continue;
    customFiles.Add(FileDump(path)!);
}

var targets = new HashSet<int>(new[]
{
    3557, 2331, 10003195, 10003196, 10003235, 10003717, 15150, 20747, 20748, 20749,
});
var references = new List<object>();
for (var index = 0; index < archive.FileCount; index++)
{
    var entry = archive.GetEntry(index);
    if (!entry.IsScript) continue;
    var path = FullPath(index);
    PvfTokenReader reader;
    try { reader = archive.OpenScript(index); }
    catch { continue; }
    var tokens = new List<PvfToken>();
    try { while (reader.Read(out var token)) tokens.Add(token); }
    catch { continue; }
    string? section = null;
    for (var at = 0; at < tokens.Count; at++)
    {
        var token = tokens[at];
        if (token.Kind == PvfTokenKind.Identifier) section = TextOf(token);
        if (token.Kind != PvfTokenKind.Int32 || !targets.Contains(token.Value)) continue;
        var from = Math.Max(0, at - 5);
        var to = Math.Min(tokens.Count - 1, at + 5);
        references.Add(new
        {
            value = token.Value,
            path,
            tokenIndex = at,
            section,
            context = tokens.Skip(from).Take(to - from + 1).Select(TextOf).ToArray(),
        });
    }
}

var dungeonPath = "dungeon/" + dungeonList.First(value => value.Id == 3557).Path;
var result = new
{
    generatedAtUtc = DateTimeOffset.UtcNow,
    archive = new
    {
        path = archivePath,
        sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(archivePath))),
        fileCount = archive.FileCount,
    },
    dungeon = new { id = 3557, dump = FileDump(dungeonPath) },
    maps = mapRecords,
    usedMonsterIds = usedMonsters.Order().ToArray(),
    monsters = resolvedMonsters,
    usedPassiveObjectIds = usedObjects.Order().ToArray(),
    passiveObjects = resolvedObjects,
    pathgates = Enumerable.Range(621, 10).Select(id => ListedDump(objectList, "passiveobject", id)).ToArray(),
    items = new[] { 10003195, 10003196, 10003235, 10003717 }.Select(id => ListedDump(stackableList, "stackable", id)).ToArray(),
    quests = new[]
    {
        (Id: 20747, Path: "n_quest/event/chn_20140424_species_dungeon/chn_species_dungeon_daily_20747.qst"),
        (Id: 20748, Path: "n_quest/event/chn_20140424_species_dungeon/chn_species_dungeon_daily_20748.qst"),
        (Id: 20749, Path: "n_quest/event/chn_20140424_species_dungeon/chn_species_dungeon_daily_20749.qst"),
    }.Select(value => new
    {
        id = value.Id,
        path = value.Path,
        listed = questList.Any(entry => entry.Id == value.Id || string.Equals("n_quest/" + entry.Path, value.Path, StringComparison.OrdinalIgnoreCase)),
        dump = FileDump(value.Path),
    }).ToArray(),
    customBehaviorFiles = customFiles,
    references,
    dailyRefill = FileDump("etc/chn_server_limititemusageinfo.etc"),
    schedule = FileDump("etc/chn_schedule.etc"),
    worldMap = FileDump("worldmap/granflorishillside.wdm"),
};

Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(result, new JsonSerializerOptions
{
    WriteIndented = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
}));
Console.WriteLine($"wrote {outputPath}");
Console.WriteLine($"maps={mapRecords.Count} monsters={usedMonsters.Count} objects={usedObjects.Count} refs={references.Count} customFiles={customFiles.Count}");
return 0;
