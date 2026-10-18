using System.Text;
using Dfo.Pvf;
using Dfo.Pvf.Tokens;

if (args.Length != 1)
{
    Console.Error.WriteLine("usage: PvfMarriageScanner <Script.pvf>");
    return 2;
}

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
Console.OutputEncoding = Encoding.UTF8;

var numericTargets = new HashSet<int> { 20737, 20738, 3541, 20011, 20012, 20013 };
var textNeedles = new[]
{
    "wedding", "couple", "婚礼", "结婚", "婚姻", "求婚", "配偶", "夫妻", "大司祭", "云清", "摩伽陀"
};
var textExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    ".etc", ".lst", ".npc", ".twn", ".map", ".dgn", ".qst", ".str", ".xui"
};

using var archive = PvfArchive.Open(args[0]);
var numericHits = 0;
var textHits = 0;
var unicodeHits = 0;

Console.WriteLine("section\tpath\tposition\tkind\tmatch\tcontext");
for (var fileIndex = 0; fileIndex < archive.FileCount; fileIndex++)
{
    var entry = archive.GetEntry(fileIndex);
    var folder = archive.ResolveString(entry.PathOffset);
    var name = archive.ResolveString(entry.NameOffset);
    var path = folder.Length == 0 ? name : folder + "/" + name;

    if (entry.IsUnicodeText)
    {
        var text = archive.ReadUnicodeText(fileIndex);
        foreach (var needle in textNeedles)
        {
            var start = 0;
            while ((start = text.IndexOf(needle, start, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                var contextStart = Math.Max(0, start - 80);
                var contextLength = Math.Min(text.Length - contextStart, needle.Length + 160);
                Console.WriteLine($"unicode\t{Escape(path)}\t{start}\ttext\t{Escape(needle)}\t{Escape(text.Substring(contextStart, contextLength))}");
                unicodeHits++;
                start += Math.Max(1, needle.Length);
            }
        }
        continue;
    }

    if (!entry.IsScript)
        continue;

    var extension = Path.GetExtension(name);
    var scanText = textExtensions.Contains(extension)
        || path.Contains("wedding", StringComparison.OrdinalIgnoreCase)
        || path.Contains("couple", StringComparison.OrdinalIgnoreCase);

    var tokens = new List<(PvfToken Token, string Text)>();
    var reader = archive.OpenScript(fileIndex);
    while (reader.Read(out var token))
    {
        var tokenText = token.Kind == PvfTokenKind.Identifier || token.IsString
            ? archive.ResolveString(token.Value)
            : token.Kind == PvfTokenKind.Single
                ? BitConverter.Int32BitsToSingle(token.Value).ToString("0.######")
                : token.Value.ToString();
        tokens.Add((token, tokenText));
    }

    for (var i = 0; i < tokens.Count; i++)
    {
        var current = tokens[i];
        if (current.Token.Kind == PvfTokenKind.Int32 && numericTargets.Contains(current.Token.Value))
        {
            Console.WriteLine($"number\t{Escape(path)}\t{i + 1}\t{current.Token.Kind}\t{current.Token.Value}\t{Context(tokens, i)}");
            numericHits++;
        }

        if (!scanText || !(current.Token.Kind == PvfTokenKind.Identifier || current.Token.IsString))
            continue;

        foreach (var needle in textNeedles)
        {
            if (!current.Text.Contains(needle, StringComparison.OrdinalIgnoreCase))
                continue;
            Console.WriteLine($"token-text\t{Escape(path)}\t{i + 1}\t{current.Token.Kind}\t{Escape(needle)}\t{Context(tokens, i)}");
            textHits++;
        }
    }
}

Console.Error.WriteLine($"numericHits={numericHits}; tokenTextHits={textHits}; unicodeHits={unicodeHits}; files={archive.FileCount}");
return 0;

static string Context(List<(PvfToken Token, string Text)> tokens, int index)
{
    var start = Math.Max(0, index - 6);
    var end = Math.Min(tokens.Count - 1, index + 6);
    return Escape(string.Join(" ", tokens.GetRange(start, end - start + 1).Select(value => value.Text)));
}

static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\r", "\\r").Replace("\n", "\\n");
