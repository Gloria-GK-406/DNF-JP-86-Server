using System.Reflection;
using Dfo.Server.Application.Tests.Execution;
using Xunit.Abstractions;

namespace Dfo.Server.Host.Tests;

/// <summary>
/// RQ-072 / F-76: the reflection branch of the job-host criterion on the full production closure. The host's
/// closure includes GameServer, GmWeb, HttpApi and Host, which the Application test project does not load and
/// which only the source branch covered before.
/// </summary>
/// <remarks>
/// <c>Dfo.Server.Utilities</c> has no source file and no project references it, so it is outside this closure;
/// the source branch in <c>GameplayJobHostStructureTests</c> still covers it and any project not yet wired in.
/// </remarks>
public sealed class GameplayJobHostClosureTests(ITestOutputHelper output)
{
    private static readonly string[] OnlySourceCoveredBefore =
        ["Dfo.Server.GameServer", "Dfo.Server.GmWeb", "Dfo.Server.Host", "Dfo.Server.HttpApi"];

    [Fact]
    public void Only_the_sealed_gameplay_executor_derives_from_the_job_host_in_the_full_production_closure()
    {
        var assemblies = GameplayJobHostClosure.ProductionAssemblies(Assembly.Load("Dfo.Server.Host"));
        var names = assemblies.Select(assembly => assembly.GetName().Name!).ToHashSet(StringComparer.Ordinal);
        output.WriteLine("scanned assemblies: " + string.Join(", ", names.Order(StringComparer.Ordinal)));

        var violations = GameplayJobHostClosure.HostViolations(assemblies.SelectMany(GameplayJobHostClosure.LoadableTypes));

        Assert.All(OnlySourceCoveredBefore, name => Assert.Contains(name, names));
        Assert.True(violations.Count == 0, string.Join("\n", violations));
    }
}
