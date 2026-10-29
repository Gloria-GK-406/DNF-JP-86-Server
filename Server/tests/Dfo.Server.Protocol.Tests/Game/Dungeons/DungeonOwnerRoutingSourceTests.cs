using System.Text.RegularExpressions;
using Xunit.Abstractions;

namespace Dfo.Server.Protocol.Tests.Game.Dungeons;

/// <summary>
/// The thirteen dungeon-owned commands share one shape: the command's route method drops a stale
/// frame, locates the requester once through <c>DungeonLocator</c>, and hands the fresh frame to one
/// channel. Protocol production sources keep that shape:
/// the stale judgement sits only in the route methods and the 0x009F / 0x0010 entries; the two
/// tower services and the occupancy registry are asked "who owns this session" only by the
/// locator, apart from the listed checks that serve the work itself; and a place is located only
/// by the route methods, the 0x000F route doing it once.
/// </summary>
/// <remarks>
/// The scan reads raw text with comments blanked. A hit belongs to the nearest member declaration
/// above it that starts with an access modifier; overloads are told apart by parameter types. Each
/// listed member is pinned to how many hits it holds, so a second lookup in a listed member fails as
/// well as one in an unlisted member. Tower and registry receivers are the names that sources
/// declare with the service type. Test drivers that copy a route stay outside the scan.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class DungeonOwnerRoutingSourceTests(ITestOutputHelper output)
{
    private enum Rule
    {
        /// <summary>A stale frame judgement: the shared definition or its inline formula.</summary>
        Stale,
        /// <summary>A Boss or illusion tower asked which run seats a session or character.</summary>
        TowerLookup,
        /// <summary>The occupancy registry asked which dungeon holds a character.</summary>
        ParticipationLookup,
        /// <summary>A requester or party located.</summary>
        Locate,
    }

    private sealed record Site(Rule Rule, string Path, string Member, int Count, string Reason, bool Route = false);

    private const string MapLoading = "Game/GameMapLoadingCommandHandler.cs";
    private const string PartyTransition = "Game/GamePartyTransitionCommandHandler.cs";
    private const string PartyStages = "Game/GamePartyTransitionCommandHandler.Stages.cs";
    private const string Selection = "Game/DungeonSelection/DungeonSelectionCommandPhases.cs";
    private const string BossProjection = "Game/Party/BossTowerActionProjection.cs";
    private const string IllusionProjection = "Game/Party/IllusionTowerActionProjection.cs";
    private const string TowerReturn = "Game/TowerReturnCommandHandler.Stages.cs";
    private static readonly string[] DepartureProjections =
        [PartyStages, "Game/DungeonSelection/GameDungeonSelectionCommandHandler.Stages.cs", TowerReturn];
    private const string FinishLoading = "HandleAsync(CommandExecutionContext, GameFinishLoadingCommand, CancellationToken)";
    private const string MoveMap = "HandleAsync(CommandExecutionContext, GameMoveMapCommand, CancellationToken)";

    /// <summary>The thirteen commands' route methods: each judges staleness first, then locates once.</summary>
    private static readonly (string Path, string Member, string Commands)[] Routes =
    [
        ("Game/GroundPickup/GameGetItemCommandHandler.cs", "HandleAsync", "0x002B pickup"),
        ("Game/CharacterDeath/GameCharacterDeathCommandHandler.cs", "HandleAsync", "0x0028 character death"),
        ("Game/GameMonsterDeathCommandHandler.cs", "RunAsync", "0x0027 monster death and object destruction"),
        (MapLoading, FinishLoading, "0x0025 finish loading"),
        (MapLoading, MoveMap, "0x002D move map"),
        (PartyTransition, "EplpAsync", "0x0048 Eplp"),
        (Selection, "BackToVillageAsync", "0x0084 back to village"),
        (Selection, "EnterSelectDungeonAsync", "0x000F enter selection; the released Boss run hands over without a second locate"),
        ("Game/CharacterDeath/GameUseRevivalCoinCommandHandler.cs", "RunAsync", "0x0029 revival coin"),
        (PartyTransition, "GiveUpAsync", "0x002A give up, located by the whole party"),
        (PartyStages, "HandleMutationAsync", "0x000D leave, 0x000E remove, 0x0079 change leader, located by the whole party"),
    ];

    private static readonly IReadOnlyList<Site> Allowed =
    [
        new(Rule.Stale, "Game/GameRequestFreshness.cs", "IsStale", 1, "the one definition of a stale frame"),
        .. Routes.Select(route => new Site(Rule.Stale, route.Path, route.Member, 1, route.Commands + ": dropped at the route entry", Route: true)),
        new(Rule.Stale, "Game/TowerLoadingCommandPhases.cs", "StartIllusionAsync", 1,
            "0x009F illusion stage start: a single illusion channel with no routing, judged at its entry"),
        new(Rule.Stale, "Game/DungeonEntry/SelectDungeonCommandPhases.cs", "SelectDungeonAsync", 1,
            "0x0010 select dungeon: judged before consuming the entry intent, outside dungeon owner routing"),
        new(Rule.Stale, "Game/GuardianAltar/GameGuardianAltarCommand.cs", "HandleAsync", 1,
            "0x0392-0x039A guardian actions: the single activity entry rejects stale frames before any owned mutation"),

        .. Routes.Select(route => new Site(Rule.Locate, route.Path, route.Member, 1, route.Commands + ": the one locate")),
        new(Rule.Locate, PartyStages, "LocatePartyOf", 1,
            "collects the party candidates once and places them through DungeonLocator.LocateParty"),

        new(Rule.ParticipationLookup, "Game/Dungeons/DungeonLocator.cs", "Locate", 1, "the locator reads the occupancy registry"),

        new(Rule.TowerLookup, "Game/Dungeons/DungeonLocator.cs", "Locate", 2,
            "the locator asks only the registered tower whether it seats this session in that run"),
        // Checks that serve the work in a channel or a projection, never the choice of channel.
        new(Rule.TowerLookup, BossProjection, "BossTowerExact", 1,
            "exact session check: the located session still sits in the same run and room version"),
        new(Rule.TowerLookup, IllusionProjection, "IllusionTowerExact", 1, "the same exact session check for the illusion tower"),
        new(Rule.TowerLookup, BossProjection, "BossTowerExactMembers", 1,
            "projection recipients: members still exactly in the run receive the frames"),
        new(Rule.TowerLookup, IllusionProjection, "IllusionTowerExactMembers", 1, "the same projection recipients for the illusion tower"),
        new(Rule.TowerLookup, BossProjection, "BossTowerCurrentRun", 1,
            "projection: rereads the run through a member still exactly seated after the write"),
        new(Rule.TowerLookup, IllusionProjection, "IllusionTowerCurrentRun", 1, "the same projection reread for the illusion tower"),
        new(Rule.TowerLookup, BossProjection, "BossTowerSendCommittedBatchAsync", 1,
            "projection retry: replays a committed batch only while the member still holds the same settlement receipt"),
        new(Rule.TowerLookup, IllusionProjection, "IllusionTowerSendCommittedBatchAsync", 1, "the same projection retry for the illusion tower"),
        new(Rule.TowerLookup, PartyStages, "BossTowerExactSessions", 1,
            "party channel: every member of the located run is still exactly seated before the party write"),
        new(Rule.TowerLookup, PartyStages, "IllusionTowerExactSessions", 1, "the same whole-party exact check for the illusion tower"),
        .. DepartureProjections.SelectMany(path => new Site[]
        {
            new(Rule.TowerLookup, path, "ProjectBossTowerPartyDepartureAsync", 1,
                "departure projection: the returning member is projected back to town only once the tower no longer holds them"),
            new(Rule.TowerLookup, path, "ProjectIllusionTowerPartyDepartureAsync", 1, "the same departure projection for the illusion tower"),
        }),
        new(Rule.TowerLookup, "Game/DungeonEntry/BossEntryCommandPhases.cs", "BossEntryCurrent", 1,
            "entry: after the entry write, every entrant must sit exactly in the new run"),
        new(Rule.TowerLookup, "Game/DungeonEntry/IllusionEntryCommandPhases.cs", "IllusionEntryCurrent", 1,
            "the same entry exactness check for the illusion tower"),
        new(Rule.TowerLookup, TowerReturn, "ReturnBossTowerRunAsync", 1,
            "timer return: the due context is matched against the run its session still sits in"),
        new(Rule.TowerLookup, TowerReturn, "ReturnIllusionTowerRunAsync", 1, "the same timer return for the illusion tower"),
        new(Rule.TowerLookup, "Game/TowerLoadingCommandPhases.cs", "StartIllusionAsync", 1,
            "0x009F illusion stage start: its single channel takes the run the session sits in"),
        // Other commands' own audiences: they place the session once, then keep only members still seated in that run.
        new(Rule.Locate, "Game/Creatures/CreatureRuntimeDelivery.cs", "Resolve", 1,
            "creature notifications: a tower member is seen by the members of the run it is located in"),
        new(Rule.TowerLookup, "Game/Creatures/CreatureRuntimeDelivery.cs", "Resolve", 2,
            "creature notifications: the seat check every delivery repeats for the located Boss or illusion run"),
        new(Rule.Locate, "Game/QuestCompletion/GameFinishQuestCommand.cs", "HandleAsync", 1,
            "quest completion appearance: placed once to choose the tower or ordinary audience"),
        new(Rule.TowerLookup, "Game/QuestCompletion/GameFinishQuestCommand.cs", "HandleAsync", 2,
            "quest completion appearance: shown to the peers still seated in the located Boss or illusion run"),
        // Commands refused inside any dungeon ask the occupancy registry, which covers both towers.
        new(Rule.ParticipationLookup, "Game/InventorySort/GameSortItemCommand.cs", "HandleAsync", 1,
            "cargo sorting is refused inside any dungeon"),
        new(Rule.ParticipationLookup, "Game/RecoverStamina/RecoverStaminaSlice.cs", "RecoverAsync", 1,
            "fatigue recovery is refused inside any dungeon"),
    ];

    [Fact]
    public void Dungeon_owner_routing_reads_staleness_and_ownership_only_where_listed()
    {
        var files = ProductionSources();
        output.WriteLine($"scanned source files: {files.Count}");
        foreach (var hit in Hits(files))
        {
            var site = Allowed.FirstOrDefault(site => site.Rule == hit.Rule && site.Path == hit.Path && site.Member == hit.Member);
            output.WriteLine($"{hit.Rule} {hit.Path}:{hit.Line} {hit.Member}: {site?.Reason ?? "NOT LISTED"}");
        }

        var violations = Violations(files);

        Assert.True(violations.Count == 0, string.Join("\n", violations));
    }

    [Fact]
    public void The_listed_sites_alone_are_clean()
    {
        Assert.Empty(Violations(Listed()));
    }

    [Fact]
    public void Guardian_action_allowance_does_not_allow_staleness_checks_inside_its_mutation()
    {
        var files = Listed();
        const string path = "Game/GuardianAltar/GameGuardianAltarCommand.cs";
        files[path] += """

            private ValueTask MutateAsync(GameMessageMetadata metadata)
            {
                if (GameRequestFreshness.IsStale(metadata)) return;
            }
            """;

        var violation = Assert.Single(Violations(files));
        Assert.StartsWith(path + ":", violation, StringComparison.Ordinal);
        Assert.EndsWith("Stale in MutateAsync is not listed", violation, StringComparison.Ordinal);
    }

    [Fact]
    public void A_stale_judgement_in_a_channel_is_named()
    {
        var files = Listed();
        files["Game/TowerLoadingCommandPhases.cs"] += """

            private async ValueTask AdvanceBossAsync(CommandExecutionContext execution, DungeonPlace.InBossTower boss, GameMessageMetadata metadata)
            {
                if (GameRequestFreshness.IsStale(metadata)) return;
            }
            """;
        files["Game/MonsterTowerCommandPhases.cs"] = """
            internal sealed partial class GameMonsterDeathCommandHandler
            {
                private async ValueTask KillIllusionAsync(CommandExecutionContext execution, DungeonPlace.InIllusionTower illusion, MonsterDeathRequest message)
                {
                    if (message.Metadata.InboundOrdinal is > 0 && !message.Metadata.SequenceIsFresh) return;
                }
            }
            """;

        var violations = Violations(files);

        Assert.Equal(2, violations.Count);
        Assert.Contains(violations, violation => violation.StartsWith("Game/MonsterTowerCommandPhases.cs:5 Stale in KillIllusionAsync is not listed", StringComparison.Ordinal));
        Assert.Contains(violations, violation => violation.StartsWith("Game/TowerLoadingCommandPhases.cs:", StringComparison.Ordinal)
            && violation.EndsWith("Stale in AdvanceBossAsync is not listed", StringComparison.Ordinal));
    }

    [Fact]
    public void A_route_that_looks_up_before_dropping_a_stale_frame_is_named()
    {
        var files = Listed();
        files[Selection] = files[Selection].Replace(
            "if (GameRequestFreshness.IsStale(metadata)) return;\n        _ = DungeonLocator.Locate(",
            "var session = sessions.FindByConnection(connectionId);\n        if (GameRequestFreshness.IsStale(metadata)) return;\n        _ = DungeonLocator.Locate(",
            StringComparison.Ordinal);

        var violations = Violations(files);

        Assert.Equal(2, violations.Count);
        Assert.All(violations, violation => Assert.Contains("looks up the session or place before dropping a stale frame", violation, StringComparison.Ordinal));
        Assert.Contains(violations, violation => violation.StartsWith(Selection + " BackToVillageAsync", StringComparison.Ordinal));
        Assert.Contains(violations, violation => violation.StartsWith(Selection + " EnterSelectDungeonAsync", StringComparison.Ordinal));
    }

    [Fact]
    public void A_tower_lookup_for_routing_outside_the_locator_is_named()
    {
        var files = Listed();
        files["Game/MonsterTowerCommandPhases.cs"] = """
            internal sealed partial class GameMonsterDeathCommandHandler
            {
                private async ValueTask KillIllusionAsync(CommandExecutionContext execution, DungeonPlace.InIllusionTower illusion)
                {
                    if (illusionTowers.Find(illusion.Session) is null) return;
                }
            }
            """;
        files["Game/GroundPickup/GameGetItemCommandHandler.cs"] += """

            public async ValueTask<bool> PickupAsync(SelectedSessionIdentity session)
            {
                if (bossTowers.Find(session) is { } run) return true;
                return bossTowers?.IsInTower(session.CharacterId) == true;
            }
            """;
        files["Game/Dungeons/DungeonLocator.cs"] += "\n_ = illusionTowers.Find(session);";

        var violations = Violations(files);

        Assert.Equal(4, violations.Count);
        Assert.Contains(violations, violation => violation.StartsWith("Game/MonsterTowerCommandPhases.cs:5 TowerLookup in KillIllusionAsync is not listed", StringComparison.Ordinal));
        Assert.Equal(2, violations.Count(violation => violation.Contains("TowerLookup in PickupAsync is not listed", StringComparison.Ordinal)));
        Assert.Contains(violations, violation => violation.StartsWith("Game/Dungeons/DungeonLocator.cs Locate holds 3 TowerLookup, pinned 2", StringComparison.Ordinal));
    }

    [Fact]
    public void A_registry_lookup_outside_the_locator_is_named()
    {
        var files = Listed();
        files["Game/OrdinaryLoadingCommandPhases.cs"] = """
            internal sealed partial class GameMapLoadingCommandHandler(IDungeonParticipationDomainService registry)
            {
                private async ValueTask MoveOrdinaryAsync(SelectedSessionIdentity session)
                {
                    if (registry.Find(session.CharacterId) is { Mode: DungeonParticipationMode.BossTower }) return;
                }
            }
            """;
        files["Game/GroundPickup/GameGetItemCommandHandler.Towers.cs"] = """
            internal sealed partial class GameGetItemCommandHandler
            {
                private async ValueTask PickupBossAsync(DungeonPlace.InBossTower boss)
                {
                    if (participation.Find(boss.Session.CharacterId)?.RunId != boss.Run.RunId) return;
                }
            }
            """;

        var violations = Violations(files);

        Assert.Equal(2, violations.Count);
        Assert.Contains("Game/GroundPickup/GameGetItemCommandHandler.Towers.cs:5 ParticipationLookup in PickupBossAsync is not listed", violations);
        Assert.Contains("Game/OrdinaryLoadingCommandPhases.cs:5 ParticipationLookup in MoveOrdinaryAsync is not listed", violations);
    }

    [Fact]
    public void A_second_locate_and_a_locate_outside_a_route_are_named()
    {
        var files = Listed();
        const string Entry = "private void EnterSelectDungeonAsync()\n    {\n        if (GameRequestFreshness.IsStale(metadata)) return;\n";
        Assert.Contains(Entry, files[Selection], StringComparison.Ordinal);
        files[Selection] = files[Selection].Replace(Entry,
            Entry + "        _ = DungeonLocator.Locate(session, participation, bossTowers, illusionTowers);\n", StringComparison.Ordinal);
        files["Game/DungeonSelection/OrdinarySelection.cs"] = """
            internal sealed partial class GameDungeonSelectionCommandHandler
            {
                private async ValueTask<bool> EnterOrdinarySelectionAsync(SelectedSessionIdentity? session)
                {
                    if (session is not null && DungeonLocator.Locate(session, participation, bossTowers, illusionTowers) is DungeonPlace.InBossTower)
                        return true;
                    return false;
                }
            }
            """;

        var violations = Violations(files);

        Assert.Equal(2, violations.Count);
        Assert.Contains(violations, violation => violation.StartsWith("Game/DungeonSelection/OrdinarySelection.cs:5 Locate in EnterOrdinarySelectionAsync is not listed", StringComparison.Ordinal));
        Assert.Contains(violations, violation => violation.StartsWith(Selection + " EnterSelectDungeonAsync holds 2 Locate, pinned 1", StringComparison.Ordinal));
    }

    [Fact]
    public void Comments_and_other_lookups_are_not_routing()
    {
        var files = Listed();
        files["Game/OrdinaryLoadingCommandPhases.cs"] = """
            internal sealed partial class GameMapLoadingCommandHandler
            {
                /// <summary>Unlike <c>bossTowers.Find(session)</c>, reads the ordinary run.</summary>
                private async ValueTask MoveOrdinaryAsync(SelectedSessionIdentity session)
                {
                    // The route already ran GameRequestFreshness.IsStale(metadata) and DungeonLocator.Locate(session, ...).
                    /* participation.Find(session.CharacterId) */
                    var run = runs.Find(session.CharacterId);
                    var current = sessions.FindByConnection(session.ConnectionId);
                    var snapshot = run.Find(session);
                }
            }
            """;

        Assert.Empty(Violations(files));
    }

    private static readonly Regex Declaration = new(
        @"^[ \t]*(?:(?:public|private|internal|protected)\s+)[^;=\n]*?\b(?!(?:static|async|readonly|unsafe|extern|override|virtual|sealed|abstract|partial|new)\b)(\w+)(?:<[^>()\n]*>)?\s*\(",
        RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.CultureInvariant);

    private static readonly Regex StalePattern = new(@"\bIsStale\s*\(|\bSequenceIsFresh\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex LocatePattern = new(@"\bDungeonLocator\s*\.\s*(?:Locate|LocateParty)\s*\(|\bLocatePartyOf\s*\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex SessionOrPlace = new(@"\bFindByConnection\s*\(|\bDungeonLocator\s*\.|\bLocatePartyOf\s*\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex TowerReceiver = new(
        @"\bI(?:BossTower|IllusionTower)DomainService\s*\??\s+(\w+)\b(?!\s*[(<])", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ParticipationReceiver = new(
        @"\bIDungeonParticipationDomainService\s*\??\s+(\w+)\b(?!\s*[(<])", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private sealed record Hit(Rule Rule, string Path, int Line, string Member, int Index);

    private sealed record Member(string Label, int Name, int Start, int End);

    private static IReadOnlyList<Hit> Hits(IReadOnlyDictionary<string, string> files)
    {
        var code = files.ToDictionary(file => file.Key, file => WithoutComments(file.Value), StringComparer.Ordinal);
        var patterns = new (Rule Rule, Regex? Pattern)[]
        {
            (Rule.Stale, StalePattern),
            (Rule.TowerLookup, Lookup(code.Values, TowerReceiver, "Find|IsInTower")),
            (Rule.ParticipationLookup, Lookup(code.Values, ParticipationReceiver, "Find")),
            (Rule.Locate, LocatePattern),
        };
        var hits = new List<Hit>();
        foreach (var (path, text) in code.OrderBy(file => file.Key, StringComparer.Ordinal))
        {
            var members = Members(text);
            foreach (var (rule, pattern) in patterns)
            {
                if (pattern is null) continue;
                foreach (Match match in pattern.Matches(text))
                {
                    var member = members.LastOrDefault(value => value.Start <= match.Index);
                    // The declaration of a member named like a lookup is not a call of it.
                    if (member is not null && member.Name == match.Index) continue;
                    var line = 1 + text.AsSpan(0, match.Index).Count('\n');
                    hits.Add(new(rule, path, line, member?.Label ?? "<none>", match.Index));
                }
            }
        }
        return hits;
    }

    private static List<string> Violations(IReadOnlyDictionary<string, string> files)
    {
        var hits = Hits(files);
        var violations = hits
            .Where(hit => !Allowed.Any(site => site.Rule == hit.Rule && site.Path == hit.Path && site.Member == hit.Member))
            .OrderBy(hit => hit.Path, StringComparer.Ordinal).ThenBy(hit => hit.Line)
            .Select(hit => $"{hit.Path}:{hit.Line} {hit.Rule} in {hit.Member} is not listed")
            .ToList();
        foreach (var site in Allowed)
        {
            var found = hits.Count(hit => hit.Rule == site.Rule && hit.Path == site.Path && hit.Member == site.Member);
            if (found != site.Count) violations.Add($"{site.Path} {site.Member} holds {found} {site.Rule}, pinned {site.Count}");
        }
        foreach (var site in Allowed.Where(site => site.Route))
        {
            if (!files.TryGetValue(site.Path, out var text)) continue;
            var code = WithoutComments(text);
            if (Members(code).FirstOrDefault(member => member.Label == site.Member) is not { } member) continue;
            var body = code[member.Start..member.End];
            var stale = StalePattern.Match(body);
            var lookup = SessionOrPlace.Match(body);
            if (lookup.Success && (!stale.Success || lookup.Index < stale.Index))
                violations.Add($"{site.Path} {site.Member} looks up the session or place before dropping a stale frame");
        }
        return violations;
    }

    /// <summary>A call on any receiver the sources declare with the service type, or none when nothing declares one.</summary>
    private static Regex? Lookup(IEnumerable<string> code, Regex receiver, string methods)
    {
        var names = code.SelectMany(text => receiver.Matches(text).Select(match => match.Groups[1].Value))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        return names.Length == 0 ? null
            : new Regex(@"\b(?:" + string.Join('|', names) + @")\s*\??\s*\.\s*(?:" + methods + @")\s*\(", RegexOptions.CultureInvariant);
    }

    /// <summary>Member declarations in order; each spans to the next one, and overloads carry their parameter types.</summary>
    private static List<Member> Members(string code)
    {
        var matches = Declaration.Matches(code).ToArray();
        var repeated = matches.GroupBy(match => match.Groups[1].Value, StringComparer.Ordinal)
            .Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet(StringComparer.Ordinal);
        var members = new List<Member>();
        for (var index = 0; index < matches.Length; index++)
        {
            var match = matches[index];
            var name = match.Groups[1].Value;
            var label = repeated.Contains(name) ? name + "(" + ParameterTypes(code, match.Index + match.Length - 1) + ")" : name;
            var end = index + 1 < matches.Length ? matches[index + 1].Index : code.Length;
            members.Add(new(label, match.Groups[1].Index, match.Index, end));
        }
        return members;
    }

    private static string ParameterTypes(string code, int open)
    {
        var depth = 0;
        var close = open;
        for (; close < code.Length; close++)
        {
            if (code[close] == '(') depth++;
            else if (code[close] == ')' && --depth == 0) break;
        }
        var parameters = new List<string>();
        var start = open + 1;
        depth = 0;
        for (var i = open + 1; i <= close; i++)
        {
            var c = code[i];
            if (c is '(' or '<' or '[') depth++;
            else if (c is ')' or '>' or ']') depth--;
            if ((c == ',' && depth == 0) || i == close)
            {
                parameters.Add(code[start..i]);
                start = i + 1;
            }
        }
        return string.Join(", ", parameters
            .Select(parameter => Regex.Replace(parameter.Split('=')[0], @"\s+", " ").Trim())
            .Select(parameter => Regex.Replace(parameter, @"^(?:\[[^\]]*\]\s*)*(?:(?:this|params|ref|out|in|scoped)\s+)*", ""))
            .Select(parameter => Regex.Replace(parameter, @"\s+@?\w+$", ""))
            .Where(type => type.Length > 0));
    }

    /// <summary>Blanks comments to spaces, keeping line breaks and string literals in place.</summary>
    private static string WithoutComments(string text)
    {
        var result = text.Replace("\r\n", "\n", StringComparison.Ordinal).ToCharArray();
        var i = 0;
        while (i < result.Length)
        {
            if (result[i] == '/' && i + 1 < result.Length && result[i + 1] == '/')
            {
                while (i < result.Length && result[i] != '\n') result[i++] = ' ';
            }
            else if (result[i] == '/' && i + 1 < result.Length && result[i + 1] == '*')
            {
                var close = new string(result).IndexOf("*/", i + 2, StringComparison.Ordinal);
                var end = close < 0 ? result.Length : close + 2;
                for (; i < end; i++) if (result[i] != '\n') result[i] = ' ';
            }
            else if (result[i] == '\'')
            {
                i += result[Math.Min(i + 1, result.Length - 1)] == '\\' ? 1 : 0;
                var close = Array.IndexOf(result, '\'', Math.Min(i + 2, result.Length - 1));
                i = close < 0 ? result.Length : close + 1;
            }
            else if (result[i] == '"')
            {
                i = SkipString(result, i);
            }
            else i++;
        }
        return new string(result);
    }

    private static int SkipString(char[] text, int open)
    {
        var verbatim = open > 0 && (text[open - 1] == '@' || open > 1 && text[open - 2] == '@' && text[open - 1] == '$');
        if (open + 2 < text.Length && text[open + 1] == '"' && text[open + 2] == '"')
        {
            var raw = new string(text).IndexOf("\"\"\"", open + 3, StringComparison.Ordinal);
            return raw < 0 ? text.Length : raw + 3;
        }
        for (var i = open + 1; i < text.Length; i++)
        {
            if (!verbatim && text[i] == '\\') { i++; continue; }
            if (text[i] != '"') continue;
            if (verbatim && i + 1 < text.Length && text[i + 1] == '"') { i++; continue; }
            return i + 1;
        }
        return text.Length;
    }

    /// <summary>Every listed member holding exactly its pinned hits, in the order a route takes them.</summary>
    private static Dictionary<string, string> Listed()
    {
        var samples = new Dictionary<Rule, string>
        {
            [Rule.Stale] = "if (GameRequestFreshness.IsStale(metadata)) return;",
            [Rule.TowerLookup] = "_ = bossTowers.Find(session);",
            [Rule.ParticipationLookup] = "_ = participation.Find(session.CharacterId);",
            [Rule.Locate] = "_ = DungeonLocator.Locate(session, participation, bossTowers, illusionTowers);",
        };
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Game/Services.cs"] = "internal sealed class Services(IBossTowerDomainService bossTowers, "
                + "IIllusionTowerDomainService illusionTowers, IDungeonParticipationDomainService participation);",
        };
        foreach (var file in Allowed.GroupBy(site => site.Path, StringComparer.Ordinal))
        {
            var members = file.GroupBy(site => site.Member, StringComparer.Ordinal).Select(member =>
            {
                var open = member.Key.IndexOf('(', StringComparison.Ordinal);
                var name = open < 0 ? member.Key : member.Key[..open];
                var parameters = open < 0 ? "" : string.Join(", ", member.Key[(open + 1)..^1].Split(", ")
                    .Select((type, index) => $"{type} p{index}"));
                var body = string.Concat(member.OrderBy(site => site.Rule)
                    .SelectMany(site => Enumerable.Repeat("        " + samples[site.Rule] + "\n", site.Count)));
                // An overload keeps its parameter types in its label only beside a sibling of the same name.
                var sibling = open < 0 ? "" : $"    private void {name}() {{ }}\n";
                return $"    private void {name}({parameters})\n    {{\n{body}    }}\n{sibling}";
            });
            files[file.Key] = "internal sealed partial class Listed\n{\n" + string.Concat(members.Distinct(StringComparer.Ordinal)) + "}\n";
        }
        return files;
    }

    private static Dictionary<string, string> ProductionSources()
    {
        var source = Path.Combine(Repository(), "Server", "src", "Dfo.Server.Protocol");
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(source, "*.cs", SearchOption.AllDirectories))
        {
            var path = Path.GetRelativePath(source, file).Replace('\\', '/');
            if (path.StartsWith("bin/", StringComparison.Ordinal) || path.StartsWith("obj/", StringComparison.Ordinal))
                continue;
            files[path] = File.ReadAllText(file);
        }
        return files;
    }

    private static string Repository()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (Directory.Exists(Path.Combine(directory.FullName, "Server", "src", "Dfo.Server.Protocol"))) return directory.FullName;
        throw new DirectoryNotFoundException("The dungeon owner routing source test requires the repository checkout.");
    }
}
