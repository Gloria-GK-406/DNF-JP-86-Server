using System.Text;
using Dfo.Pvf;
using Dfo.Pvf.Tokens;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
Console.OutputEncoding = Encoding.UTF8;
using var archive = PvfArchive.Open(args[0]);
if (args[1] == "scan")
{
    var extensions = args[2].Split(',');
    var needles = args[3..];
    for (var i = 0; i < archive.FileCount; i++)
    {
        var entry = archive.GetEntry(i);
        var path = archive.ResolveString(entry.PathOffset) + "/" + archive.ResolveString(entry.NameOffset);
        if (!extensions.Any(ext => path.EndsWith(ext, StringComparison.OrdinalIgnoreCase))) continue;
        var reader = archive.OpenScript(i);
        var tokens = new List<string>();
        var integers = new HashSet<int>();
        while (reader.Read(out var token))
        {
            tokens.Add(Text(token));
            if (token.Kind == PvfTokenKind.Int32) integers.Add(token.Value);
        }
        var content = entry.IsUnicodeText ? archive.ReadUnicodeText(i) : string.Join(" ", tokens);
        bool Matches(string needle) => int.TryParse(needle, out var integer) ? integers.Contains(integer)
            : content.Contains(needle, StringComparison.OrdinalIgnoreCase);
        if (needles.Any(Matches))
            Console.WriteLine(path + "\t" + string.Join(" | ", needles.Where(Matches)));
    }
}
else
{
    foreach (var path in args[1..])
    {
        var i = archive.FindFile(path);
        Console.WriteLine("\n## " + path + "\n");
        if (i < 0) { Console.WriteLine("MISSING"); continue; }
        if (archive.GetEntry(i).IsUnicodeText) { Console.WriteLine(archive.ReadUnicodeText(i).TrimEnd('\0')); continue; }
        var reader = archive.OpenScript(i);
        var line = new StringBuilder();
        while (reader.Read(out var token))
        {
            var text = Text(token);
            if (token.Kind == PvfTokenKind.Identifier && text.StartsWith('['))
            {
                if (line.Length > 0) Console.WriteLine(line.ToString().TrimEnd());
                line.Clear();
                Console.WriteLine(text);
            }
            else line.Append(text).Append(' ');
        }
        if (line.Length > 0) Console.WriteLine(line.ToString().TrimEnd());
    }
}
string Text(PvfToken token) => token.IsString ? archive.ResolveString(token.Value)
    : token.Kind == PvfTokenKind.Single ? token.AsSingle().ToString(System.Globalization.CultureInfo.InvariantCulture)
    : token.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
