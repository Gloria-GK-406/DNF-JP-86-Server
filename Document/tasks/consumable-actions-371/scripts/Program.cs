using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dfo.Pvf;
using Dfo.Pvf.Schema;
using Dfo.Pvf.Tokens;

// Read-only survey. No game process, client component, or player database is changed.
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
Console.OutputEncoding = Encoding.UTF8;
if (args.Length != 2) throw new ArgumentException("Survey <Script.pvf> <output.json>");
using var archive = PvfArchive.Open(args[0]);
var list = archive.OpenScript("stackable/stackable.lst");
var entries = new List<(int Id, string Path)>();
var id = int.MinValue;
while (list.Read(out var token))
{
    if (token.Kind == PvfTokenKind.Int32) id = token.Value;
    else if (token.IsString)
    {
        entries.Add((id, "stackable/" + archive.ResolveString(token.Value).Trim('`')));
        id = int.MinValue;
    }
}
string Text(PvfToken token) => token.Kind == PvfTokenKind.Identifier || token.IsString
    ? archive.ResolveString(token.Value)
    : token.Kind == PvfTokenKind.Single
        ? BitConverter.Int32BitsToSingle(token.Value).ToString(System.Globalization.CultureInfo.InvariantCulture)
        : token.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
var items = new List<object>();
var errors = new List<object>();
foreach (var entry in entries)
{
    try
    {
        var document = PvfDocument.Open(archive, entry.Path);
        var roots = document.AtRoot().Sections;
        var action = roots.FirstOrDefault(section => section.Name == "[action type]");
        if (action is null) continue;
        string[] Field(string name) => roots.FirstOrDefault(section => section.Name == name)?.Values.Select(Text).ToArray() ?? [];
        items.Add(new
        {
            entry.Id, entry.Path, Action = action.Values.Select(Text).ToArray(),
            Name = Field("[name]"), Type = Field("[stackable type]"), RootTags = roots.Select(section => section.Name).ToArray(),
            MinimumLevel = Field("[minimum level]"), MaximumLevel = Field("[maximum level]"),
            Expiration = Field("[expiration date]"), Explanation = Field("[explain]"),
            Sections = document.Sections.Where(section => new[]
            {
                "[action type]", "[use action packet]", "[need material]", "[usable count]",
                "[action usable place]", "[usable place]", "[cool time]", "[cool time group]",
                "[quest]", "[quest clear]", "[int data]", "[string data]", "[icon]",
                "[expiration date]", "[impossible contents]", "[levelup range]"
            }.Contains(section.Name)).Select(section => new { section.Name, Values = section.Values.Select(Text).ToArray() }).ToArray()
        });
    }
    catch (Exception error) { errors.Add(new { entry.Id, entry.Path, Error = error.Message }); }
}
var result = new
{
    Archive = Path.GetFullPath(args[0]), Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(args[0]))).ToLowerInvariant(),
    ListedItems = entries.Count, ActionItems = items.Count, Errors = errors, Items = items,
    AuxiliaryScripts = new[] { "etc/amplifyitem.etc" }.Select(path => new
    {
        Path = path, Sections = PvfDocument.Open(archive, path).Sections.Select(section =>
            new { section.Name, Values = section.Values.Select(Text).ToArray() }).ToArray()
    }).ToArray()
};
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1]))!);
File.WriteAllText(args[1], JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true })
    .Replace("\r\n", "\n", StringComparison.Ordinal) + "\n", new UTF8Encoding(false));
Console.WriteLine($"listed={entries.Count} actionItems={items.Count} errors={errors.Count} output={args[1]}");
