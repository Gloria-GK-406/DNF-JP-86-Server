using System.Text.RegularExpressions;
using Xunit.Abstractions;

namespace Dfo.Server.Application.Tests.Execution;

/// <summary>
/// AC-017 / RQ-060: the gates spec appendix F deletes leave no type, registration, forward or comment
/// behind, every <c>SemaphoreSlim</c> left in the production sources is on a list that says why and how
/// many times it occurs, and none of the other waiting primitives named here is used.
/// </summary>
/// <remarks>
/// The scan reads the raw text, comments included: a gate name left in a comment misleads the next
/// reader who greps for gates. Every item of appendix F's delete column is named here; the party
/// delivery gates by their members (the per-character semaphores here, <c>PartyDeliveryStream.Gate</c> through
/// the semaphore list, which no longer lists the party file), since the stream itself still carries the
/// revision watermark (DR-5). A hand-made wait over
/// <c>TaskCompletionSource</c> is guarded by naming its type, not by scanning for the primitive; a short
/// <c>lock</c> is kept by appendix F and is not a waiting primitive here, on a <c>System.Threading.Lock</c> as on
/// any object. Entering a <c>Lock</c> explicitly is recognised by its parameterless <c>Enter</c>,
/// <c>EnterScope</c> and <c>TryEnter</c> calls, since the text does not say a field's type at the call site.
/// </remarks>
public sealed class LockRemovalSourceTests(ITestOutputHelper output)
{
    private static readonly string[] Names =
    [
        "TownAreaProjectionGate", "DungeonProjectionGate", "BossTowerProjectionGate", "BossTowerEntryGate",
        "IllusionTowerProjectionGate", "IllusionTowerEntryGate", "AchievementTitleProjectionGate",
        "LeaseSet", "TransitionLeaseSet", "ProjectionLeaseSet", "WeddingProposalExecution",
        "SelectedSessionTransitionGate", "OnlineUserExecutionGate", "UserExecutionGate",
        "BorrowOrAcquireSharedAsync", "BorrowOrAcquireExclusiveAsync", "AcquireManyExclusiveAsync",
        "TransactionalExecutionGate", "UserAccessMode",
        "AccountExecutionGate", "OnlineUserExecutionBehavior", "AccountExecutionBehavior", "TransactionalExecutionBehavior",
        "OnlineUserAccessMode", "AccountAccessMode", "IMultiAccountCommand", "IOnlineUserCommand",
        "ScheduledTaskSchedulerOptions", "groupGates",
        // T-006 delivery (TASK-23): the direct send path, its executor deadline, the notification retry
        // queue and its drain, the retry options, the superseded-login close loop and the direct closer.
        "TouchSocketNetworkMessageSender", "ExecutorSendDeadline", "NotificationRetryQueue",
        "NotificationRetryDrainService", "INotificationRetryDrain", "NotificationDeliveryOptions",
        "CloseSupersededAsync", "TouchSocketNetworkConnectionCloser",
        // T-006 delivery (TASK-24): the party participant gates and the gate assertion that guarded them.
        "GameplayGateAssertion", "GameplayGateWaitException", "EnterParticipantsAsync", "PartyParticipantLease",
        "participantGates",
    ];

    /// <summary>Waiting primitives other than <c>SemaphoreSlim</c>; none is allowed anywhere.</summary>
    private static readonly string[] OtherWaits =
    [
        "ReaderWriterLockSlim", "ReaderWriterLock", "Semaphore", "Mutex", "SpinLock", "Barrier", "CountdownEvent",
        "ManualResetEventSlim", "ManualResetEvent", "AutoResetEvent", "Monitor.Enter", "Monitor.TryEnter", "Monitor.Wait",
        "EventWaitHandle", "WaitHandle", "SpinWait",
    ];

    /// <summary>Explicit entry of a <c>System.Threading.Lock</c>; a <c>lock</c> statement on one stays allowed.</summary>
    private static readonly (string Name, Regex Pattern)[] OtherWaitCalls =
    [
        ("Lock.Enter() / Lock.EnterScope()", new Regex(@"\.(?:Enter|EnterScope)\(\s*\)", RegexOptions.CultureInvariant)),
        ("Lock.TryEnter(...)", new Regex(@"\.TryEnter\(", RegexOptions.CultureInvariant)),
    ];

    /// <summary>Each file that may use <c>SemaphoreSlim</c>, how many times the name occurs in it, and why.</summary>
    private static readonly IReadOnlyDictionary<string, (int Count, string Reason)> Semaphores =
        new Dictionary<string, (int Count, string Reason)>(StringComparer.Ordinal)
    {
        ["Dfo.Server.Application/Execution/TypedGameplayTimerSource.cs"] = (1, "accepted F-07 scan/session rounds serialize only off the executor; no world-state gate"),
        ["Dfo.Server.Application/Events/JoustRuleService.cs"] = (1, "rule initialization (appendix F keep); stays"),
        ["Dfo.Server.Application/Characters/CharacterStatRuleService.cs"] = (1, "rule initialization (appendix F keep); stays"),
        ["Dfo.Server.Application/Authentication/PasswordComputationLimiter.cs"] = (2, "password hashing limit off the executor (RQ-007); stays"),
        ["Dfo.Server.Infrastructure/Content/PvfItemCatalogSource.cs"] = (1, "PVF content initialization (appendix F keep); stays"),
        ["Dfo.Server.Infrastructure/Content/PvfBossTowerContentSource.cs"] = (1, "PVF content initialization (appendix F keep); stays"),
    };

