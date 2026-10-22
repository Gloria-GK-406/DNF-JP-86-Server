using System.Text.RegularExpressions;

namespace Dfo.Server.Application.Tests.Execution;

/// <summary>
/// Pins where work may leave a job's flow: <c>GameplayJob.RunDetached*</c> and
/// <c>ExecutionContext.SuppressFlow</c> only appear at the listed sites (TASK-04 Remaining R2-2).
/// Adding a site means changing the list here and the detachment table in
/// <c>verification/entry-reconciliation.md</c> together. This only fixes where detachment happens;
/// that no job awaits a detached task is shown by reading the sites (TASK-16 section 3.2).
/// </summary>
public sealed class DetachmentSiteTests
{
    private static readonly IReadOnlyDictionary<string, string> RunDetachedSites = new Dictionary<string, string>
    {
        ["Dfo.Server.Abstractions/Execution/GameplayJob.cs"] = "the definition",
        ["Dfo.Server.Networking/Runtime/NetworkPlugins.cs"] =
            "transport callbacks may run inline inside a job's send; each is a flow boundary, not forked work",
    };

    private static readonly IReadOnlyDictionary<string, string> SuppressFlowSites = new Dictionary<string, string>
    {
        ["Dfo.Server.Application/Execution/GameplayExecutor.cs"] = "the consumer loop must not carry its starter's flow",
        ["Dfo.Server.Networking/Runtime/ExecutorSendDeadline.cs"] = "the deadline close runs after the job, unawaited",
        ["Dfo.Server.Networking/Runtime/TouchSocketNetworkConnectionCloser.cs"] = "a detached close never inherits the job",
        ["Dfo.Server.Protocol/Game/CardReward/DungeonCardPresentationCoordinator.cs"] =
            "card timers outlive the job that scheduled them; only the drain waits for them",
        ["Dfo.Server.Networking/Outbound/OutboundSender.cs"] = "admission and per-connection drain loops (not wired until T-006)",
    };

    private static readonly Regex RunDetachedCall = new(@"\bRunDetached(?:Async)?\s*(?:<[^>()]*>)?\s*\(", RegexOptions.Compiled);
    private static readonly Regex SuppressFlowCall = new(@"\bSuppressFlow\s*\(", RegexOptions.Compiled);

    [Fact]
    public void Detached_work_starts_only_at_the_listed_sites()
    {
        var source = Path.Combine(Repository(), "Server", "src");
        var violations = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(source, "*.cs", SearchOption.AllDirectories))
        {
            var path = Path.GetRelativePath(source, file).Replace('\\', '/');
            if (path.Contains("/bin/", StringComparison.Ordinal) || path.Contains("/obj/", StringComparison.Ordinal))
                continue;
            var text = File.ReadAllText(file);
            violations.AddRange(Violations(path, text));
            var code = WithoutComments(text);
            if (RunDetachedCall.IsMatch(code) || SuppressFlowCall.IsMatch(code))
                seen.Add(path);
        }

        Assert.Empty(violations);
        // A listed site that no longer detaches is a stale exemption.
        var stale = RunDetachedSites.Keys.Concat(SuppressFlowSites.Keys).Except(seen).ToArray();
        Assert.Empty(stale);
    }

    [Theory]
    [InlineData("Dfo.Server.Application/Somewhere/NewService.cs", "GameplayJob.RunDetached(() => { });")]
    [InlineData("Dfo.Server.Protocol/Game/Some/Handler.cs", "await GameplayJob.RunDetachedAsync(async () => await work());")]
    [InlineData("Dfo.Server.GmWeb/Endpoint.cs", "var x = GameplayJob.RunDetached<int>(() => 1);")]
    [InlineData("Dfo.Server.Application/Somewhere/NewService.cs", "using (ExecutionContext.SuppressFlow()) _ = Task.Run(work);")]
    [InlineData("Dfo.Server.Application/Execution/GameplayExecutor.cs", "GameplayJob.RunDetached(() => { });")]
    [InlineData("Dfo.Server.Networking/Runtime/NetworkPlugins.cs", "using var flow = ExecutionContext.SuppressFlow();")]
    public void A_detachment_outside_the_listed_sites_is_refused(string path, string statement)
    {
        var text = "class C\n{\n    void M()\n    {\n        " + statement + "\n    }\n}\n";

        Assert.NotEmpty(Violations(path, text));
    }

    [Fact]
    public void Comments_and_the_listed_sites_are_not_refused()
    {
        const string commented =
            "/// <see cref=\"GameplayJob.RunDetached(Action)\"/>\n// ExecutionContext.SuppressFlow()\n/* RunDetached( */ class C { }\n";

        Assert.Empty(Violations("Dfo.Server.Application/Somewhere/NewService.cs", commented));
        Assert.Empty(Violations("Dfo.Server.Networking/Runtime/NetworkPlugins.cs", "GameplayJob.RunDetachedAsync(async () => { });"));
        Assert.Empty(Violations("Dfo.Server.Application/Execution/GameplayExecutor.cs", "using (ExecutionContext.SuppressFlow()) { }"));
    }

    private static IEnumerable<string> Violations(string path, string text)
    {
        var code = WithoutComments(text);
        if (RunDetachedCall.IsMatch(code) && !RunDetachedSites.ContainsKey(path))
            yield return path + ": RunDetached outside the listed sites";
        if (SuppressFlowCall.IsMatch(code) && !SuppressFlowSites.ContainsKey(path))
            yield return path + ": SuppressFlow outside the listed sites";
    }

    private static string WithoutComments(string text) =>
        Regex.Replace(Regex.Replace(text, @"/\*.*?\*/", " ", RegexOptions.Singleline), @"//[^\n]*", " ");

    private static string Repository()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (Directory.Exists(Path.Combine(directory.FullName, "Server", "src"))) return directory.FullName;
        throw new DirectoryNotFoundException("The detachment site test requires the repository checkout.");
    }
}
