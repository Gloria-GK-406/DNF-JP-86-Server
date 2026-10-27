using System.Text;
using System.Text.RegularExpressions;
using Dfo.Pvf;
using Dfo.Pvf.Tokens;

// Task-local, read-only evidence scanner. Keeps at most one script in memory.
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
if (args[0] == "restore-entry")
{
    NativeEntrancePatch.Write(args[1], args[2]);
    return;
}
using var archive = PvfArchive.Open(args[0]);
Directory.CreateDirectory(args[1]);
var targets = new HashSet<int> { 3200, 3201, 3202, 3536, 3537, 3538, 3539, 3540, 20720, 20722, 20727, 20728, 10002519, 10002520 };
using var references = File.CreateText(Path.Combine(args[1], "pvf-references.txt"));
for (var i = 0; i < archive.FileCount; i++)
{
    var entry = archive.GetEntry(i);
    var path = archive.ResolveString(entry.PathOffset) + "/" + archive.ResolveString(entry.NameOffset);
    var extension = Path.GetExtension(path).ToLowerInvariant();
    var selected = Regex.IsMatch(path, @"^(town/.*shonan.*\.twn|worldmap/(ui/)?(chn|kor)_shonan_holy\.(wdm|xui)|n_quest/(dragon_kurun_kor|event/chn_20140121_dragondungeon)/.*\.qst|map/town/(chn|kor)_dragon_shonan_holyland/.*\.map)$", RegexOptions.IgnoreCase);
    var search = selected && extension is ".twn" or ".wdm" or ".qst";
    if (!search && !selected) continue;
    if (entry.IsUnicodeText)
    {
        if (selected) File.WriteAllText(Path.Combine(args[1], path.Replace('/', '_') + ".txt"), archive.ReadUnicodeText(i));
        continue;
    }
    if (!entry.IsScript) continue;
    var reader = archive.OpenScript(i);
    var lines = new StringBuilder();
    var hit = false;
    var tag = "";
    while (reader.Read(out var token))
    {
        var value = token.Kind == PvfTokenKind.Identifier || token.IsString ? archive.ResolveString(token.Value)
            : token.Kind == PvfTokenKind.Single ? BitConverter.Int32BitsToSingle(token.Value).ToString("R") : token.Value.ToString();
        if (token.Kind == PvfTokenKind.Identifier && value.StartsWith('[')) { tag = value; lines.AppendLine(); }
        lines.Append(value).Append(' ');
        if (search && token.Kind == PvfTokenKind.Int32 && targets.Contains(token.Value))
        { references.WriteLine($"{path}\t{tag}\t{token.Value}"); hit = true; }
    }
    if (selected || hit) File.WriteAllText(Path.Combine(args[1], path.Replace('/', '_') + ".txt"), lines.ToString());
}
