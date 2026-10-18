using System.Diagnostics;
using System.Security.Cryptography;
using Dfo.Pvf;
using GmIconPack;

// Builds the GM back-office icon pack: icons/{itemId}.png (decoded from the client's ImagePacks2 via each item's
// PVF [icon]) plus manifest.json. Regenerate the whole pack whenever the PVF changes. The pack carries Nexon art:
// keep it in the deployment folder only, never in the repository or a release.

Console.OutputEncoding = System.Text.Encoding.UTF8;

string? pvfPath = null, packsDir = null, outPath = null;
var includeAll = false;
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--pvf" when i + 1 < args.Length: pvfPath = args[++i]; break;
        case "--imagepacks" when i + 1 < args.Length: packsDir = args[++i]; break;
        case "--out" when i + 1 < args.Length: outPath = args[++i]; break;
        case "--all": includeAll = true; break;
        default: return Usage($"unknown or incomplete argument: {args[i]}");
    }
}
if (pvfPath is null || packsDir is null || outPath is null) return Usage(null);
if (!File.Exists(pvfPath)) return Usage($"PVF not found: {pvfPath}");
if (!Directory.Exists(packsDir)) return Usage($"ImagePacks2 folder not found: {packsDir}");

var clock = Stopwatch.StartNew();
string sha;
using (var file = File.OpenRead(pvfPath)) sha = Convert.ToHexStringLower(SHA256.HashData(file));

var index = NpkIndex.Build(packsDir);
Console.WriteLine($"npk index: {index.Count} img in {index.PackCount} pack(s) ({clock.Elapsed.TotalSeconds:0.0}s)");

ItemScan scan;
using (var archive = PvfArchive.Open(pvfPath)) scan = PvfItemCatalog.Scan(archive, includeAll);
Console.WriteLine($"pvf scan: {scan.Equipment} equipment + {scan.Stackables} stackable"
                  + (includeAll ? "" : $"; excluded {scan.ExcludedAvatars} avatar, {scan.ExcludedCreatures} creature")
                  + $" ({clock.Elapsed.TotalSeconds:0.0}s)");

var target = Path.GetFullPath(outPath);
Directory.CreateDirectory(Path.GetDirectoryName(target)!);
var temp = target + ".tmp-" + Environment.ProcessId;
IconPackSummary summary;
try
{
    using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 1 << 20))
        summary = IconPackBuilder.Build(scan.Requests, index.ReadImg, stream, sha, DateTimeOffset.UtcNow);
    try { File.Move(temp, target, overwrite: true); }
    // A running server keeps the pack open shared for delete only: replacing fails, deleting then moving works.
    catch (Exception error) when (error is UnauthorizedAccessException or IOException) { File.Delete(target); File.Move(temp, target); }
}
catch
{
    File.Delete(temp);
    throw;
}

var missingTotal = summary.Missing.Values.Sum();
Console.WriteLine($"candidates {summary.Candidates}, resolved {summary.Resolved}, missing {missingTotal}");
foreach (var (reason, count) in summary.Missing)
{
    Console.WriteLine($"  missing {reason}: {count}");
    foreach (var example in summary.Examples[reason]) Console.WriteLine($"    e.g. {example}");
}
Console.WriteLine($"unique frames encoded {summary.UniqueFrames}; non-{IconPackBuilder.IconSize}x{IconPackBuilder.IconSize}: "
                  + $"{summary.NonStandardSizeFrames} frame(s), {summary.NonStandardSizeItems} item(s)");
Console.WriteLine("img versions: " + string.Join(", ", summary.ImgVersions.Select(v => $"v{v.Key}={v.Value}")));
Console.WriteLine($"wrote {target} ({new FileInfo(target).Length / 1024.0 / 1024.0:0.0} MiB), pvf sha256 {sha}, {clock.Elapsed.TotalSeconds:0.0}s");
return 0;

static int Usage(string? error)
{
    if (error is not null) Console.Error.WriteLine(error);
    Console.Error.WriteLine("usage: GmIconPack --pvf <Script.pvf> --imagepacks <ImagePacks2 dir> --out <pack.zip> [--all]");
    Console.Error.WriteLine("  --all  also include avatar and creature equipment");
    return 2;
}
