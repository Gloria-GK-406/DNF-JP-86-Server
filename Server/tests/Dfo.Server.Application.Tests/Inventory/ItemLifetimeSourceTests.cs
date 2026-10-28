using System.Text.RegularExpressions;
using Xunit.Abstractions;

namespace Dfo.Server.Application.Tests.Inventory;

/// <summary>
/// Issue #200: whether an item is permanent or expired is read only through <c>ItemLifetime</c>. Production
/// sources do not compare a raw <c>ExpireTime</c> or <c>ExpireDate</c>, except the listed files, each pinned
/// to how many lines do and why.
/// </summary>
/// <remarks>
/// The scan reads raw text for the three shapes such a judgement takes here: the field on the left of a
/// comparison or <c>is</c> pattern, the field on the right of a comparison, and the field inside a property
/// pattern. Reading the field to write or encode it, or naming it as a constructor argument, does not match.
/// </remarks>
public sealed class ItemLifetimeSourceTests(ITestOutputHelper output)
{
    private static readonly (string Name, Regex Pattern)[] Judgements =
    [
        ("field compared", new Regex(@"\b(?:ExpireTime|ExpireDate)\b\s*(?:<=|>=|==|!=|<(?![<=])|>(?![>=])|\bis\b)", RegexOptions.CultureInvariant)),
        ("compared to field", new Regex(@"(?:<=|>=|==|!=|(?<![=<>-])<|(?<![=<>-])>)\s*[\w.!?()]*\.(?:ExpireTime|ExpireDate)\b", RegexOptions.CultureInvariant)),
        ("property pattern", new Regex(@"\{[^}]*\b(?:ExpireTime|ExpireDate)\s*:\s*(?:<|>|not\b|-?\d|int\.)", RegexOptions.CultureInvariant)),
    ];

    /// <summary>Each file that may compare a raw deadline, how many lines do, and why.</summary>
    private static readonly IReadOnlyDictionary<string, (int Count, string Reason)> Allowed =
        new Dictionary<string, (int Count, string Reason)>(StringComparer.Ordinal)
    {
        ["Dfo.Server.Application/Rental/EquipmentRentalDomainService.cs"] =
            (1, "a rental is active while its always-positive rental deadline is ahead; zero is not permanent there"),
        ["Dfo.Server.Persistence/Inventory/InventoryStateTables.cs"] =
            (1, "the same rental-active filter while merging rental rows"),
        ["Dfo.Server.Protocol/Game/StateSynchronization/StateSynchronizationPayloadCodec.Entry.cs"] =
            (1, "the same rental-active filter before encoding the rental panel"),
        ["Dfo.Server.Application/Inventory/InventoryEntryServices.cs"] =
            (1, "a stored rental record must carry a deadline that fits the wire's uint"),
        ["Dfo.Server.Application/Inventory/InventoryEntryAssembly.cs"] =
            (1, "the same rental record range check on the entry load"),
        ["Dfo.Server.Application/Accounts/QuestHelperPuppetDomainService.cs"] =
            (1, "compares the stored value with the corrected deadline to decide a rewrite, not whether it expired"),
    };

    [Fact]
    public void Only_the_listed_files_compare_a_raw_item_deadline()
    {
        var files = ProductionSources();
        output.WriteLine($"scanned source files: {files.Count}");
        foreach (var hit in Hits(files))
            output.WriteLine($"raw deadline {hit.Path}:{hit.Line} ({hit.Shape}): "
                + (Allowed.TryGetValue(hit.Path, out var allowed) ? allowed.Reason : "NOT LISTED"));

        var violations = Violations(files);

        Assert.True(violations.Count == 0, string.Join("\n", violations));
    }

    [Fact]
    public void Each_judgement_shape_in_any_other_file_is_named()
    {
        var files = Listed();
        files["Dfo.Server.Application/Inventory/Left.cs"] = "if (item.Snapshot.ExpireTime > 0) return;";
        files["Dfo.Server.Application/Inventory/Right.cs"] = "return now.ToUnixTimeSeconds() >= item.AvatarDetail!.ExpireDate;";
        files["Dfo.Server.Application/Inventory/Pattern.cs"] = "if (item.Snapshot is { IsMirror: false, ExpireTime: > 0 }) return;";
        files["Dfo.Server.Application/Inventory/Sentinel.cs"] = "var permanent = item.ExpireTime is 0 or int.MaxValue;";

        var violations = Violations(files);

        Assert.Equal(4, violations.Count);
        Assert.Contains(violations, violation => violation.StartsWith("Dfo.Server.Application/Inventory/Left.cs:1 compares a raw item deadline (field compared)", StringComparison.Ordinal));
        Assert.Contains(violations, violation => violation.StartsWith("Dfo.Server.Application/Inventory/Right.cs:1 compares a raw item deadline (compared to field)", StringComparison.Ordinal));
        Assert.Contains(violations, violation => violation.StartsWith("Dfo.Server.Application/Inventory/Pattern.cs:1 compares a raw item deadline (property pattern)", StringComparison.Ordinal));
        Assert.Contains(violations, violation => violation.StartsWith("Dfo.Server.Application/Inventory/Sentinel.cs:1 compares a raw item deadline (field compared)", StringComparison.Ordinal));
    }

