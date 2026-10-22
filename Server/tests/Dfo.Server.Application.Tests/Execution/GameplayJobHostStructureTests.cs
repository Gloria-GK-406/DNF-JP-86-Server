using System.Reflection;
using System.Text.RegularExpressions;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Application.Execution;
using Xunit.Abstractions;

namespace Dfo.Server.Application.Tests.Execution;

/// <summary>
/// RQ-072: in production <see cref="GameplayJobHost"/> has one subclass, the sealed
/// <see cref="GameplayExecutor"/>, and a host refuses a job token it did not issue.
/// </summary>
/// <remarks>
/// Access modifiers cannot pin the first half: the executor lives in another assembly, so the base
/// constructor has to be <c>protected</c>, and opening Abstractions' internals to Application was
/// rejected (TASK-04 R2-1). The reflection branch only sees the assemblies this test project loads;
/// the source branch covers the projects outside that closure (GameServer, GmWeb, Host, HttpApi, Utilities).
/// </remarks>
public sealed class GameplayJobHostStructureTests(ITestOutputHelper output)
{
    private const string OwnershipRefusal = "job token of another executor";

    private static readonly IReadOnlySet<string> HostSourceFiles = new HashSet<string>(StringComparer.Ordinal)
    {
        "Dfo.Server.Abstractions/Execution/GameplayJob.cs",
        "Dfo.Server.Application/Execution/GameplayExecutor.cs",
    };

    private static readonly Regex HostIdentifier = new(@"\bGameplayJobHost\b", RegexOptions.Compiled);

    [Fact]
    public void Only_the_sealed_gameplay_executor_derives_from_the_job_host_in_the_loaded_production_assemblies()
    {
        var assemblies = ProductionAssemblies();
        var types = assemblies.SelectMany(LoadableTypes).ToArray();
        output.WriteLine("scanned assemblies: " + string.Join(", ", assemblies.Select(x => x.GetName().Name)));
        output.WriteLine("job hosts found: " + string.Join(", ",
            types.Where(x => x.IsSubclassOf(typeof(GameplayJobHost))).Select(x => x.FullName)));

        var violations = HostViolations(types);

        Assert.Contains(typeof(GameplayExecutor).Assembly, assemblies);
        Assert.Contains(typeof(GameplayJobHost).Assembly, assemblies);
        Assert.True(violations.Count == 0, string.Join("\n", violations));
    }

    [Fact]
    public void Only_the_two_host_files_name_the_job_host_in_the_production_sources()
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
        output.WriteLine($"scanned source files: {files.Count}");
        foreach (var (path, text) in files)
            foreach (var line in text.Split('\n').Select((value, index) => (value, index)))
                if (HostIdentifier.IsMatch(line.value))
                    output.WriteLine($"{path}:{line.index + 1}: {line.value.Trim()}");

        var violations = SourceViolations(files);

