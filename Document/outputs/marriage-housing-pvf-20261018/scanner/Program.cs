using System.Text;
using Dfo.Pvf;
using Dfo.Pvf.Index;
using Dfo.Pvf.Tokens;

if (args.Length != 2)
{
    Console.Error.WriteLine("usage: HousingPvfScanner <Script.pvf> <output-directory>");
    return 2;
}

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
Console.OutputEncoding = Encoding.UTF8;
Directory.CreateDirectory(args[1]);
using var archive = PvfArchive.Open(args[0]);
var stackables = ReadList("stackable/stackable.lst")
    .ToDictionary(entry => entry.Id, entry => "stackable/" + entry.Path);

var furnitureShop = Tokens("itemshop/chn_mago.shp");
var shopItems = ValuesAfter(furnitureShop, "[item list]").Select(token => token.Value).ToArray();
var tabs = ValuesAfter(furnitureShop, "[tab name]").Select(Text).ToArray();
var tabByItem = new Dictionary<int, string>();
var tabIndex = 0;
foreach (var value in shopItems)
{
    if (value == -2) { tabIndex++; continue; }
    if (value > 0) tabByItem[value] = tabIndex < tabs.Length ? tabs[tabIndex] : $"tab-{tabIndex}";
}

var decoration = Tokens("decoration/coupleroomiteminfo.etc");
var placements = RepeatedValues(decoration, "[coupleroom item]")
    .ToDictionary(values => values[0].Value, values => values);
var rows = new List<string>
{
    "item_id\tname\tshop_tab\tmaterial_id\tmaterial_cost\tplacement_count\tposition_indices\tposition_details\taction_path\titem_path"
};
foreach (var (itemId, tab) in tabByItem.OrderBy(pair => Array.IndexOf(shopItems, pair.Key)))
{
    var itemPath = stackables[itemId];
    var tokens = Tokens(itemPath);
    var name = FirstText(tokens, "[name]");
    var material = ValuesAfter(tokens, "[need material]");
    var values = placements[itemId];
    var count = values[1].Value;
    var position = new List<string>();
    for (var i = 0; i < count; i++)
    {
        var offset = 2 + i * 5;
        position.Add(string.Join(',', values.Skip(offset).Take(5).Select(v => v.Value)));
    }
    var action = values.LastOrDefault(token => token.IsString) is { IsString: true } actionToken ? Text(actionToken) : "";
    rows.Add(string.Join('\t', itemId, Escape(name), Escape(tab),
        material.Count > 0 ? material[0].Value : 0,
        material.Count > 1 ? material[1].Value : 0,
        count,
        string.Join(',', position.Select(group => group.Split(',')[0])),
        string.Join(';', position), Escape(action), Escape(itemPath)));
}
File.WriteAllLines(Path.Combine(args[1], "furniture.tsv"), rows, new UTF8Encoding(false));

var targetIds = new HashSet<int>(tabByItem.Keys)
{
    10002606, 10002901, 10002765, 10002905, 10002766, 10002767, 10002768,
    10002609, 10002758, 10002910, 10002915, 10002611, 10002612, 10002613, 10002614, 10002615,
};
var references = new List<string> { "path\ttoken\titem_id\tcontext" };
for (var fileIndex = 0; fileIndex < archive.FileCount; fileIndex++)
{
    var entry = archive.GetEntry(fileIndex);
    if (!entry.IsScript) continue;
    var path = PathOf(entry);
    var tokens = TokensByIndex(fileIndex);
    for (var i = 0; i < tokens.Count; i++)
    {
        if (tokens[i].Kind != PvfTokenKind.Int32 || !targetIds.Contains(tokens[i].Value)) continue;
        var begin = Math.Max(0, i - 8);
        var end = Math.Min(tokens.Count, i + 9);
        references.Add($"{Escape(path)}\t{i + 1}\t{tokens[i].Value}\t{Escape(string.Join(' ', tokens.Skip(begin).Take(end - begin).Select(Text)))}");
    }
}
File.WriteAllLines(Path.Combine(args[1], "target-references.tsv"), references, new UTF8Encoding(false));

var needles = new[] { "婚房", "保管箱", "爱情树", "夫妻赠礼", "爱情成长胶囊", "传送", "配偶", "扩张" };
var textHits = new List<string> { "path\tposition\tneedle\tcontext" };
for (var fileIndex = 0; fileIndex < archive.FileCount; fileIndex++)
{
    var entry = archive.GetEntry(fileIndex);
    var path = PathOf(entry);
    if (entry.IsUnicodeText)
    {
        var content = archive.ReadUnicodeText(fileIndex);
        AddTextHits(path, content);
    }
    else if (entry.IsScript)
    {
        var tokens = TokensByIndex(fileIndex);
        for (var i = 0; i < tokens.Count; i++)
        {
            if (!tokens[i].IsString) continue;
            var content = Text(tokens[i]);
            foreach (var needle in needles)
                if (content.Contains(needle, StringComparison.OrdinalIgnoreCase))
                    textHits.Add($"{Escape(path)}\ttoken-{i + 1}\t{needle}\t{Escape(Context(content, content.IndexOf(needle, StringComparison.OrdinalIgnoreCase)))}");
        }
    }
}
File.WriteAllLines(Path.Combine(args[1], "text-hits.tsv"), textHits, new UTF8Encoding(false));

