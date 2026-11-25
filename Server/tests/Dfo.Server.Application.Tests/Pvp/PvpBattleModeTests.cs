using System.Text.RegularExpressions;
using Dfo.Server.Abstractions.Pvp;

namespace Dfo.Server.Application.Tests.Pvp;

public sealed class PvpBattleModeTests
{
    [Fact]
    public void Individual_team_and_relay_battles_are_supported()
    {
        Assert.Equal([PvpBattleMode.Individual, PvpBattleMode.Team, PvpBattleMode.Relay, PvpBattleMode.TotalMatch],
            Enum.GetValues<PvpBattleMode>().Where(mode => PvpBattleModeCapabilities.Of(mode).Supported));
    }

    [Fact]
    public void Team_battles_split_seats_into_two_balanced_teams()
    {
        var individual = PvpBattleModeCapabilities.Of(PvpBattleMode.Individual);
        var team = PvpBattleModeCapabilities.Of(PvpBattleMode.Team);

        Assert.Equal(new PvpBattleModeCapabilities(true, PvpTeamAssignment.Individual, false,
            PvpBattleTerminal.LastFighterStanding, true), individual);
        Assert.Equal(new PvpBattleModeCapabilities(true, PvpTeamAssignment.AlternatingTwoTeams, true,
            PvpBattleTerminal.LastTeamStanding, true), team);
    }

    [Fact]
    public void Mode_switches_in_production_name_every_member()
    {
        var violations = new List<string>();
        foreach (var (path, text) in ProductionSources())
            violations.AddRange(DiscardedModeSwitches(text).Select(line => $"{path}:{line}"));
        Assert.Empty(violations);
    }

    [Fact]
    public void A_mode_switch_with_a_fallback_arm_is_rejected()
    {
        Assert.Single(DiscardedModeSwitches("""
            var x = mode switch
            {
                PvpBattleMode.Individual => 1,
                _ => 0,
            };
            """));
        Assert.Single(DiscardedModeSwitches("""
            switch (mode)
            {
                case PvpBattleMode.Team: break;
                default: break;
            }
            """));
        Assert.Empty(DiscardedModeSwitches("""
            var x = mode switch { PvpBattleMode.Individual => 1, PvpBattleMode.Team => 2 };
            var y = other switch { 1 => 1, _ => 0 };
            """));
    }

    /// <summary>The line of every switch over battle modes that hides new modes behind a fallback arm.</summary>
    private static IEnumerable<int> DiscardedModeSwitches(string text)
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
            if (body.Contains("PvpBattleMode.", StringComparison.Ordinal)
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
        throw new DirectoryNotFoundException("The battle mode source test requires the repository checkout.");
    }
}
