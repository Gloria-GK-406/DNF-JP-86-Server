using System.Text;
using Dfo.Pvf;
using Dfo.Pvf.Tokens;

// Ad-hoc reader for Script.pvf, for the questions that come up while working out a protocol:
// what does this item declare, what values does this tag take across the whole archive, which
// files carry a given one. The server's content sources answer the questions the server needs;
// this answers the ones a person needs before deciding what the server should ask.
//
// Deliberately not in the solution, and deliberately printing rather than asserting: its output
// belongs in a probe note under protocol/patterns/data/probes/, not in a test.

const string ArchiveVariable = "PVF_ARCHIVE_PATH";

var archivePath = Environment.GetEnvironmentVariable(ArchiveVariable);
if (string.IsNullOrWhiteSpace(archivePath))
{
    Console.Error.WriteLine($"Set {ArchiveVariable} to the Script.pvf to read.");
    return 2;
}

if (args.Length == 0)
{
    Usage();
    return 2;
}

// GBK, which is what the archive's own strings are. Without it every name reads as mojibake and
// a probe that is looking for an item by name finds nothing.
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
Console.OutputEncoding = Encoding.UTF8;

using var archive = PvfArchive.Open(archivePath);

switch (args[0])
{
    case "dump":
        Require(args, 2);
        Dump(archive, args[1], args.Length > 2 ? int.Parse(args[2]) : 400);
        return 0;

    case "tokens":
    {
        // Kind and interned offset alongside the text. `dump` shows what a section says; this
        // shows what a reader matching on tags and integers will actually see, which is the
        // difference that decides whether a parser written against the printed form works.
        Require(args, 2);
        var index = archive.FindFile(args[1]);
        if (index < 0)
        {
            Console.WriteLine($"!! not found: {args[1]}");
            return 1;
        }

        var limit = args.Length > 2 ? int.Parse(args[2]) : 200;
        var skip = args.Length > 3 ? int.Parse(args[3]) : 0;
        var script = archive.OpenScript(index);
        var seen = 0;
        while (script.Read(out var token) && seen < skip + limit)
        {
            seen++;
            if (seen <= skip)
                continue;
            Console.WriteLine($"{seen}	{token.Kind}	string={token.IsString}	"
                              + $"value={token.Value}	{TokenText(archive, token)}");
        }

        return 0;
    }

    case "files":
    {
        // The whole file table. Every other command reaches a file through a path or a .lst,
        // which can only find what something already declares; this is what answers "is there
        // anything of this kind in here at all".
        var needle = args.Length > 1 ? args[1] : null;
        var shown = 0;
        for (var i = 0; i < archive.FileCount; i++)
        {
            var entry = archive.GetEntry(i);
            var folder = archive.ResolveString(entry.PathOffset);
            var name = archive.ResolveString(entry.NameOffset);
            var path = folder.Length > 0 ? folder + "/" + name : name;
            if (needle is not null && !path.Contains(needle, StringComparison.OrdinalIgnoreCase))
                continue;

            Console.WriteLine($"{entry.DataType}\t{entry.DataSize}\t{path}");
            shown++;
        }

        Console.Error.WriteLine($"{shown} of {archive.FileCount} file(s)");
        return 0;
    }

    case "pool":
    {
        // The string pools outlive the files that used them: a path whose file was stripped at
        // pack time stays interned. Named in the pool but absent from the file table is what
        // separates "this subsystem was removed" from "this subsystem was never built".
        var needle = args.Length > 1 ? args[1] : null;
        var utf8 = WalkUtf8Pool(archive, needle);
        var utf16 = WalkUtf16Pool(archive, needle);
        Console.Error.WriteLine($"{utf8} + {utf16} entries shown");
        return 0;
    }

    case "lst":
    {
        Require(args, 3);
        var wanted = int.Parse(args[2]);
        var entry = ReadList(archive, args[1]).FirstOrDefault(value => value.Id == wanted);
        if (entry.Path is null)
        {
            Console.WriteLine($"!! id {wanted} is not listed in {args[1]}");
            return 1;
        }

        Console.WriteLine($"id {wanted} -> {entry.Path}");
        Dump(archive, FolderOf(args[1]) + entry.Path, args.Length > 3 ? int.Parse(args[3]) : 400);
        return 0;
    }

    case "tags":
    {
        // A .lst answers "what vocabulary do these particular files use"; a bare extension
        // answers "what can this file type express at all", which is the question you ask before
        // deciding whether a feature can be built out of data instead of code.
        Require(args, 2);
        var counts = new Dictionary<string, int>();
        var scanned = args[1].EndsWith(".lst", StringComparison.OrdinalIgnoreCase)
            ? ForEachListedFile(archive, args[1], script => CountTags(archive, script, counts))
            : ForEachFileOfType(archive, args[1], script => CountTags(archive, script, counts));

        Console.WriteLine($"tag vocabulary over {scanned} files:");
        if (scanned == 0)
            Console.WriteLine("!! nothing matched; only token scripts carry tags, .lua and .xui are UTF-16 text");
        Report(counts);
        return 0;
    }

    case "values":
    {
        Require(args, 3);
        var wantedTag = args[2];
        var counts = new Dictionary<string, int>();
        var scanned = ForEachListedFile(archive, args[1], script =>
        {
            // The token immediately after the tag, whatever its kind. Bracketed enum values such
            // as [waste] are identifiers too, so a scanner that treats every bracket as a tag
            // boundary reports every one of them as empty.
            var armed = false;
            var recorded = false;
            while (script.Read(out var token))
            {
                var text = TokenText(archive, token);
                if (armed)
                {
                    counts[text] = counts.GetValueOrDefault(text) + 1;
                    recorded = true;
                    break;
                }

                if (token.Kind == PvfTokenKind.Identifier && text == wantedTag)
                    armed = true;
            }

            if (!recorded)
                counts["(absent)"] = counts.GetValueOrDefault("(absent)") + 1;
        });

        Console.WriteLine($"first token after {wantedTag} over {scanned} files:");
        Report(counts);
        return 0;
    }

    case "where":
    {
        Require(args, 4);
        var wantedTag = args[2];
        var wantedValue = args[3];
        var limit = args.Length > 4 ? int.Parse(args[4]) : 40;
        var hits = 0;
        var folder = FolderOf(args[1]);

        foreach (var (id, relativePath) in ReadList(archive, args[1]))
        {
            if (hits >= limit)
            {
                Console.WriteLine($"-- stopped at {limit}; pass a larger limit to see the rest");
                break;
            }

            var index = archive.FindFile(folder + relativePath);
            if (index < 0)
                continue;

            var script = archive.OpenScript(index);
            var armed = false;
            while (script.Read(out var token))
            {
                var text = TokenText(archive, token);
                if (armed)
                {
                    if (text == wantedValue)
                    {
                        Console.WriteLine($"id={id}  {folder}{relativePath}");
                        hits++;
                    }

                    break;
                }

                if (token.Kind == PvfTokenKind.Identifier && text == wantedTag)
                    armed = true;
            }
        }

        Console.WriteLine($"-- {hits} file(s)");
        return 0;
    }

    case "name":
    {
        Require(args, 3);
        var needle = args[2];
        var limit = args.Length > 3 ? int.Parse(args[3]) : 40;
        var hits = 0;
        var folder = FolderOf(args[1]);

        foreach (var (id, relativePath) in ReadList(archive, args[1]))
        {
            if (hits >= limit)
            {
                Console.WriteLine($"-- stopped at {limit}; pass a larger limit to see the rest");
                break;
            }

            var index = archive.FindFile(folder + relativePath);
            if (index < 0)
                continue;

            var script = archive.OpenScript(index);
            var armed = false;
            while (script.Read(out var token))
            {
                var text = TokenText(archive, token);
                if (armed)
                {
                    if (text.Contains(needle, StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine($"id={id}  {text}  ({folder}{relativePath})");
                        hits++;
                    }

                    break;
                }

                if (token.Kind == PvfTokenKind.Identifier && text == "[name]")
                    armed = true;
            }
        }

        Console.WriteLine($"-- {hits} file(s)");
        return 0;
    }

    default:
        Usage();
        return 2;
}

static void Usage()
{
    Console.Error.WriteLine("""
        PvfProbe <command>, with PVF_ARCHIVE_PATH pointing at a Script.pvf.

          dump   <path> [maxTokens]                  print one script file
          lst    <listPath> <id> [maxTokens]         resolve an id through a .lst, then print it
          tags   <listPath|ext>                      tag vocabulary: one .lst, or a file type
          values <listPath> <tag>                    the value each listed file gives that tag
          where  <listPath> <tag> <value> [limit]    the files whose tag has that value
          name   <listPath> <substring> [limit]      the files whose [name] contains a substring
          files  [substring]                         every file-table row: dataType, size, path
          pool   [substring]                         every interned string, orphans included

        Example:
          PvfProbe values stackable/stackable.lst "[stackable type]"
          PvfProbe files .lua
        """);
}

static void Require(string[] args, int count)
{
    if (args.Length < count)
    {
        Usage();
        Environment.Exit(2);
    }
}

static string FolderOf(string listPath) => listPath[..(listPath.LastIndexOf('/') + 1)];

// Both pools are flat runs of NUL-terminated strings, so the way to the next one is to step over
// this one. Neither pool publishes its length, so the walk ends on a long run of empty reads —
// and says so on stderr, because a probe whose output becomes evidence must never truncate
// quietly. A pool holding 64 consecutive empty strings would end the walk early; none observed
// so far comes close.
//
// Even offsets index the UTF-8 pool. The stride is the byte count, not the string length: a
// decoded GBK or CJK name is shorter in chars than in bytes, and stepping by chars desynchronises
// the walk on the first non-ASCII entry.
static int WalkUtf8Pool(PvfArchive archive, string? needle)
{
    const int EmptyRunLimit = 64;
    var position = 0;
    var empties = 0;
    var shown = 0;
    while (empties < EmptyRunLimit)
    {
        var bytes = archive.Names.Utf8Bytes(position << 1);
        if (bytes.IsEmpty)
        {
            empties++;
            position++;
            continue;
        }

        empties = 0;
        var text = Encoding.UTF8.GetString(bytes);
        if (needle is null || text.Contains(needle, StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"u8\t{position << 1}\t{text}");
            shown++;
        }

        position += bytes.Length + 1;
    }

    Console.Error.WriteLine($"u8 pool: walked to byte {position}");
    return shown;
}

// Odd offsets index the UTF-16 pool, where the stride is the char count.
static int WalkUtf16Pool(PvfArchive archive, string? needle)
{
    const int EmptyRunLimit = 64;
    var position = 0;
    var empties = 0;
    var shown = 0;
    while (empties < EmptyRunLimit)
    {
        var text = archive.ResolveString((position << 1) | 1);
        if (text.Length == 0)
        {
            empties++;
            position++;
            continue;
        }

        empties = 0;
        if (needle is null || text.Contains(needle, StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"u16\t{(position << 1) | 1}\t{text}");
            shown++;
        }

        position += text.Length + 1;
    }

    Console.Error.WriteLine($"u16 pool: walked to char {position}");
    return shown;
}

static string TokenText(PvfArchive archive, PvfToken token) =>
    token.Kind == PvfTokenKind.Identifier || token.IsString
        ? archive.ResolveString(token.Value)
        : token.Kind == PvfTokenKind.Single
            ? BitConverter.Int32BitsToSingle(token.Value).ToString("0.######")
            : token.Value.ToString();

// A .lst is a flat run of (id, path) pairs; the path is quoted with backticks in the archive.
static List<(int Id, string Path)> ReadList(PvfArchive archive, string listPath)
{
    var entries = new List<(int, string)>();
    var reader = archive.OpenScript(listPath);
    var pending = int.MinValue;
    while (reader.Read(out var token))
    {
        if (token.Kind == PvfTokenKind.Int32)
        {
            pending = token.Value;
            continue;
        }

        if (!token.IsString)
            continue;

        entries.Add((pending, archive.ResolveString(token.Value).Trim('`')));
        pending = int.MinValue;
    }

    return entries;
}

static void CountTags(PvfArchive archive, PvfTokenReader script, Dictionary<string, int> counts)
{
    while (script.Read(out var token))
    {
        if (token.Kind != PvfTokenKind.Identifier)
            continue;
        var name = archive.ResolveString(token.Value);
        if (name.StartsWith('[') && !name.StartsWith("[/", StringComparison.Ordinal))
            counts[name] = counts.GetValueOrDefault(name) + 1;
    }
}

// Every script file with the given extension, whether or not any .lst declares it. Extensions
// carry meaning here — .act is a behaviour script, .obj an entity, .atk a hit — so the file type
// is a real population to reason about, not just a naming habit.
static int ForEachFileOfType(PvfArchive archive, string extension, Action<PvfTokenReader> visit)
{
    var suffix = extension.StartsWith('.') ? extension : "." + extension;
    var scanned = 0;
    for (var i = 0; i < archive.FileCount; i++)
    {
        var entry = archive.GetEntry(i);
        if (!entry.IsScript)
            continue;
        if (!archive.ResolveString(entry.NameOffset).EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            continue;

        scanned++;
        visit(archive.OpenScript(i));
    }

    return scanned;
}

static int ForEachListedFile(PvfArchive archive, string listPath, Action<PvfTokenReader> visit)
{
    var folder = FolderOf(listPath);
    var scanned = 0;
    foreach (var (_, relativePath) in ReadList(archive, listPath))
    {
        var index = archive.FindFile(folder + relativePath);
        if (index < 0)
            continue;
        scanned++;
        visit(archive.OpenScript(index));
    }

    return scanned;
}

static void Report(Dictionary<string, int> counts)
{
    foreach (var pair in counts.OrderByDescending(value => value.Value).ThenBy(value => value.Key))
        Console.WriteLine($"{pair.Value,7}  {pair.Key}");
}

// One token per line is unreadable and one line per file is unreadable the other way; a break
// before every tag is the shape the archive's own files are written in.
static void Dump(PvfArchive archive, string path, int maxTokens)
{
    var index = archive.FindFile(path);
    if (index < 0)
    {
        Console.WriteLine($"!! not found: {path}");
        return;
    }

    Console.WriteLine($"=== {path} ===");
    var reader = archive.OpenScript(index);
    var line = new StringBuilder();
    var written = 0;
    while (reader.Read(out var token) && written < maxTokens)
    {
        written++;
        var text = TokenText(archive, token);
        var isTag = token.Kind == PvfTokenKind.Identifier && text.StartsWith('[');
        if (isTag && line.Length > 0)
        {
            Console.WriteLine(line.ToString().TrimEnd());
            line.Clear();
        }

        line.Append(text).Append(' ');
        if (isTag)
        {
            Console.WriteLine(line.ToString().TrimEnd());
            line.Clear();
        }
    }

    if (line.Length > 0)
        Console.WriteLine(line.ToString().TrimEnd());
    if (written >= maxTokens)
        Console.WriteLine($"-- stopped at {maxTokens} tokens");
}
