using System.Security.Cryptography;
using System.Text;
using Dfo.Pvf;
using Dfo.Pvf.Schema;
using Dfo.Pvf.Tokens;
using Dfo.Server.Infrastructure.Content;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
Console.OutputEncoding = Encoding.UTF8;
var path = Environment.GetEnvironmentVariable("PVF_ARCHIVE_PATH") ?? throw new InvalidOperationException("PVF_ARCHIVE_PATH required");
using (var input = File.OpenRead(path)) Console.WriteLine($"PVF SHA256: {Convert.ToHexString(SHA256.HashData(input))}");
using var archive = PvfArchive.Open(path);
string Text(PvfToken token) => token.IsString ? archive.ResolveString(token.Value) : token.Value.ToString();
var townAreas = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
foreach (var town in PvfListFile.Read(archive, "town/town.lst"))
{
    var document = PvfDocument.Open(archive, "town/" + town.RelativePath);
    foreach (var section in document.Sections.Where(x => x.Name == "[area]"))
    {
        if (section.Values.Count < 2) continue;
        var mapPath = "map/" + Text(section.Values[1]);
        if (!townAreas.TryGetValue(mapPath, out var list)) townAreas[mapPath] = list = [];
        list.Add($"town={town.Id}, area={section.Values[0].Value}, source=town/{town.RelativePath}");
    }
}
var scanned = 0;
for (var index = 0; index < archive.FileCount; index++)
{
    var entry = archive.GetEntry(index);
    var folder = archive.ResolveString(entry.PathOffset);
    var name = archive.ResolveString(entry.NameOffset);
    var file = folder + "/" + name;
    if (!entry.IsScript || !file.StartsWith("map/town/", StringComparison.OrdinalIgnoreCase)
        || !file.EndsWith(".map", StringComparison.OrdinalIgnoreCase)) continue;
    scanned++;
    var document = PvfDocument.Open(archive, file);
    foreach (var section in document.Sections.Where(x => x.Name.Equals("[NPC]", StringComparison.OrdinalIgnoreCase)))
    {
        var rows = section.Values;
        if (rows.Count % 5 != 0) throw new InvalidDataException($"Unexpected NPC row width: {file}");
        for (var i = 0; i < rows.Count; i += 5)
        {
            if (rows[i].Kind != PvfTokenKind.Int32 || rows[i].Value != 161) continue;
            Console.WriteLine($"NPC 161: {file}, {string.Join(' ', rows.Skip(i).Take(5).Select(Text))}");
            if (townAreas.TryGetValue(file, out var locations)) foreach (var location in locations) Console.WriteLine($"  {location}");
            else Console.WriteLine("  Not referenced by current town/town.lst area declarations");
        }
    }
}
Console.WriteLine($"Scanned {scanned} town maps, {townAreas.Count} referenced area maps.");