    [Fact]
    public void Writing_encoding_and_lifetime_reads_are_not_judgements()
    {
        var files = Listed();
        files["Dfo.Server.Protocol/Game/Encode.cs"] = "writer.Write(hasItem ? core!.ExpireTime : 0);";
        files["Dfo.Server.Protocol/Game/Construct.cs"] = "var state = new State(\n    ExpireTime: 0,\n    Count: 1);";
        files["Dfo.Server.Application/Inventory/Write.cs"] = "item = item with { ExpireTime = deadline };";
        files["Dfo.Server.Application/Inventory/Lambda.cs"] = "var deadlines = items.Select(x => x.ExpireTime);";
        files["Dfo.Server.Application/Inventory/Read.cs"] = "if (item.Snapshot.Lifetime.IsExpired(now)) return;";

        Assert.Empty(Violations(files));
    }

    [Fact]
    public void A_second_judgement_in_a_listed_file_and_a_listed_file_without_one_are_named()
    {
        var files = Listed();
        files["Dfo.Server.Application/Rental/EquipmentRentalDomainService.cs"] += "\nif (row.Core.ExpireTime == 0) return;";
        files["Dfo.Server.Application/Accounts/QuestHelperPuppetDomainService.cs"] = "class QuestHelperPuppetDomainService { }";

        var violations = Violations(files);

        Assert.Equal(2, violations.Count);
        Assert.Contains(violations, violation => violation.Contains("EquipmentRentalDomainService.cs compares 2 times, pinned 1", StringComparison.Ordinal));
        Assert.Contains(violations, violation => violation.Contains("QuestHelperPuppetDomainService.cs compares 0 times, pinned 1", StringComparison.Ordinal));
    }

    private static IReadOnlyList<(string Path, int Line, string Shape)> Hits(IReadOnlyDictionary<string, string> files)
    {
        var hits = new List<(string, int, string)>();
        foreach (var (path, text) in files.OrderBy(file => file.Key, StringComparer.Ordinal))
        {
            var lines = text.Split('\n');
            for (var index = 0; index < lines.Length; index++)
            {
                var shape = Judgements.FirstOrDefault(judgement => judgement.Pattern.IsMatch(lines[index]));
                if (shape.Pattern is not null) hits.Add((path, index + 1, shape.Name));
            }
        }
        return hits;
    }

    private static IReadOnlyList<string> Violations(IReadOnlyDictionary<string, string> files)
    {
        var hits = Hits(files);
        return hits.Where(hit => !Allowed.ContainsKey(hit.Path))
            .Select(hit => $"{hit.Path}:{hit.Line} compares a raw item deadline ({hit.Shape}) and is not on the list; use ItemLifetime")
            .Concat(Allowed.OrderBy(file => file.Key, StringComparer.Ordinal)
                .Select(file => (file.Key, Found: hits.Count(hit => hit.Path == file.Key), Pinned: file.Value.Count))
                .Where(file => file.Found != file.Pinned)
                .Select(file => $"{file.Key} compares {file.Found} times, pinned {file.Pinned}"))
            .ToList();
    }

    /// <summary>Every listed file, holding exactly its pinned number of comparisons.</summary>
    private static Dictionary<string, string> Listed() =>
        Allowed.ToDictionary(file => file.Key,
            file => string.Join("\n", Enumerable.Repeat("if (row.Core.ExpireTime > now) return;", file.Value.Count)),
            StringComparer.Ordinal);

    private static Dictionary<string, string> ProductionSources()
    {
        var source = Path.Combine(Repository(), "Server", "src");
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(source, "*.cs", SearchOption.AllDirectories))
        {
            var path = Path.GetRelativePath(source, file).Replace('\\', '/');
            if (path.Contains("/bin/", StringComparison.Ordinal) || path.Contains("/obj/", StringComparison.Ordinal))
                continue;
            files[path] = File.ReadAllText(file);
        }
        return files;
    }

    private static string Repository()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (Directory.Exists(Path.Combine(directory.FullName, "Server", "src"))) return directory.FullName;
        throw new DirectoryNotFoundException("The item lifetime source test requires the repository checkout.");
    }
}
