using System.Reflection;
using Dfo.Server.Infrastructure;
using Dfo.Server.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Support;

/// <summary>The constructor graph the container would build, read from real registrations.</summary>
/// <remarks>
/// An interface expands to every type registered for it; a factory registration is opened by resolving it and taking
/// the instance's type, so a factory cannot hide an edge. <c>Func</c>, <c>Lazy</c> and <c>IEnumerable</c> expand to what
/// they would produce, so a deferral cannot hide one either. Constructors are only walked for this server's own types.
/// </remarks>
internal sealed class ServiceGraph(IServiceCollection services, IServiceProvider provider)
{
    private readonly Dictionary<Type, Type[]> edges = [];

    /// <remarks>A record's copy constructor takes its own type; that is not a dependency.</remarks>
    internal static IEnumerable<Type> Parameters(Type type) =>
        type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .SelectMany(x => x.GetParameters()).Select(x => x.ParameterType).Where(x => x != type).Distinct();

    /// <summary>Constructor parameters that put off finding a dependency instead of taking it.</summary>
    /// <remarks>
    /// The provider, a <c>Lazy</c>, or a <c>Func</c> that produces an interface. A <c>Func</c> producing a value is a
    /// callback the owner was handed (an authority check, a composer, a token resolver), not a dependency put off.
    /// </remarks>
    internal static IReadOnlyList<string> Deferrals(IEnumerable<Type> types) =>
        types.SelectMany(type => Parameters(type)
            .Where(x => x == typeof(IServiceProvider) || x.IsGenericType && x.GetGenericTypeDefinition() is var g && (g == typeof(Lazy<>)
                || g.FullName!.StartsWith("System.Func`", StringComparison.Ordinal) && x.GetGenericArguments()[^1].IsInterface))
            .Select(x => $"{type.Name} defers {x.Name}"))
            .ToArray();

    internal IReadOnlyList<Type> Next(Type type)
    {
        if (edges.TryGetValue(type, out var known)) return known;
        return edges[type] = Expand(type).Distinct().ToArray();
    }

    private IEnumerable<Type> Expand(Type type)
    {
        if (type.IsGenericType && (type.GetGenericTypeDefinition() == typeof(Lazy<>) || type.GetGenericTypeDefinition() == typeof(IEnumerable<>)
            || type.GetGenericTypeDefinition().FullName!.StartsWith("System.Func`", StringComparison.Ordinal)))
            return [type.GetGenericArguments()[^1]];
        var registered = services.Where(x => !x.IsKeyedService && x.ServiceType == type).ToArray();
        var built = registered.Select(x => x.ImplementationType ?? x.ImplementationInstance?.GetType()).OfType<Type>().Where(x => x != type);
        if (registered.Any(x => x.ImplementationFactory is not null))
            built = built.Concat(provider.GetServices(type).OfType<object>().Select(x => x.GetType()).Where(x => x != type));
        var parameters = !type.IsInterface && !type.IsAbstract && type.Assembly.GetName().Name!.StartsWith("Dfo.", StringComparison.Ordinal)
            ? Parameters(type) : [];
        return built.Concat(parameters).ToArray();
    }

    internal HashSet<Type> Reach(Type root)
    {
        var seen = new HashSet<Type>();
        var queue = new Queue<Type>([root]);
        while (queue.TryDequeue(out var type))
        {
            if (!seen.Add(type)) continue;
            foreach (var next in Next(type)) queue.Enqueue(next);
        }
        return seen;
    }

    internal IReadOnlyList<string> ReachViolations(Type root, IEnumerable<Type> forbidden)
    {
        var reached = Reach(root);
        return forbidden.Where(reached.Contains).Select(x => $"{root.Name} reaches {x.Name}").ToArray();
    }

    /// <summary>Every loop among the registered services, one line per strongly connected set of this server's classes.</summary>
    internal IReadOnlyList<string> Cycles()
    {
        var roots = services.Where(x => !x.IsKeyedService && !x.ServiceType.ContainsGenericParameters).Select(x => x.ServiceType).Distinct().ToArray();
        var index = new Dictionary<Type, int>();
        var low = new Dictionary<Type, int>();
        var stack = new Stack<Type>();
        var onStack = new HashSet<Type>();
        var cycles = new List<string>();
        foreach (var root in roots)
            if (!index.ContainsKey(root)) Connect(root);
        return cycles;

        void Connect(Type type)
        {
            index[type] = low[type] = index.Count;
            stack.Push(type);
            onStack.Add(type);
            foreach (var next in Next(type))
            {
                if (!index.ContainsKey(next)) { Connect(next); low[type] = Math.Min(low[type], low[next]); }
                else if (onStack.Contains(next)) low[type] = Math.Min(low[type], index[next]);
            }
            if (low[type] != index[type]) return;
            var members = new List<Type>();
            Type member;
            do { member = stack.Pop(); onStack.Remove(member); members.Add(member); } while (member != type);
            if (members.Count > 1 || Next(type).Contains(type))
                cycles.Add(string.Join(", ", members.Where(x => x.IsClass).Select(x => x.Name).Order(StringComparer.Ordinal)));
        }
    }

    /// <summary>The modules production composes, over a throwaway database.</summary>
    internal sealed class Production : IDisposable
    {
        private readonly string path = Path.Combine(Path.GetTempPath(), $"service-graph-{Guid.NewGuid():N}.db");
        public IServiceCollection Services { get; } = new ServiceCollection().AddLogging();
        public ServiceProvider Provider { get; }
        public ServiceGraph Graph { get; }

        public Production()
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Database:Path"] = path }).Build();
            new PersistenceModule().ConfigureServices(Services, configuration);
            new InfrastructureModule().ConfigureServices(Services, configuration);
            new ApplicationModule().ConfigureServices(Services, configuration);
            Provider = Services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = false, ValidateScopes = false });
            Graph = new(Services, Provider);
        }

        public void Dispose()
        {
            Provider.Dispose();
            SqliteTestPools.ClearOwned(path);
            File.Delete(path);
        }
    }
}