var keyPaths = new[]
{
    "town/coupleroom.twn", "map/coupleroom/coupleroom.map", "npc/chn_mago.npc",
    "npc/chn_coupleroom_tree.npc", "itemshop/chn_mago.shp", "itemshop/chn_mago_normal.shp",
    "decoration/coupleroomiteminfo.etc", "decoration/coupletree.etc",
    "decoration/growtree/coupletreeiteminfo.etc", "etc/couplegrowthcapsule.etc", "etc/weddingsystem.etc",
};
var dumps = new List<string>();
var town101 = ReadList("town/town.lst").Single(entry => entry.Id == 101);
dumps.Add("## town/town.lst#101");
dumps.Add($"101 -> town/{town101.Path}");
dumps.Add("");
foreach (var path in keyPaths)
{
    dumps.Add($"## {path}");
    dumps.Add(FormatTokens(Tokens(path)));
    dumps.Add("");
}
foreach (var id in targetIds.Where(id => !tabByItem.ContainsKey(id)).Order())
{
    if (!stackables.TryGetValue(id, out var path)) continue;
    dumps.Add($"## {id} -> {path}");
    dumps.Add(FormatTokens(Tokens(path)));
    dumps.Add("");
}
File.WriteAllLines(Path.Combine(args[1], "key-pvf-dumps.md"), dumps, new UTF8Encoding(false));

var configuredOnly = placements.Keys.Except(tabByItem.Keys).Order().ToArray();
var shopOnly = tabByItem.Keys.Except(placements.Keys).Order().ToArray();
Console.WriteLine($"shopPositiveTokens={shopItems.Count(value => value > 0)}; shopFurniture={tabByItem.Count}; configuredFurniture={placements.Count}; "
    + $"configuredOnly={string.Join(',', configuredOnly)}; shopOnly={string.Join(',', shopOnly)}; "
    + $"tabs={string.Join(',', tabs.Select(tab => $"{tab}:{tabByItem.Count(x => x.Value == tab)}"))}; "
    + $"references={references.Count - 1}; textHits={textHits.Count - 1}");
return 0;

void AddTextHits(string path, string content)
{
    foreach (var needle in needles)
    {
        var start = 0;
        while ((start = content.IndexOf(needle, start, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            textHits.Add($"{Escape(path)}\t{start}\t{needle}\t{Escape(Context(content, start))}");
            start += needle.Length;
        }
    }
}

string PathOf(PvfFileEntry entry)
{
    var folder = archive.ResolveString(entry.PathOffset);
    var name = archive.ResolveString(entry.NameOffset);
    return folder.Length == 0 ? name : folder + "/" + name;
}

List<PvfToken> Tokens(string path)
{
    var index = archive.FindFile(path);
    if (index < 0) throw new InvalidDataException($"Missing {path}");
    return TokensByIndex(index);
}

List<PvfToken> TokensByIndex(int index)
{
    var values = new List<PvfToken>();
    var reader = archive.OpenScript(index);
    while (reader.Read(out var token)) values.Add(token);
    return values;
}

List<(int Id, string Path)> ReadList(string path)
{
    var entries = new List<(int, string)>();
    var reader = archive.OpenScript(path);
    var pending = int.MinValue;
    while (reader.Read(out var token))
    {
        if (token.Kind == PvfTokenKind.Int32) { pending = token.Value; continue; }
        if (!token.IsString) continue;
        entries.Add((pending, archive.ResolveString(token.Value).Trim('`')));
        pending = int.MinValue;
    }
    return entries;
}

List<PvfToken> ValuesAfter(List<PvfToken> tokens, string field)
{
    var start = tokens.FindIndex(token => token.Kind == PvfTokenKind.Identifier && Text(token) == field);
    if (start < 0) return [];
    var values = new List<PvfToken>();
    for (var i = start + 1; i < tokens.Count && tokens[i].Kind != PvfTokenKind.Identifier; i++) values.Add(tokens[i]);
    return values;
}

List<List<PvfToken>> RepeatedValues(List<PvfToken> tokens, string field)
{
    var result = new List<List<PvfToken>>();
    for (var i = 0; i < tokens.Count; i++)
    {
        if (tokens[i].Kind != PvfTokenKind.Identifier || Text(tokens[i]) != field) continue;
        var values = new List<PvfToken>();
        for (i++; i < tokens.Count && tokens[i].Kind != PvfTokenKind.Identifier; i++) values.Add(tokens[i]);
        result.Add(values);
        i--;
    }
    return result;
}

string FirstText(List<PvfToken> tokens, string field) => ValuesAfter(tokens, field).Select(Text).FirstOrDefault() ?? "";
string Text(PvfToken token) => token.Kind switch
{
    PvfTokenKind.Identifier => archive.ResolveString(token.Value),
    _ when token.IsString => archive.ResolveString(token.Value),
    PvfTokenKind.Single => BitConverter.Int32BitsToSingle(token.Value).ToString("0.######"),
    _ => token.Value.ToString(),
};
string FormatTokens(List<PvfToken> tokens) => string.Join(' ', tokens.Select(Text));
static string Context(string value, int at)
{
    var begin = Math.Max(0, at - 80);
    return value.Substring(begin, Math.Min(value.Length - begin, 200));
}
static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\r", "\\r").Replace("\n", "\\n");
