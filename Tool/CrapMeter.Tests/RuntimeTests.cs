using System.Diagnostics;
using System.Text.Json;
using CrapMeter.Runtime;
using Xunit;

namespace CrapMeter.Tests;

public sealed class RuntimeTests
{
    [Fact]
    public void Snapshot_identity_changes_when_already_dirty_content_changes()
    {
        using var repo = new RepositoryFixture();
        repo.Write("Server/src/P/F.cs", "class F { int X = 1; }");
        string before = JsonSerializer.Serialize(GitSnapshot.Read(repo.Root));
        repo.Write("Server/src/P/F.cs", "class F { int X = 2; }");
        string after = JsonSerializer.Serialize(GitSnapshot.Read(repo.Root));
        Assert.NotEqual(before, after);
    }

    [Fact]
    public void Empty_trx_is_rejected_instead_of_becoming_a_green_test_run()
    {
        using var repo = new RepositoryFixture();
        repo.Write("results/empty.trx", "<TestRun><ResultSummary><Counters total='0' executed='0' passed='0' failed='0'/></ResultSummary></TestRun>");
        Assert.Throws<InvalidDataException>(() => TrxReader.ReadAll(Path.Combine(repo.Root, "results")));
    }

    [Fact]
    public async Task Simultaneous_runs_never_share_a_directory()
    {
        using var repo = new RepositoryFixture();
        DateTimeOffset now = DateTimeOffset.Now;
        string[] paths = await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(
            () => RunDirectory.Create(Path.Combine(repo.Root, ".crap"), now).Path)));
        Assert.Equal(paths.Length, paths.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Snapshot_covers_untracked_tests_build_configuration_and_tool_but_ignores_outputs_and_documents()
    {
        using var repo = new RepositoryFixture();
        string[] inputs = ["Server/tests/P.Tests/T.cs", "Server/Directory.Build.props", "global.json",
            "Tool/CrapMeter/Runtime/F.cs", "Tool/CrapMeter.Tests/T.cs"];
        foreach (string input in inputs) repo.Write(input, "before");
        GitSnapshot before = GitSnapshot.Read(repo.Root);
        foreach (string input in inputs)
        {
            repo.Write(input, "after");
            Assert.False(before.SameContent(GitSnapshot.Read(repo.Root)));
            repo.Write(input, "before");
        }
        repo.Write("Server/src/P/obj/cache.cs", "ignored");
        repo.Write("Server/src/P/bin/assembly.dll", "ignored");
        repo.Write(".crap/run/result.json", "ignored");
        repo.Write(".rks-agent/tasks/task/state.yaml", "ignored");
        repo.Write("Docs/notes.md", "ignored");
        Assert.True(before.SameContent(GitSnapshot.Read(repo.Root)));
        repo.Write("Server/src/P/New.cs", "class New {}");
        Assert.False(before.SameContent(GitSnapshot.Read(repo.Root)));
    }

    [Fact]
    public void Snapshot_rejects_changed_head_even_when_scoped_content_is_unchanged()
    {
        using var repo = new RepositoryFixture();
        GitSnapshot before = GitSnapshot.Read(repo.Root);
        repo.Git("-c", "user.name=Runtime Test", "-c", "user.email=runtime@example.invalid", "commit", "--allow-empty", "-m", "new head");
        Assert.False(before.SameContent(GitSnapshot.Read(repo.Root)));
    }

    [Fact]
    public void Trx_requires_exact_expected_project_set_and_nonempty_consistent_results()
    {
        using var repo = new RepositoryFixture();
        repo.Write("Server/86JP.Server.slnx", "<Solution><Project Path='tests/P.Tests/P.Tests.csproj'/></Solution>");
        repo.Write("Server/tests/P.Tests/P.Tests.csproj", "<Project/>");
        const string trx = """
            <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
              <TestDefinitions><UnitTest id="t"><TestMethod codeBase="C:\repo\P.Tests.dll"/></UnitTest></TestDefinitions>
              <Results><UnitTestResult testId="t" outcome="Passed"/></Results>
              <ResultSummary><Counters total="1" executed="1" passed="1" failed="0"/></ResultSummary>
            </TestRun>
            """;
        repo.Write("results/good.trx", trx);
        List<Manifest.TestOutcome> outcomes = TrxReader.ReadAll(Path.Combine(repo.Root, "results"));
        List<string> expected = TrxReader.ExpectedProjects(Path.Combine(repo.Root, "Server/86JP.Server.slnx"));
        TrxReader.ValidateExpected(outcomes, expected);
        Assert.Throws<InvalidDataException>(() => TrxReader.ValidateExpected([], expected));
        Assert.Throws<InvalidDataException>(() => TrxReader.ValidateExpected(outcomes, ["Other.Tests"]));
        Assert.Throws<InvalidDataException>(() => TrxReader.ValidateExpected([.. outcomes, .. outcomes], expected));
        repo.Write("results/good.trx", trx.Replace("total=\"1\"", "total=\"2\"", StringComparison.Ordinal));
        Assert.Throws<InvalidDataException>(() => TrxReader.ReadAll(Path.Combine(repo.Root, "results")));
    }

    [Fact]
    public void Rerun_script_carries_every_option_and_escapes_powershell_literals()
    {
        string script = RerunScript.Create("E:/repo O'Brien", "E:/PVF $data/Script.pvf", "Release", 17, "after-tests");
        Assert.Contains("'E:/repo O''Brien'", script, StringComparison.Ordinal);
        Assert.Contains("$env:PVF_ARCHIVE_PATH = 'E:/PVF $data/Script.pvf'", script, StringComparison.Ordinal);
        Assert.Contains("'--configuration' 'Release' '--top' '17' '--simulate-failure' 'after-tests'", script, StringComparison.Ordinal);
        Assert.Contains("exit $LASTEXITCODE", script, StringComparison.Ordinal);
        Assert.DoesNotContain(".crap", script, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Failed_collection_preserves_self_describing_evidence(bool invalidSolution)
    {
        using var repo = new RepositoryFixture();
        repo.Write("Server/86JP.Server.slnx", invalidSolution ? "<invalid" :
            "<Solution><Project Path='tests/P.Tests/P.Tests.csproj'/></Solution>");
        repo.Write("Server/tests/P.Tests/P.Tests.csproj", "<Project><Target Name='Build'><Error Text='intentional fixture failure'/></Target></Project>");
        repo.Write("fixture.pvf", "fixture only; never consumed by tests");
        ProcessResult process = ProcessRunner.Run("dotnet", [typeof(GitSnapshot).Assembly.Location,
            "--repo", repo.Root, "--pvf-archive", Path.Combine(repo.Root, "fixture.pvf"),
            "--configuration", "Release", "--top", "17"], repo.Root, Path.Combine(repo.Root, "cli.log"));
        Assert.NotEqual(0, process.ExitCode);
        string run = Assert.Single(Directory.GetDirectories(Path.Combine(repo.Root, ".crap")));
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(run, "manifest.json")));
        JsonElement root = manifest.RootElement;
        Assert.Equal("failed", root.GetProperty("Run").GetProperty("Status").GetString());
        foreach (string field in new[] { "Scope", "Policy", "Tools", "Execution", "Artifacts", "Before", "After" })
            Assert.True(root.TryGetProperty(field, out _), "missing " + field);
        Assert.Equal(!invalidSolution, File.Exists(Path.Combine(run, "raw/source-inventory.json")));
        Assert.True(File.Exists(Path.Combine(run, "rerun.ps1")));
        Assert.False(File.Exists(Path.Combine(run, "function-details.json")));
        Assert.Contains("console;verbosity=detailed", root.GetProperty("Execution").GetProperty("Commands").ToString(), StringComparison.Ordinal);
        if (invalidSolution)
        {
            Assert.Contains("XmlException", File.ReadAllText(Path.Combine(run, "logs/error.log")), StringComparison.Ordinal);
        }
        else
        {
            Assert.NotEqual(0, root.GetProperty("Execution").GetProperty("BuildExitCode").GetInt32());
            string sdk = root.GetProperty("Tools").GetProperty("DotnetSdk").GetString()!;
            Assert.Matches(@"^\d+\.\d+\.\d+", sdk);
            Assert.Contains(sdk, File.ReadAllText(Path.Combine(run, "logs/sdk.log")), StringComparison.Ordinal);
        }
    }

    private sealed class RepositoryFixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "CrapMeter-runtime-" + Guid.NewGuid().ToString("N"));

        public RepositoryFixture()
        {
            Directory.CreateDirectory(Root);
            Git("init");
            Write(".gitignore", "**/bin/\n**/obj/\n.crap/\n");
            Write("Server/src/P/F.cs", "class F { }");
            Git("add", ".");
            Git("-c", "user.name=Runtime Test", "-c", "user.email=runtime@example.invalid", "commit", "-m", "fixture");
        }

        public void Write(string relative, string content)
        {
            string path = Path.Combine(Root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        public void Git(params string[] arguments)
        {
            var info = new ProcessStartInfo("git") { WorkingDirectory = Root, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string argument in arguments) info.ArgumentList.Add(argument);
            using Process process = Process.Start(info)!;
            Task<string> output = process.StandardOutput.ReadToEndAsync();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            Assert.True(process.ExitCode == 0, error + output.GetAwaiter().GetResult());
        }

        public void Dispose()
        {
            // Only remove the fixture's uniquely allocated temporary directory.
            foreach (string path in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
                File.SetAttributes(path, FileAttributes.Normal);
            Directory.Delete(Root, recursive: true);
        }
    }
}
