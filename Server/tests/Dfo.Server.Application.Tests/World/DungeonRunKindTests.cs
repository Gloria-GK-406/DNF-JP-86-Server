using System.Text.RegularExpressions;
using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Application.Tests.World;

public sealed class DungeonRunKindTests
{
    private static readonly DungeonRunKind[] PlainKinds =
    [
        DungeonRunKind.Ordinary, DungeonRunKind.Dimension, DungeonRunKind.SouthernDale, DungeonRunKind.TauKingdom,
        DungeonRunKind.SealedTreasure, DungeonRunKind.Heras, DungeonRunKind.GoldSilverHorn, DungeonRunKind.Couple,
    ];

    [Fact]
    public void Every_kind_answers_the_shared_decisions()
    {
        foreach (var kind in Enum.GetValues<DungeonRunKind>())
            Assert.NotNull(DungeonRunCapabilities.Of(kind));
    }

    [Fact]
    public void Only_kinds_without_an_entry_plan_can_be_plain()
    {
        foreach (var kind in Enum.GetValues<DungeonRunKind>())
        {
            if (PlainKinds.Contains(kind)) Assert.Equal(kind, new DungeonRunMode.Plain(kind).Kind);
            else Assert.Throws<ArgumentException>(() => new DungeonRunMode.Plain(kind));
        }
    }

    [Fact]
    public void Kind_switches_in_production_name_every_member()
    {
        var violations = new List<string>();
        foreach (var (path, text) in ProductionSources())
            violations.AddRange(DiscardedKindSwitches(text).Select(line => $"{path}:{line}"));
        Assert.Empty(violations);
    }

    [Fact]
    public void A_kind_switch_with_a_fallback_arm_is_rejected()
    {
        Assert.Single(DiscardedKindSwitches("""
            var x = kind switch
            {
                DungeonRunKind.Ordinary => 1,
                _ => 0,
            };
            """));
        Assert.Single(DiscardedKindSwitches("""
            switch (kind)
            {
                case DungeonRunKind.Altar: break;
                default: break;
            }
            """));
        Assert.Empty(DiscardedKindSwitches("""
            var x = kind switch { DungeonRunKind.Ordinary => 1, DungeonRunKind.Hell => 2 };
            var y = other switch { 1 => 1, _ => 0 };
            """));
    }

    /// <summary>The line of every switch over run kinds that hides new kinds behind a fallback arm.</summary>
    private static IEnumerable<int> DiscardedKindSwitches(string text)
    {
        foreach (Match keyword in Regex.Matches(text, @"\bswitch\b"))
        {
            var open = text.IndexOf('{', keyword.Index);
            if (open < 0) continue;
            var depth = 0;
            var close = open;
            for (; close < text.Length; close++)
            {
                if (text[close] == '{') depth++;
                else if (text[close] == '}' && --depth == 0) break;
            }
            var body = text[open..Math.Min(close + 1, text.Length)];
            if (body.Contains("DungeonRunKind.", StringComparison.Ordinal)
                && Regex.IsMatch(body, @"(^|[\s,{(])_\s*=>|\bdefault\s*:", RegexOptions.Multiline))
                yield return text[..keyword.Index].Count(c => c == '\n') + 1;
        }
    }

    private static IEnumerable<(string Path, string Text)> ProductionSources()
    {
        var source = Path.Combine(Repository(), "Server", "src");
        foreach (var file in Directory.EnumerateFiles(source, "*.cs", SearchOption.AllDirectories))
        {
            var path = Path.GetRelativePath(source, file).Replace('\\', '/');
            if (path.Contains("/bin/", StringComparison.Ordinal) || path.Contains("/obj/", StringComparison.Ordinal)) continue;
            yield return (path, File.ReadAllText(file));
        }
    }

    private static string Repository()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (Directory.Exists(Path.Combine(directory.FullName, "Server", "src", "Dfo.Server.Abstractions"))) return directory.FullName;
        throw new DirectoryNotFoundException("The run kind source test requires the repository checkout.");
    }
}
