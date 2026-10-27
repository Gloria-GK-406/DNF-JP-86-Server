using System.Reflection;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Application.Execution;

namespace Dfo.Server.Application.Tests.Execution;

/// <summary>
/// RQ-072's reflection branch, shared by <c>GameplayJobHostStructureTests</c> (the closure this project loads)
/// and <c>Dfo.Server.Host.Tests</c>' <c>GameplayJobHostClosureTests</c> (the full production closure), which
/// compiles this file in by link.
/// </summary>
internal static class GameplayJobHostClosure
{
    /// <summary>The production assemblies reachable from <paramref name="seeds"/> and the <c>Dfo.*</c> files next to the test.</summary>
    public static IReadOnlyList<Assembly> ProductionAssemblies(params Assembly[] seeds)
    {
        var start = new List<Assembly>(seeds);
        foreach (var file in Directory.EnumerateFiles(AppContext.BaseDirectory, "Dfo.*.dll"))
        {
            var name = AssemblyName.GetAssemblyName(file);
            if (IsProduction(name)) start.Add(Assembly.Load(name));
        }

        var found = new Dictionary<string, Assembly>(StringComparer.Ordinal);
        var pending = new Queue<Assembly>(start);
        while (pending.TryDequeue(out var assembly))
        {
            if (!found.TryAdd(assembly.GetName().Name!, assembly)) continue;
            foreach (var reference in assembly.GetReferencedAssemblies().Where(IsProduction))
                pending.Enqueue(Assembly.Load(reference));
        }
        return found.Values.OrderBy(x => x.GetName().Name, StringComparer.Ordinal).ToArray();
    }

    public static bool IsProduction(AssemblyName name) =>
        name.Name is { } simple
        && (simple.StartsWith("Dfo.Server.", StringComparison.Ordinal) || simple == "Dfo.Pvf")
        && !simple.EndsWith(".Tests", StringComparison.Ordinal);

    public static IEnumerable<Type> LoadableTypes(Assembly assembly)
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

    public static IReadOnlyList<string> HostViolations(IEnumerable<Type> types)
    {
        var violations = types
            .Where(type => type.IsSubclassOf(typeof(GameplayJobHost)) && type != typeof(GameplayExecutor))
            .Select(type => $"{type.FullName} derives from {nameof(GameplayJobHost)}")
            .ToList();
        // An unsealed executor would make its own subclasses hosts.
        if (!typeof(GameplayExecutor).IsSealed) violations.Add($"{nameof(GameplayExecutor)} is not sealed");
        return violations;
    }
}
