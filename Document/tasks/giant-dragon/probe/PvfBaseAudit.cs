using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dfo.Pvf;

internal static class PvfBaseAudit
{
    public static void Run(string original, string current, string evidence, string output)
    {
        using var baseline = PvfArchive.Open(original);
        using var active = PvfArchive.Open(current);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "town/town.lst", "n_quest/quest.lst", "town/chn_shonan_holy.twn",
            "map/town/shonan/shonan_workshop.map",
            "map/town/chn_dragon_shonan_holyland/gate_shonan_holy.map",
            "worldmap/chn_shonan_holy.wdm", "worldmap/ui/chn_shonan_holy.xui",
        };
        foreach (var file in Directory.EnumerateFiles(evidence, "dgn-*.txt"))
        {
            var match = Regex.Match(File.ReadAllText(file), @"=== (.*?) ===");
            if (!match.Success) throw new InvalidDataException(file);
            paths.Add(match.Groups[1].Value);
        }
        for (var id = 20720; id <= 20735; id++)
        {
            var kind = id is >= 20729 and <= 20731 ? "daily" : "achievement";
            paths.Add($"n_quest/event/chn_20140121_dragondungeon/chn_dragon_dungeon_{kind}_{id}.qst");
        }
        var rows = paths.Order().Select(path =>
        {
            var before = baseline.FindFile(path);
            var after = active.FindFile(path);
            var a = before < 0 ? null : Convert.ToHexString(SHA256.HashData(baseline.GetFileBytes(before)));
            var b = after < 0 ? null : Convert.ToHexString(SHA256.HashData(active.GetFileBytes(after)));
            return new { Path = path, OriginalSha256 = a, CurrentSha256 = b, Identical = a is not null && a == b };
        }).ToArray();
        string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
        var result = new { Original = Path.GetFullPath(original), Current = Path.GetFullPath(current),
            OriginalSha256 = Hash(original), CurrentSha256 = Hash(current),
            OriginalEntries = baseline.FileCount, CurrentEntries = active.FileCount,
            AllIdentical = rows.All(row => row.Identical), Files = rows };
        File.WriteAllText(output, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Audited {rows.Length} native resources; all identical: {result.AllIdentical}; current SHA256 {result.CurrentSha256}");
        foreach (var row in rows.Where(row => !row.Identical)) Console.WriteLine($"DIFF {row.Path}");
        if (!result.AllIdentical) throw new InvalidDataException("Native restoration inputs differ; adaptation requires further audit.");
    }
}
