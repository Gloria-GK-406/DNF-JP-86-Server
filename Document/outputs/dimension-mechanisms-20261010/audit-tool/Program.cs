using System.Text;
using System.Text.Json;
using Dfo.Pvf;
using Dfo.Pvf.Schema;
using Dfo.Pvf.Tokens;
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
using var archive = PvfArchive.Open(args[0]);
string Text(PvfToken t) => t.IsString ? archive.ResolveString(t.Value) : t.Value.ToString();
List<(int Id, string RelativePath)> ReadList(string path)
{
    var rows = new List<(int, string)>();
    var reader = archive.OpenScript(path);
    int? id = null;
    while (reader.Read(out var token))
    {
        if (token.Kind == PvfTokenKind.Int32) id = token.Value;
        else if (id is { } key && token.IsString) { rows.Add((key, archive.ResolveString(token.Value))); id = null; }
    }
    return rows;
}
var dungeons = new Dictionary<int, string>();
foreach (var row in ReadList("dungeon/dungeon.lst"))
{
    var path = "dungeon/" + row.RelativePath;
    if (archive.FindFile(path) >= 0 && PvfDocument.Open(archive, path).Sections.Any(s => s.Name == "[dimension dungeon]"))
        dungeons.Add(row.Id, path);
}
var maps = new List<object>();
var objectIds = new HashSet<int>();
var monsterIds = new HashSet<int>();
foreach (var row in ReadList("map/map.lst"))
{
    var path = "map/" + row.RelativePath;
    if (archive.FindFile(path) < 0) continue;
    var sections = PvfDocument.Open(archive, path).Sections;
    var owners = sections.Where(s => s.Name == "[dungeon]").SelectMany(s => s.Values)
        .Where(t => t.Kind == PvfTokenKind.Int32 && dungeons.ContainsKey(t.Value)).Select(t => t.Value).Distinct().ToArray();
    if (owners.Length == 0) continue;
    var passive = sections.Where(s => s.Name == "[passive object]").SelectMany(s => s.Values).ToArray();
    var localObjects = new HashSet<int>();
    if (passive.Length % 4 != 0 || passive.Any(t => t.Kind != PvfTokenKind.Int32)) throw new InvalidDataException(path + ": unsupported passive layout");
    for (var i = 0; i < passive.Length; i += 4) localObjects.Add(passive[i].Value);
    var localMonsters = new HashSet<int>();
    foreach (var s in sections.Where(s => s.Name == "[special passive object]"))
    {
        var cursor = 0;
        while (cursor < s.Values.Count)
        {
            if (Text(s.Values[cursor]) == "[hellparty]") break;
            if (cursor + 5 > s.Values.Count || s.Values.Skip(cursor).Take(5).Any(t => t.Kind != PvfTokenKind.Int32))
                throw new InvalidDataException(path + ": unsupported special object header at " + cursor);
            localObjects.Add(s.Values[cursor].Value);
            var actionCount = s.Values[cursor + 4].Value;
            cursor += 5;
            for (var child = 0; child < actionCount; child++)
            {
                var kind = Text(s.Values[cursor++]);
                if (kind == "[hellparty]")
                {
                    while (cursor < s.Values.Count && Text(s.Values[cursor]) != "[/hellparty]") cursor++;
                    cursor++;
                    continue;
                }
                if (kind is not ("[monster]" or "[item]" or "[trap]" or "[quest]") || cursor + 5 > s.Values.Count || s.Values.Skip(cursor).Take(5).Any(t => t.Kind != PvfTokenKind.Int32))
                    throw new InvalidDataException(path + ": unsupported special object child at " + cursor);
                if (kind == "[monster]") localMonsters.Add(s.Values[cursor].Value);
                cursor += 5;
            }
        }
    }
    objectIds.UnionWith(localObjects);
    foreach (var s in sections.Where(s => s.Name is "[monster]" or "[monster condition]" or "[conditional summon monster]"))
        for (var i = 0; i + 8 < s.Values.Count; i++)
            if (s.Values.Skip(i).Take(8).All(t => t.Kind == PvfTokenKind.Int32) && Text(s.Values[i + 8]) is "[fixed]" or "[ratio]")
                localMonsters.Add(s.Values[i].Value);
    monsterIds.UnionWith(localMonsters);
    var selected = sections.Where(s => new[] { 16101, 15083, 17054, 17058, 39101, 39139, 53071 }.Contains(row.Id)).Where(s => !new[] { "[tile]", "[animation]", "[background animation]", "[ani info]", "[filename]", "[layer]", "[order]", "[/ani info]", "[/background animation]", "[player number]", "[pvp start area]", "[far sight scroll]", "[middle sight scroll]", "[near sight scroll]", "[sound]", "[/sound]" }.Contains(s.Name))
        .Select(s => new { Tag = s.Name, Values = s.Values.Select(Text).ToArray() }).ToArray();
    maps.Add(new { Id = row.Id, Path = path, Owners = owners, Objects = localObjects.Order().ToArray(), Monsters = localMonsters.Order().ToArray(), Sections = selected });
}
object[] References(string list, string folder, HashSet<int> ids) => ReadList(list).Where(r => ids.Contains(r.Id))
    .Select(r => (object)new { r.Id, Path = folder + r.RelativePath }).ToArray();
var result = new { Archive = args[0], ArchiveSHA256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(args[0]))), Scope = "Direct MAP object and monster declarations; not transitive OBJ/ACT dependencies. Sections retained for seven candidate MAPs only.", Dungeons = dungeons, MapCount = maps.Count, Maps = maps,
    Objects = References("passiveobject/passiveobject.lst", "passiveobject/", objectIds),
    Monsters = References("monster/monster.lst", "monster/", monsterIds) };
File.WriteAllText(args[1], JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }).Replace("\r\n", "\n") + "\n");
Console.WriteLine(JsonSerializer.Serialize(new { DungeonCount = dungeons.Count, MapCount = maps.Count, ObjectCount = objectIds.Count, MonsterCount = monsterIds.Count, Dungeons = dungeons }));

foreach (var path in ReadList("passiveobject/passiveobject.lst").Where(r => new[] { 61982, 61985, 10285, 10286, 14133, 10362 }.Contains(r.Id)).Select(r => "passiveobject/" + r.RelativePath))
{
    var sections = PvfDocument.Open(archive, path).Sections.Select(s => new { Tag = s.Name, Values = s.Values.Select(Text).ToArray() });
    File.WriteAllText(Path.Combine(Path.GetDirectoryName(args[1])!, Path.GetFileName(path) + ".json"), JsonSerializer.Serialize(new { Path = path, Sections = sections }, new JsonSerializerOptions { WriteIndented = true }).Replace("\r\n", "\n") + "\n");
}
