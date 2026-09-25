using System.Text;
using System.Text.Json;
using Dfo.Pvf;
using Dfo.Pvf.Tokens;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
var path = Environment.GetEnvironmentVariable("PVF_ARCHIVE_PATH")
    ?? throw new ArgumentException("PVF_ARCHIVE_PATH required");
using var archive = PvfArchive.Open(path);
var wanted = new HashSet<string>(StringComparer.Ordinal)
{
    "[name]", "[grade]", "[type]", "[sub type]", "[int data]",
    "[enemy reward item]", "[monster reward item]", "[depend give item]",
    "[condition message]", "[depend message]", "[creature kind]", "[level]",
    "[reward int data]", "[reward selection int data]", "[target character]"
};
var listed = ReadList(archive, "n_quest/quest.lst")
    .GroupBy(x => "n_quest/" + x.RelativePath.Replace('\\', '/'), StringComparer.OrdinalIgnoreCase)
    .ToDictionary(x => x.Key, x => x.Select(y => y.Id).ToArray(), StringComparer.OrdinalIgnoreCase);
var records = new List<object>();
var targetIds = new HashSet<int>();
var dungeonIds = new HashSet<int>();
var itemIds = new HashSet<int>();
var scanned = 0;
var allEnemy = 0;
var allMonster = 0;
for (var i = 0; i < archive.FileCount; i++)
{
    var entry = archive.GetEntry(i);
    if (!entry.IsScript || !archive.ResolveString(entry.NameOffset).EndsWith(".qst", StringComparison.OrdinalIgnoreCase))
        continue;
    scanned++;
    var folder = archive.ResolveString(entry.PathOffset);
    var name = archive.ResolveString(entry.NameOffset);
    var file = folder.Length > 0 ? folder + "/" + name : name;
    var reader = archive.OpenScript(i);
    var sections = new Dictionary<string, List<List<string>>>(StringComparer.Ordinal);
    string? active = null;
    while (reader.Read(out var token))
    {
        if (token.Kind == PvfTokenKind.Identifier)
        {
            var id = archive.ResolveString(token.Value);
            if (id == "[enemy reward item]") allEnemy++;
            if (id == "[monster reward item]") allMonster++;
            if (wanted.Contains(id))
            {
                active = id;
                if (!sections.TryGetValue(id, out var occurrences))
                    sections[id] = occurrences = [];
                occurrences.Add([]);
                continue;
            }
            if (id.StartsWith('['))
            {
                if (active is "[grade]" or "[type]" && sections[active][^1].Count == 0)
                    sections[active][^1].Add(id);
                active = null;
                continue;
            }
        }
        if (active is not null)
        {
            var value = token.IsString ? archive.ResolveString(token.Value)
                : token.Kind == PvfTokenKind.Single ? token.AsSingle().ToString("R") : token.Value.ToString();
            sections[active][^1].Add(value);
        }
    }
    if (sections.ContainsKey("[enemy reward item]"))
    {
        foreach (var occurrence in sections["[enemy reward item]"])
            for (var field = 0; field + 7 < occurrence.Count; field += 8)
            {
                targetIds.Add(int.Parse(occurrence[field]));
                dungeonIds.Add(int.Parse(occurrence[field + 2]));
                itemIds.Add(int.Parse(occurrence[field + 4]));
            }
        listed.TryGetValue(file, out var ids);
        records.Add(new { path = file, questIds = ids ?? [], sections });
    }
}
var lookups = new Dictionary<string, Dictionary<int, string>>();
foreach (var (key, listPath, wantedIds) in new[] {
    ("monster", "monster/monster.lst", targetIds),
    ("aicharacter", "aicharacter/aicharacter.lst", targetIds),
    ("passiveobject", "passiveobject/passiveobject.lst", targetIds),
    ("dungeon", "dungeon/dungeon.lst", dungeonIds),
    ("stackable", "stackable/stackable.lst", itemIds) })
    lookups[key] = ReadList(archive, listPath).Where(x => wantedIds.Contains(x.Id))
        .GroupBy(x => x.Id).ToDictionary(x => x.Key, x => x.First().RelativePath);
var result = new { archive = path, scannedQuestFiles = scanned, enemyTagCount = allEnemy,
    monsterTagCount = allMonster, enemyFiles = records.Count, lookups, records };
var output = Path.GetFullPath("Document/outputs/enemy-reward-investigation/scan.json");
File.WriteAllText(output, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
Console.WriteLine($"{output}: {scanned} quest files, enemy tags {allEnemy}, monster tags {allMonster}, enemy files {records.Count}");

static List<(int Id, string RelativePath)> ReadList(PvfArchive archive, string path)
{
    var reader = archive.OpenScript(path);
    var entries = new List<(int, string)>();
    var pending = int.MinValue;
    while (reader.Read(out var token))
    {
        if (token.Kind == PvfTokenKind.Int32)
            pending = token.Value;
        else if (token.IsString)
        {
            entries.Add((pending, archive.ResolveString(token.Value).Trim('`')));
            pending = int.MinValue;
        }
    }
    return entries;
}
