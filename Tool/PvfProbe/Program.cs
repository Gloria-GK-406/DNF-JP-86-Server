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
        Require(args, 2);
        var counts = new Dictionary<string, int>();
        var scanned = ForEachListedFile(archive, args[1], script =>
        {
            while (script.Read(out var token))
            {
                if (token.Kind != PvfTokenKind.Identifier)
                    continue;
                var name = archive.ResolveString(token.Value);
                if (name.StartsWith('[') && !name.StartsWith("[/", StringComparison.Ordinal))
                    counts[name] = counts.GetValueOrDefault(name) + 1;
            }
        });

        Console.WriteLine($"tag vocabulary over {scanned} files:");
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
          tags   <listPath>                          tag vocabulary across every listed file
          values <listPath> <tag>                    the value each listed file gives that tag
          where  <listPath> <tag> <value> [limit]    the files whose tag has that value
          name   <listPath> <substring> [limit]      the files whose [name] contains a substring

        Example:
          PvfProbe values stackable/stackable.lst "[stackable type]"
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
