using System.Text.RegularExpressions;
using Xunit.Abstractions;

namespace Dfo.Server.Application.Tests.Execution;

/// <summary>
/// AC-017 / RQ-060: the gates spec appendix F deletes leave no type, registration, forward or comment
/// behind, and every <c>SemaphoreSlim</c> left in the production sources is on a list that says why.
/// </summary>
/// <remarks>
/// The scan reads the raw text, comments included: a gate name left in a comment misleads the next
/// reader who greps for gates. T-005b widens the Protocol-scoped names to the whole of <c>Server/src</c>
/// and shortens the semaphore list; T-006 removes the party delivery gates and their two entries. Not
/// named here until then: the party delivery gates, <c>NotificationRetryQueue</c> and
/// <c>NotificationRetryDrainService</c> (T-006), and the Application gate types (T-005b).
/// </remarks>
public sealed class LockRemovalSourceTests(ITestOutputHelper output)
{
    private const string Everywhere = "";
    private const string Protocol = "Dfo.Server.Protocol/";

    private static readonly NameRule[] Names =
    [
        .. new[]
        {
            "TownAreaProjectionGate", "DungeonProjectionGate", "BossTowerProjectionGate", "BossTowerEntryGate",
            "IllusionTowerProjectionGate", "IllusionTowerEntryGate", "AchievementTitleProjectionGate",
            "LeaseSet", "TransitionLeaseSet", "ProjectionLeaseSet", "WeddingProposalExecution",
        }.Select(name => new NameRule(name, Everywhere, null)),
        .. new[]
        {
            "SelectedSessionTransitionGate", "OnlineUserExecutionGate", "UserExecutionGate",
            "BorrowOrAcquireSharedAsync", "BorrowOrAcquireExclusiveAsync", "AcquireManyExclusiveAsync",
            "TransactionalExecutionGate",
        }.Select(name => new NameRule(name, Protocol, null)),
        // The command registrations still declare access modes until T-005b removes them.
        new NameRule("UserAccessMode", Protocol, "Dfo.Server.Protocol/ProtocolModule.cs"),
    ];