    private static readonly Regex Semaphore = new(@"\bSemaphoreSlim\b", RegexOptions.CultureInvariant);

    [Fact]
    public void No_deleted_gate_name_is_left_in_the_production_sources()
    {
        var files = ProductionSources();
        output.WriteLine($"scanned source files: {files.Count}");
        foreach (var name in Names)
            output.WriteLine($"rule {name} scope 'Server/src': {Hits(files, name).Count} hits");

        var violations = NameViolations(files);

        Assert.True(violations.Count == 0, string.Join("\n", violations));
    }

    [Fact]
    public void Every_production_semaphore_is_on_the_list_and_the_list_has_no_stale_entry()
    {
        var files = ProductionSources();
        output.WriteLine($"scanned source files: {files.Count}");
        foreach (var (path, (count, reason)) in Semaphores)
            output.WriteLine($"listed {path} ({reason}): "
                + $"{(files.TryGetValue(path, out var text) ? Semaphore.Matches(text).Count : 0)} occurrences, pinned {count}");

        var violations = SemaphoreViolations(files);

        Assert.True(violations.Count == 0, string.Join("\n", violations));
    }

    [Fact]
    public void No_other_waiting_primitive_is_used_in_the_production_sources()
    {
        var files = ProductionSources();
        output.WriteLine($"scanned source files: {files.Count}");
        foreach (var name in OtherWaits)
            output.WriteLine($"primitive {name} scope 'Server/src': {Hits(files, name).Count} hits");
        foreach (var (name, pattern) in OtherWaitCalls)
            output.WriteLine($"call {name} scope 'Server/src': {Hits(files, pattern).Count} hits");

        var violations = OtherWaitViolations(files);

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
        var violations = NameViolations(new Dictionary<string, string>
        {
            ["Dfo.Server.Protocol/Game/Somewhere.cs"] = "sealed class H(SelectedSessionTransitionGate gate);",
            ["Dfo.Server.Application/OnlineUsers/SelectedSessionTransitionGate.cs"] = "public sealed class SelectedSessionTransitionGate;",
        });

        Assert.Equal(2, violations.Count);
        Assert.Contains(violations, violation => violation.Contains("Dfo.Server.Protocol/Game/Somewhere.cs", StringComparison.Ordinal));
        Assert.Contains(violations, violation => violation.Contains("Dfo.Server.Application/OnlineUsers/SelectedSessionTransitionGate.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void An_access_mode_is_allowed_in_the_protocol_module_only()
    {
        var violations = NameViolations(new Dictionary<string, string>
        {
            ["Dfo.Server.Protocol/ProtocolModule.cs"] = "services.AddCommandHandler(UserAccessMode.Shared);",
            ["Dfo.Server.Protocol/Game/Teleport/TeleportSlice.cs"] = "var mode = UserAccessMode.Shared;",
        });

        Assert.Equal(2, violations.Count);
        Assert.Contains(violations, violation => violation.Contains("Dfo.Server.Protocol/ProtocolModule.cs", StringComparison.Ordinal));
        Assert.Contains(violations, violation => violation.Contains("Dfo.Server.Protocol/Game/Teleport/TeleportSlice.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void A_pipeline_behaviour_name_is_named_outside_application_and_a_longer_identifier_is_not()
    {
        var violation = Assert.Single(NameViolations(new Dictionary<string, string>
        {
            ["Dfo.Server.Host/Somewhere.cs"] = "services.AddScoped(typeof(AccountExecutionBehavior<>));",
            ["Dfo.Server.Protocol/Game/Elsewhere.cs"] = "sealed record Command : IMultiAccountCommandShim;",
        }));

        Assert.Contains("Dfo.Server.Host/Somewhere.cs", violation, StringComparison.Ordinal);
        Assert.Contains("AccountExecutionBehavior", violation, StringComparison.Ordinal);
    }

    [Fact]
    public void Another_waiting_primitive_is_named_in_any_file()
    {
        var violation = Assert.Single(OtherWaitViolations(new Dictionary<string, string>
        {
            ["Dfo.Server.Infrastructure/Somewhere.cs"] = "private readonly ReaderWriterLockSlim probe = new();",
            ["Dfo.Server.Application/Elsewhere.cs"] = "private readonly SemaphoreSlim probe = new(1, 1); lock (gate) { }",
        }));

        Assert.Contains("Dfo.Server.Infrastructure/Somewhere.cs", violation, StringComparison.Ordinal);
        Assert.Contains("ReaderWriterLockSlim", violation, StringComparison.Ordinal);
    }

    [Fact]
    public void A_semaphore_outside_the_list_is_named()
    {
        var files = Listed();
        files["Dfo.Server.Protocol/Game/Somewhere.cs"] = "private readonly SemaphoreSlim probe = new(1, 1);";

        var violation = Assert.Single(SemaphoreViolations(files));

        Assert.Contains("Dfo.Server.Protocol/Game/Somewhere.cs", violation, StringComparison.Ordinal);
    }

    [Fact]
    public void A_listed_file_without_a_semaphore_is_a_stale_entry()
    {
        var files = Listed();
        files["Dfo.Server.Application/Events/JoustRuleService.cs"] = "class JoustRuleService { }";

        var violation = Assert.Single(SemaphoreViolations(files));

        Assert.Contains("Dfo.Server.Application/Events/JoustRuleService.cs", violation, StringComparison.Ordinal);
        Assert.Contains("stale", violation, StringComparison.Ordinal);
    }

    [Fact]
    public void A_listed_file_with_one_more_semaphore_than_pinned_is_named()
    {
        var files = Listed();
        files["Dfo.Server.Application/Events/JoustRuleService.cs"] += "\nprivate readonly SemaphoreSlim second = new(1, 1);";

        var violation = Assert.Single(SemaphoreViolations(files));

        Assert.Contains("Dfo.Server.Application/Events/JoustRuleService.cs", violation, StringComparison.Ordinal);
        Assert.Contains("pinned 1", violation, StringComparison.Ordinal);
    }

    [Fact]
    public void Wait_handles_spin_waits_and_explicit_lock_entry_are_named_and_a_lock_statement_is_not()
    {
        var violations = OtherWaitViolations(new Dictionary<string, string>
        {
            ["Dfo.Server.Application/A.cs"] = "private readonly EventWaitHandle signal = new(false, EventResetMode.AutoReset);",
            ["Dfo.Server.Application/B.cs"] = "WaitHandle.WaitAll(handles);",
            ["Dfo.Server.Application/C.cs"] = "var spinner = new SpinWait(); spinner.SpinOnce();",
            ["Dfo.Server.Application/D.cs"] = "gate.Enter();",
            ["Dfo.Server.Application/E.cs"] = "using var held = gate.EnterScope();",
            ["Dfo.Server.Application/F.cs"] = "if (gate.TryEnter()) { }",
            ["Dfo.Server.Application/G.cs"] = "private readonly Lock gate = new(); lock (gate) { } using var selected = scope.Enter(recipient);",
        });

        Assert.Equal(6, violations.Count);
        foreach (var file in new[] { "A", "B", "C", "D", "E", "F" })
            Assert.Contains(violations, violation => violation.StartsWith($"Dfo.Server.Application/{file}.cs:", StringComparison.Ordinal));
        Assert.DoesNotContain(violations, violation => violation.StartsWith("Dfo.Server.Application/G.cs:", StringComparison.Ordinal));
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
        Names.SelectMany(name => Hits(files, name).Select(hit => $"{hit} names {name}")).ToList();

    private static IReadOnlyList<string> OtherWaitViolations(IReadOnlyDictionary<string, string> files) =>
        OtherWaits.SelectMany(name => Hits(files, name).Select(hit => $"{hit} uses {name}"))
            .Concat(OtherWaitCalls.SelectMany(call => Hits(files, call.Pattern).Select(hit => $"{hit} uses {call.Name}")))
            .ToList();

    private static IReadOnlyList<string> Hits(IReadOnlyDictionary<string, string> files, string name) =>
        Hits(files, new Regex($@"\b{Regex.Escape(name)}\b", RegexOptions.CultureInvariant));

    private static IReadOnlyList<string> Hits(IReadOnlyDictionary<string, string> files, Regex pattern)
    {
        var hits = new List<string>();
        foreach (var (path, text) in files.OrderBy(file => file.Key, StringComparer.Ordinal))
        {
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
            .Concat(Semaphores.Where(file => naming.Contains(file.Key)).OrderBy(file => file.Key, StringComparer.Ordinal)
                .Select(file => (file.Key, Found: Semaphore.Matches(files[file.Key]).Count, Pinned: file.Value.Count))
                .Where(file => file.Found != file.Pinned)
                .Select(file => $"{file.Key} uses SemaphoreSlim {file.Found} times, pinned {file.Pinned}"))
            .ToList();
    }

    /// <summary>Every listed file, holding exactly its pinned number of semaphores.</summary>
    private static Dictionary<string, string> Listed() =>
        Semaphores.ToDictionary(file => file.Key, file => string.Join("\n", Enumerable.Repeat("SemaphoreSlim gate;", file.Value.Count)),
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
        throw new DirectoryNotFoundException("The lock removal source test requires the repository checkout.");
    }
}
