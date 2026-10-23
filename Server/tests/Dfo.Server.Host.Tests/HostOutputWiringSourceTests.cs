using System.Text.RegularExpressions;

namespace Dfo.Server.Host.Tests;

/// <summary>
/// F-61: every test file that starts a process hands each line of its standard output and standard error to
/// <see cref="Dfo.Server.Testing.GameplayAssertionRecorder.ObserveHostOutput"/>. A packaged Host has no recorder of
/// its own, so that wiring is the only way an assertion exception it prints becomes an <c>exe:</c> record
/// (TASK-18 N4: nothing guarded it).
/// </summary>
/// <remarks>
/// A raw text scan of <c>Server/tests</c>. A file starts a process when it names <c>ProcessStartInfo</c> as a word
/// (constructor or object initializer) or calls <c>Process.Start(</c>; a start through another spelling, such as an
/// instance <c>process.Start()</c> on a <c>Process</c> built without either, is not recognised. The scan checks that
/// such a file also subscribes both output events and calls the recorder, not that the three are connected, nor which
/// of a file's processes they serve. A violation the child process swallows without
/// printing stays unseen whatever this says. This file spells the same tokens in its counter-examples, so it is left
/// out of the scan by its path.
/// </remarks>
public sealed class HostOutputWiringSourceTests
{
    private static readonly Regex StartsAProcess =
        new(@"\bProcessStartInfo\b|\bProcess\.Start\(", RegexOptions.CultureInvariant);

    private static readonly string[] Wiring =
        ["OutputDataReceived +=", "ErrorDataReceived +=", "GameplayAssertionRecorder.ObserveHostOutput("];

    private const string Self = "Dfo.Server.Host.Tests/HostOutputWiringSourceTests.cs";

    /// <summary>Each test file that starts a process, and what it starts.</summary>
    private static readonly IReadOnlyDictionary<string, string> Starters =
        new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Dfo.Server.Host.Tests/EndToEndHostHarness.cs"] = "the packaged Host behind the end-to-end and GM tests",
        ["Dfo.Server.Host.Tests/LauncherHttpHostStartupTests.cs"] = "the packaged Host with and without its launcher HTTP listener",
        ["Dfo.Server.Host.Tests/ChannelHostStartupTests.cs"] = "the packaged Host from an unrelated working directory",
    };

    [Fact]
    public void Every_test_file_that_starts_a_process_passes_its_output_to_the_recorder()
    {
        var violations = Violations(TestSources());

        Assert.True(violations.Count == 0, string.Join("\n", violations));
    }

    [Fact]
    public void A_file_that_starts_a_process_without_the_recorder_is_named()
    {
        var files = Listed();
        files["Dfo.Server.Networking.Tests/Somewhere.cs"] = """
            var info = new ProcessStartInfo("dotnet");
            process.OutputDataReceived += (_, args) => output.AppendLine(args.Data);
            process.ErrorDataReceived += (_, args) => output.AppendLine(args.Data);
            """;

        var violations = Violations(files);

        Assert.Equal(2, violations.Count);
        Assert.Contains("Dfo.Server.Networking.Tests/Somewhere.cs starts a process and does not have GameplayAssertionRecorder.ObserveHostOutput(", violations);
        Assert.Contains("Dfo.Server.Networking.Tests/Somewhere.cs starts a process and is not on the list", violations);
    }

    [Fact]
    public void A_process_started_with_an_object_initializer_is_named()
    {
        var files = Listed();
        files["Dfo.Server.Host.Tests/Elsewhere.cs"] = """
            var info = new ProcessStartInfo { FileName = "dotnet", RedirectStandardOutput = true };
            """;

        var violations = Violations(files);

        Assert.Equal(4, violations.Count);
        Assert.All(violations, violation => Assert.StartsWith("Dfo.Server.Host.Tests/Elsewhere.cs starts a process", violation));
    }

    [Fact]
    public void A_listed_file_that_no_longer_starts_a_process_is_named()
    {
        var files = Listed();
        files["Dfo.Server.Host.Tests/ChannelHostStartupTests.cs"] = "public sealed class ChannelHostStartupTests { }";

        var violations = Violations(files);

        Assert.Equal(new[] { "Dfo.Server.Host.Tests/ChannelHostStartupTests.cs is listed and no longer starts a process" }, violations);
    }

    private static IReadOnlyList<string> Violations(IReadOnlyDictionary<string, string> files)
    {
        var violations = new List<string>();
        foreach (var (path, text) in files.OrderBy(file => file.Key, StringComparer.Ordinal))
        {
            if (!StartsAProcess.IsMatch(text))
                continue;
            violations.AddRange(Wiring
                .Where(token => !text.Contains(token, StringComparison.Ordinal))
                .Select(token => $"{path} starts a process and does not have {token}"));
            if (!Starters.ContainsKey(path))
                violations.Add($"{path} starts a process and is not on the list");
        }
        violations.AddRange(Starters.Keys
            .Where(path => !files.TryGetValue(path, out var text) || !StartsAProcess.IsMatch(text))
            .Order(StringComparer.Ordinal)
            .Select(path => $"{path} is listed and no longer starts a process"));
        return violations;
    }

    /// <summary>Every listed file, starting a process and wired.</summary>
    private static Dictionary<string, string> Listed() =>
        Starters.Keys.ToDictionary(path => path,
            _ => "new ProcessStartInfo(\"dotnet\");\n" + string.Join("\n", Wiring),
            StringComparer.Ordinal);

    private static Dictionary<string, string> TestSources()
    {
        var root = Path.Combine(Repository(), "Server", "tests");
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            var path = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (path == Self || path.Contains("/bin/", StringComparison.Ordinal) || path.Contains("/obj/", StringComparison.Ordinal)
                || path.Contains("/TestResults/", StringComparison.Ordinal))
                continue;
            files[path] = File.ReadAllText(file);
        }
        return files;
    }

    private static string Repository()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (Directory.Exists(Path.Combine(directory.FullName, "Server", "tests"))) return directory.FullName;
        throw new DirectoryNotFoundException("The host output wiring source test requires the repository checkout.");
    }
}