    private static readonly IReadOnlyDictionary<string, string> Semaphores = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Dfo.Server.Protocol/Game/Party/PartyNotificationSlice.cs"] = "party delivery gates (DR-17); T-006 deletes",
        ["Dfo.Server.Abstractions/Execution/GameplayAssertions.cs"] = "GameplayGateAssertion.Enter takes the party gate; T-006 deletes",
        ["Dfo.Server.Application/DomainEvents/DomainEventDispatcher.cs"] = "subscriber group gates; T-005b deletes",
        ["Dfo.Server.Application/Scheduling/ScheduledTaskHostedService.cs"] = "concurrency slots; T-005b deletes",
        ["Dfo.Server.Application/Commands/Pipeline/TransactionalExecutionBehavior.cs"] = "TransactionalExecutionGate; T-005b deletes",
        ["Dfo.Server.Application/Events/JoustMaintenanceService.cs"] = "maintenance tick; T-005b deletes",
        ["Dfo.Server.Application/Effects/EffectOperationLedger.cs"] = "per-connection ledger; T-005b deletes",
        ["Dfo.Server.Application/Events/JoustRuleService.cs"] = "rule initialization (appendix F keep); stays",
        ["Dfo.Server.Application/Characters/CharacterStatRuleService.cs"] = "rule initialization (appendix F keep); stays",
        ["Dfo.Server.Application/Authentication/PasswordComputationLimiter.cs"] = "password hashing limit off the executor (RQ-007); stays",
        ["Dfo.Server.Infrastructure/Content/PvfItemCatalogSource.cs"] = "PVF content initialization (appendix F keep); stays",
        ["Dfo.Server.Infrastructure/Content/PvfBossTowerContentSource.cs"] = "PVF content initialization (appendix F keep); stays",
    };

    private static readonly Regex Semaphore = new(@"\bSemaphoreSlim\b", RegexOptions.CultureInvariant);

    [Fact]
    public void No_deleted_gate_name_is_left_in_the_production_sources()
    {
        var files = ProductionSources();
        output.WriteLine($"scanned source files: {files.Count}");
        foreach (var rule in Names)
            output.WriteLine($"rule {rule.Name} scope '{(rule.Scope.Length == 0 ? "Server/src" : rule.Scope)}'"
                + $"{(rule.Exempt is null ? "" : $" except {rule.Exempt}")}: {Hits(files, rule).Count} hits");

        var violations = NameViolations(files);

        Assert.True(violations.Count == 0, string.Join("\n", violations));
    }

    [Fact]
    public void Every_production_semaphore_is_on_the_list_and_the_list_has_no_stale_entry()
    {
        var files = ProductionSources();
        output.WriteLine($"scanned source files: {files.Count}");
        foreach (var (path, reason) in Semaphores)
            output.WriteLine($"listed {path} ({reason}): "
                + $"{(files.TryGetValue(path, out var text) ? Semaphore.Matches(text).Count : 0)} occurrences");

        var violations = SemaphoreViolations(files);

        Assert.True(violations.Count == 0, string.Join("\n", violations));
    }

    [Fact]
    public void A_global_name_is_named_in_any_file()
    {
        var violation = Assert.Single(NameViolations(new Dictionary<string, string>
        {
            ["Dfo.Server.GmWeb/Somewhere.cs"] = "internal sealed class TownAreaProjectionGate;",
        }));

        Assert.Contains("Dfo.Server.GmWeb/Somewhere.cs", violation, StringComparison.Ordinal);
        Assert.Contains("TownAreaProjectionGate", violation, StringComparison.Ordinal);
    }

    [Fact]
    public void A_name_left_only_in_a_comment_is_still_named()
    {
        var violation = Assert.Single(NameViolations(new Dictionary<string, string>
        {
            ["Dfo.Server.Application/Somewhere.cs"] = "// once held a ProjectionLeaseSet here\nclass Somewhere { }",
        }));

        Assert.Contains("ProjectionLeaseSet", violation, StringComparison.Ordinal);
    }

    [Fact]
    public void A_protocol_scoped_name_is_named_in_protocol_and_not_in_application()
    {
        var violation = Assert.Single(NameViolations(new Dictionary<string, string>
        {
            ["Dfo.Server.Protocol/Game/Somewhere.cs"] = "sealed class H(SelectedSessionTransitionGate gate);",
            ["Dfo.Server.Application/OnlineUsers/SelectedSessionTransitionGate.cs"] = "public sealed class SelectedSessionTransitionGate;",
        }));

        Assert.Contains("Dfo.Server.Protocol/Game/Somewhere.cs", violation, StringComparison.Ordinal);
    }

    [Fact]
    public void An_access_mode_is_allowed_in_the_protocol_module_only()
    {
        var violation = Assert.Single(NameViolations(new Dictionary<string, string>
        {
            ["Dfo.Server.Protocol/ProtocolModule.cs"] = "services.AddCommandHandler(UserAccessMode.Shared);",
            ["Dfo.Server.Protocol/Game/Teleport/TeleportSlice.cs"] = "var mode = UserAccessMode.Shared;",
        }));

        Assert.Contains("Dfo.Server.Protocol/Game/Teleport/TeleportSlice.cs", violation, StringComparison.Ordinal);
    }

    [Fact]
    public void A_semaphore_outside_the_list_is_named()
    {
        var files = Semaphores.Keys.ToDictionary(path => path, _ => "SemaphoreSlim gate;", StringComparer.Ordinal);
        files["Dfo.Server.Protocol/Game/Somewhere.cs"] = "private readonly SemaphoreSlim probe = new(1, 1);";

        var violation = Assert.Single(SemaphoreViolations(files));

        Assert.Contains("Dfo.Server.Protocol/Game/Somewhere.cs", violation, StringComparison.Ordinal);
    }

    [Fact]
    public void A_listed_file_without_a_semaphore_is_a_stale_entry()
    {
        var files = Semaphores.Keys.ToDictionary(path => path, _ => "SemaphoreSlim gate;", StringComparer.Ordinal);
        files["Dfo.Server.Application/Events/JoustMaintenanceService.cs"] = "class JoustMaintenanceService { }";

        var violation = Assert.Single(SemaphoreViolations(files));

        Assert.Contains("Dfo.Server.Application/Events/JoustMaintenanceService.cs", violation, StringComparison.Ordinal);
        Assert.Contains("stale", violation, StringComparison.Ordinal);
    }

    [Fact]
    public void A_name_inside_a_longer_identifier_is_not_named()
    {
        Assert.Empty(NameViolations(new Dictionary<string, string>
        {
            ["Dfo.Server.Protocol/Game/Somewhere.cs"] = "sealed class TownAreaProjectionGateway; var x = OnlineUserExecutionGateKey;",
        }));
    }

    private static IReadOnlyList<string> NameViolations(IReadOnlyDictionary<string, string> files) =>
        Names.SelectMany(rule => Hits(files, rule).Select(hit => $"{hit} names {rule.Name}")).ToList();

    private static IReadOnlyList<string> Hits(IReadOnlyDictionary<string, string> files, NameRule rule)
    {
        var pattern = new Regex($@"\b{rule.Name}\b", RegexOptions.CultureInvariant);
        var hits = new List<string>();
        foreach (var (path, text) in files.OrderBy(file => file.Key, StringComparer.Ordinal))
        {
            if (!path.StartsWith(rule.Scope, StringComparison.Ordinal) || path == rule.Exempt) continue;
            var lines = text.Split('\n');
            for (var index = 0; index < lines.Length; index++)
                if (pattern.IsMatch(lines[index])) hits.Add($"{path}:{index + 1}");
        }
        return hits;
    }

    private static IReadOnlyList<string> SemaphoreViolations(IReadOnlyDictionary<string, string> files)
    {
        var naming = files.Where(file => Semaphore.IsMatch(file.Value)).Select(file => file.Key).ToHashSet(StringComparer.Ordinal);
        return naming.Where(path => !Semaphores.ContainsKey(path)).Order(StringComparer.Ordinal)
            .Select(path => $"{path} uses SemaphoreSlim and is not on the list")
            .Concat(Semaphores.Keys.Where(path => !naming.Contains(path)).Order(StringComparer.Ordinal)
                .Select(path => $"{path} is listed but no longer uses SemaphoreSlim: stale entry"))
            .ToList();
    }

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
        throw new DirectoryNotFoundException("The lock removal source test requires the repository checkout.");
    }

    private sealed record NameRule(string Name, string Scope, string? Exempt);
}
