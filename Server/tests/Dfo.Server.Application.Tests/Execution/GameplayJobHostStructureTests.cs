using System.Net;
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
/// rejected (TASK-04 R2-1). The reflection branch here only sees the assemblies this test project loads;
/// <c>Dfo.Server.Host.Tests</c>' <c>GameplayJobHostClosureTests</c> runs the same branch on the full production
/// closure (GameServer, GmWeb, Host, HttpApi included) and is the main criterion. The source branch stays as the
/// net outside every closure: <c>Dfo.Server.Utilities</c> has no source file today, and a new project is not
/// referenced by the host until someone wires it.
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
        var projects = ProjectFiles(source);
        output.WriteLine($"scanned project files: {projects.Count}");

        var violations = SourceViolations(files, projects);

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

    [Theory]
    [InlineData("escape")]
    [InlineData("string")]
    [InlineData("alias")]
    public void The_source_criterion_sees_through_an_escape_a_string_and_a_project_alias(string shape)
    {
        // F-76: the three ways round the source branch a reviewer found (TASK-19 round 1 L3).
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Dfo.Server.Abstractions/Execution/GameplayJob.cs"] = "public abstract class GameplayJobHost { }",
            ["Dfo.Server.Application/Execution/GameplayExecutor.cs"] = "public sealed class GameplayExecutor : GameplayJobHost { }",
        };
        var projects = new Dictionary<string, string>(StringComparer.Ordinal);
        string expected;
        switch (shape)
        {
            case "escape":
                files["Dfo.Server.GmWeb/Escaped.cs"] = "internal sealed class Escaped : GameplayJob\\u0048ost { }";
                expected = "Dfo.Server.GmWeb/Escaped.cs names GameplayJobHost";
                break;
            case "string":
                files["Dfo.Server.Host/Strung.cs"] = "class Strung { string url = \"http://example\"; } sealed class Rogue : GameplayJobHost { }";
                expected = "Dfo.Server.Host/Strung.cs names GameplayJobHost";
                break;
            default:
                projects["Dfo.Server.HttpApi/Dfo.Server.HttpApi.csproj"] =
                    "<Project><ItemGroup><Using Include=\"Dfo.Server.Abstractions.Execution.GameplayJobHost\" Alias=\"Host\" /></ItemGroup></Project>";
                files["Dfo.Server.HttpApi/Aliased.cs"] = "internal sealed class Aliased : Host { }";
                expected = "Dfo.Server.HttpApi/Dfo.Server.HttpApi.csproj names GameplayJobHost in a <Using> item";
                break;
        }

        Assert.Equal([expected], SourceViolations(files, projects));
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

    private static IReadOnlyList<string> HostViolations(IEnumerable<Type> types) => GameplayJobHostClosure.HostViolations(types);

    private static IReadOnlyList<string> SourceViolations(IReadOnlyDictionary<string, string> files,
        IReadOnlyDictionary<string, string>? projects = null)
    {
        var naming = files.Where(file => HostIdentifier.IsMatch(Unescaped(WithoutComments(file.Value)))).Select(file => file.Key).ToHashSet();
        return naming.Where(path => !HostSourceFiles.Contains(path))
            .Select(path => $"{path} names {nameof(GameplayJobHost)}")
            // A listed file that no longer names the host is a stale entry.
            .Concat(HostSourceFiles.Where(path => !naming.Contains(path)).Select(path => $"{path} is listed but no longer names it"))
            // No project may alias it: a global using in a project file is invisible to the .cs scan.
            .Concat((projects ?? new Dictionary<string, string>())
                .Where(project => UsingItem.Matches(project.Value).Any(item => HostIdentifier.IsMatch(WebUtility.HtmlDecode(item.Value))))
                .Select(project => $"{project.Key} names {nameof(GameplayJobHost)} in a <Using> item"))
            .ToList();
    }

    private static readonly Regex UsingItem = new(@"<Using\b[^>]*>", RegexOptions.Compiled);

    /// <summary>Identifiers may spell a letter as <c>\uXXXX</c> or <c>\UXXXXXXXX</c>; the compiler reads it as the letter.</summary>
    private static string Unescaped(string code) =>
        Regex.Replace(code, @"\\u([0-9A-Fa-f]{4})|\\U([0-9A-Fa-f]{8})", match =>
        {
            if (match.Groups[1].Success) return ((char)Convert.ToInt32(match.Groups[1].Value, 16)).ToString();
            var value = Convert.ToInt32(match.Groups[2].Value, 16);
            return value is >= 0 and <= 0x10FFFF and not (>= 0xD800 and <= 0xDFFF) ? char.ConvertFromUtf32(value) : match.Value;
        });

    private static IReadOnlyList<Assembly> ProductionAssemblies() =>
        GameplayJobHostClosure.ProductionAssemblies(typeof(GameplayExecutor).Assembly);

    private static IEnumerable<Type> LoadableTypes(Assembly assembly) => GameplayJobHostClosure.LoadableTypes(assembly);

    /// <summary>The project files that can declare a global using: every csproj under <c>Server/src</c> and the shared props and targets.</summary>
    private static Dictionary<string, string> ProjectFiles(string source)
    {
        var server = Path.GetDirectoryName(source)!;
        var projects = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(source, "*.csproj", SearchOption.AllDirectories))
            projects[Path.GetRelativePath(source, file).Replace('\\', '/')] = File.ReadAllText(file);
        foreach (var name in new[] { "Directory.Build.props", "Directory.Build.targets" })
            if (File.Exists(Path.Combine(server, name))) projects["../" + name] = File.ReadAllText(Path.Combine(server, name));
        return projects;
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

    /// <summary>
    /// The text with its comments blanked and everything else kept, read by a minimal lexer: a <c>//</c> or
    /// <c>/*</c> inside a string or character literal is not a comment.
    /// </summary>
    /// <remarks>
    /// Recognised: regular, verbatim (<c>@"…"</c>, with <c>""</c>), interpolated (<c>$"…"</c>, <c>$@"…"</c>, holes
    /// read as code) and character literals; a raw string literal (<c>"""…"""</c>) is kept whole, so a comment in
    /// a raw interpolated hole is kept too (the boundary errs towards finding the name, never towards losing it).
    /// </remarks>
    private static string WithoutComments(string text)
    {
        var output = new System.Text.StringBuilder(text.Length);
        var at = 0;
        Code(text, ref at, output, inHole: false);
        return output.ToString();
    }

    private static void Code(string text, ref int at, System.Text.StringBuilder output, bool inHole)
    {
        var depth = 0;
        while (at < text.Length)
        {
            var c = text[at];
            if (c == '/' && Next(text, at) == '/')
            {
                while (at < text.Length && text[at] != '\n') at++;
                output.Append(' ');
            }
            else if (c == '/' && Next(text, at) == '*')
            {
                var end = text.IndexOf("*/", at + 2, StringComparison.Ordinal);
                at = end < 0 ? text.Length : end + 2;
                output.Append(' ');
            }
            else if (c == '"' || c == '\'' || ((c == '$' || c == '@') && StringStart(text, at) is not null))
            {
                Literal(text, ref at, output);
            }
            else
            {
                if (inHole && c == '{') depth++;
                if (inHole && c == '}' && depth-- == 0) return;
                output.Append(c);
                at++;
            }
        }
    }

    /// <summary>The index of the opening quote when a <c>$</c>/<c>@</c> prefix at <paramref name="at"/> starts a string.</summary>
    private static int? StringStart(string text, int at)
    {
        var quote = at;
        while (quote < text.Length && text[quote] is '$' or '@') quote++;
        return quote < text.Length && text[quote] == '"' && quote - at <= 3 ? quote : null;
    }

    private static void Literal(string text, ref int at, System.Text.StringBuilder output)
    {
        var start = at;
        if (text[at] == '\'')
        {
            for (at++; at < text.Length && text[at] != '\''; at++)
                if (text[at] == '\\') at++;
            at = Math.Min(at + 1, text.Length);
            output.Append(text, start, at - start);
            return;
        }
        var quote = StringStart(text, at) ?? at;
        var prefix = text[at..quote];
        var interpolated = prefix.Contains('$');
        var verbatim = prefix.Contains('@');
        var quotes = 0;
        while (quote + quotes < text.Length && text[quote + quotes] == '"') quotes++;
        if (quotes >= 3)
        {
            // Raw string literal: kept whole up to the same run of quotes.
            var end = text.IndexOf(new string('"', quotes), quote + quotes, StringComparison.Ordinal);
            at = end < 0 ? text.Length : end + quotes;
            output.Append(text, start, at - start);
            return;
        }
        output.Append(text, start, quote + 1 - start);
        at = quote + 1;
        while (at < text.Length)
        {
            var c = text[at];
            if (!verbatim && c == '\\')
            {
                output.Append(text, at, Math.Min(2, text.Length - at));
                at += 2;
            }
            else if (c == '"' && verbatim && Next(text, at) == '"')
            {
                output.Append("\"\"");
                at += 2;
            }
            else if (c == '"')
            {
                output.Append(c);
                at++;
                return;
            }
            else if (interpolated && c == '{' && Next(text, at) == '{')
            {
                output.Append("{{");
                at += 2;
            }
            else if (interpolated && c == '{')
            {
                output.Append(c);
                at++;
                Code(text, ref at, output, inHole: true);
                if (at < text.Length)
                {
                    output.Append('}');
                    at++;
                }
            }
            else
            {
                output.Append(c);
                at++;
            }
        }
    }

    private static char Next(string text, int at) => at + 1 < text.Length ? text[at + 1] : '\0';

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