        Assert.True(violations.Count == 0, string.Join("\n", violations));
    }

    [Fact]
    public void A_second_job_host_is_named_by_the_reflection_criterion()
    {
        var violations = HostViolations([typeof(GameplayExecutor), typeof(RogueHost), typeof(GameplayQueue)]);

        var violation = Assert.Single(violations);
        Assert.Contains(nameof(RogueHost), violation, StringComparison.Ordinal);
    }

    [Fact]
    public void A_job_host_declared_in_a_third_source_file_is_named_by_the_source_criterion()
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Dfo.Server.Abstractions/Execution/GameplayJob.cs"] = "public abstract class GameplayJobHost { }",
            ["Dfo.Server.Application/Execution/GameplayExecutor.cs"] = "public sealed class GameplayExecutor : GameplayJobHost { }",
            ["Dfo.Server.GmWeb/Somewhere/Shortcut.cs"] = "internal sealed class Shortcut : GameplayJobHost { }",
            ["Dfo.Server.Host/Commented.cs"] = "// GameplayJobHost in a comment is not a host\nclass Commented { }",
        };

        var violation = Assert.Single(SourceViolations(files));
        Assert.Contains("Dfo.Server.GmWeb/Somewhere/Shortcut.cs", violation, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_host_refuses_to_exit_a_job_of_another_executor_and_the_job_runs_on()
    {
        var (executor, _) = ExecutionTest.Executor();
        await using var _executor = executor;
        var rogue = new RogueHost();

        var seen = await executor.RunAsync(ExecutionTest.Work(_ =>
        {
            var refused = Record.Exception(() => rogue.Exit(OwnToken()));
            return ValueTask.FromResult((Refused: refused, StillOnExecutor: GameplayJob.IsOnExecutor, Value: 42));
        }), CancellationToken.None).AsTask().WaitAsync(ExecutionTest.Patience);

        AssertOwnershipRefusal(seen.Refused, "ExitJob");
        Assert.True(seen.StillOnExecutor);
        Assert.Equal(42, seen.Value);
    }

    [Fact]
    public async Task A_host_refuses_to_fault_a_job_of_another_executor_and_the_job_does_not_fail()
    {
        var (executor, _) = ExecutionTest.Executor();
        await using var _executor = executor;
        var rogue = new RogueHost();

        var refused = await executor.RunAsync(ExecutionTest.Work(_ =>
            ValueTask.FromResult(Record.Exception(() => rogue.Record(OwnToken(), new ForeignFault())))),
            CancellationToken.None).AsTask().WaitAsync(ExecutionTest.Patience);

        AssertOwnershipRefusal(refused, "Fault");
    }

    [Fact]
    public async Task A_host_refuses_to_read_the_program_error_of_a_job_of_another_executor()
    {
        var (executor, _) = ExecutionTest.Executor();
        await using var _executor = executor;
        var rogue = new RogueHost();

        var refused = await executor.RunAsync(ExecutionTest.Work(_ =>
            ValueTask.FromResult(Record.Exception(() => rogue.ErrorOf(OwnToken())))),
            CancellationToken.None).AsTask().WaitAsync(ExecutionTest.Patience);

        AssertOwnershipRefusal(refused, "ProgramErrorOf");
    }

    [Fact]
    public async Task A_host_refuses_to_read_the_chain_of_a_job_of_another_executor()
    {
        var (executor, _) = ExecutionTest.Executor();
        await using var _executor = executor;
        var rogue = new RogueHost();

        var refused = await executor.RunAsync(ExecutionTest.Work(_ =>
            ValueTask.FromResult(Record.Exception(() => rogue.ChainFor(OwnToken())))),
            CancellationToken.None).AsTask().WaitAsync(ExecutionTest.Patience);

        AssertOwnershipRefusal(refused, "ChainOf");
    }

    [Fact]
    public void A_host_uses_the_tokens_it_issued_itself()
    {
        var rogue = new RogueHost();
        var chain = ExecutionTest.Root().ForCommand("Rogue");
        var error = new ForeignFault();

        var token = rogue.Enter(chain);
        try
        {
            Assert.True(GameplayJob.IsOnExecutor);
            Assert.Equal(chain.Id, rogue.ChainFor(token).Id);
            Assert.Null(rogue.ErrorOf(token));
            Assert.Same(error, rogue.Record(token, error));
            Assert.Same(error, rogue.ErrorOf(token));
        }
        finally
        {
            rogue.Exit(token);
        }

        Assert.False(GameplayJob.IsOnExecutor);
    }

    /// <summary>
    /// The depth refusal sticks to whatever job is running on the flow, including a job of another
    /// executor (the shape before the ownership checks, kept by <c>FaultRunningJob</c>).
    /// </summary>
    [Fact]
    public async Task A_swallowed_depth_refusal_from_another_executor_still_fails_the_running_job()
    {
        var (first, _) = ExecutionTest.Executor();
        await using var _first = first;
        var (second, _) = ExecutionTest.Executor();
        await using var _second = second;
        var tooDeep = ExecutionTest.Root();
        for (var i = 0; i <= GameplayCallDepth.Limit; i++) tooDeep = tooDeep.ForCommand($"C{i}").Child(GameplayCallSource.Event);

        await Assert.ThrowsAsync<GameplayDepthExceededException>(async () =>
            await first.RunAsync(ExecutionTest.Work(async ct =>
            {
                try
                {
                    await second.RunAsync(ExecutionTest.Work(_ => ValueTask.CompletedTask, "TooDeep", tooDeep), ct);
                }
                catch (GameplayDepthExceededException)
                {
                }
            }), CancellationToken.None).AsTask().WaitAsync(ExecutionTest.Patience));
    }

    private static IReadOnlyList<string> HostViolations(IEnumerable<Type> types)
    {
        var violations = types
            .Where(type => type.IsSubclassOf(typeof(GameplayJobHost)) && type != typeof(GameplayExecutor))
            .Select(type => $"{type.FullName} derives from {nameof(GameplayJobHost)}")
            .ToList();
        // An unsealed executor would make its own subclasses hosts.
        if (!typeof(GameplayExecutor).IsSealed) violations.Add($"{nameof(GameplayExecutor)} is not sealed");
        return violations;
    }

    private static IReadOnlyList<string> SourceViolations(IReadOnlyDictionary<string, string> files)
    {
        var naming = files.Where(file => HostIdentifier.IsMatch(WithoutComments(file.Value))).Select(file => file.Key).ToHashSet();
        return naming.Where(path => !HostSourceFiles.Contains(path))
            .Select(path => $"{path} names {nameof(GameplayJobHost)}")
            // A listed file that no longer names the host is a stale entry.
            .Concat(HostSourceFiles.Where(path => !naming.Contains(path)).Select(path => $"{path} is listed but no longer names it"))
            .ToList();
    }

    private static IReadOnlyList<Assembly> ProductionAssemblies()
    {
        var seeds = new List<Assembly> { typeof(GameplayExecutor).Assembly };
        foreach (var file in Directory.EnumerateFiles(AppContext.BaseDirectory, "Dfo.*.dll"))
        {
            var name = AssemblyName.GetAssemblyName(file);
            if (IsProduction(name)) seeds.Add(Assembly.Load(name));
        }

        var found = new Dictionary<string, Assembly>(StringComparer.Ordinal);
        var pending = new Queue<Assembly>(seeds);
        while (pending.TryDequeue(out var assembly))
        {
            if (!found.TryAdd(assembly.GetName().Name!, assembly)) continue;
            foreach (var reference in assembly.GetReferencedAssemblies().Where(IsProduction))
                pending.Enqueue(Assembly.Load(reference));
        }
        return found.Values.OrderBy(x => x.GetName().Name, StringComparer.Ordinal).ToArray();

        static bool IsProduction(AssemblyName name) =>
            name.Name is { } simple
            && (simple.StartsWith("Dfo.Server.", StringComparison.Ordinal) || simple == "Dfo.Pvf")
            && !simple.EndsWith(".Tests", StringComparison.Ordinal);
    }

    private static IEnumerable<Type> LoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException partial)
        {
            return partial.Types.OfType<Type>();
        }
    }

    /// <summary>The running job's own token; it belongs to the executor running the job, not to the rogue host.</summary>
    private static GameplayJobToken OwnToken() =>
        GameplayJob.Running.Value ?? throw new InvalidOperationException("Not inside a job.");

    private static void AssertOwnershipRefusal(Exception? refused, string operation)
    {
        var error = Assert.IsType<InvalidOperationException>(refused);
        Assert.Contains(OwnershipRefusal, error.Message, StringComparison.Ordinal);
        Assert.StartsWith(operation, error.Message, StringComparison.Ordinal);
    }

    private static string WithoutComments(string text) =>
        Regex.Replace(Regex.Replace(text, @"/\*.*?\*/", " ", RegexOptions.Singleline), @"//[^\n]*", " ");

    private static string Repository()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (Directory.Exists(Path.Combine(directory.FullName, "Server", "src"))) return directory.FullName;
        throw new DirectoryNotFoundException("The job host structure test requires the repository checkout.");
    }

    private sealed class ForeignFault : Exception;

    /// <summary>A second host; it lives in the test assembly, which the reflection branch does not scan.</summary>
    private sealed class RogueHost : GameplayJobHost
    {
        public GameplayJobToken Enter(GameplayCallChain chain) => EnterJob(chain);

        public void Exit(GameplayJobToken token) => ExitJob(token);

        public Exception Record(GameplayJobToken token, Exception error) => Fault(token, error);

        public Exception? ErrorOf(GameplayJobToken token) => ProgramErrorOf(token);

        public GameplayCallChain ChainFor(GameplayJobToken token) => ChainOf(token);
    }
}
