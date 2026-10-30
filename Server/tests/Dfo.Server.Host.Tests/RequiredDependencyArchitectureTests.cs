using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Host.Tests;

/// <summary>
/// A service either has its dependency or the server does not start; nothing degrades because a registration is missing.
/// </summary>
/// <remarks>
/// An optional constructor dependency lets the container build without the service behind it: the container passes
/// <c>null</c>, and the class then skips its part, or throws the first time a rare path reaches it. So no production
/// constructor takes a service it may go without, a registered type has one public constructor for the container to
/// choose, and the composition the executable runs is built with validation, which then refuses a missing registration.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class RequiredDependencyArchitectureTests
{
    private const BindingFlags Constructors = BindingFlags.Public | BindingFlags.Instance;

    /// <summary>Assemblies whose interfaces are services; the data project's interfaces describe records and messages.</summary>
    private static readonly HashSet<string> ServiceAssemblies = new(StringComparer.Ordinal)
    {
        "Dfo.Server.Abstractions", "Dfo.Server.Application", "Dfo.Server.Infrastructure", "Dfo.Server.Persistence",
        "Dfo.Server.Networking", "Dfo.Server.Protocol", "Dfo.Server.HttpApi", "Dfo.Server.GmWeb", "Dfo.Server.GameServer",
        "Dfo.Server.Host",
    };

    /// <summary>
    /// Outbox and notification adapters whose second constructor adds what only the networking component registers. The
    /// executable always composes networking, so the container takes the wider constructor; an Application-only
    /// composition takes the narrower one, in which every connection is unknown and every send is dropped.
    /// </summary>
    private static readonly Dictionary<string, Type[]> NetworkingConstructors = new(StringComparer.Ordinal)
    {
        ["Dfo.Server.Application.Execution.GameplayDepthGuard"] = [typeof(Dfo.Server.Abstractions.Networking.INetworkConnectionContexts)],
        ["Dfo.Server.Application.Execution.Outbox.OutboxConnectionCloser"] = [typeof(Dfo.Server.Abstractions.Networking.INetworkConnectionContexts)],
        ["Dfo.Server.Application.Execution.Outbox.OutboxNetworkMessageSender"] =
            [typeof(Dfo.Server.Abstractions.Networking.INetworkConnectionContexts), typeof(Dfo.Server.Abstractions.Execution.IOutboundMessageEncoder)],
        ["Dfo.Server.Application.Notifications.NotificationBus"] = [typeof(Dfo.Server.Abstractions.Networking.INetworkConnectionContexts)],
    };

    [Fact]
    public void The_production_composition_builds_with_validation()
    {
        using var composed = Composed.Build();
        Assert.NotEmpty(composed.Services);
    }

    [Fact]
    public void No_production_constructor_takes_an_optional_service()
    {
        using var composed = Composed.Build();
        var violations = Violations(composed.Built(), composed.Services);
        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Each_networking_constructor_pair_differs_only_by_what_networking_registers()
    {
        var types = typeof(Dfo.Server.Application.ApplicationModule).Assembly.GetTypes().ToDictionary(x => x.FullName!);
        Assert.All(NetworkingConstructors, pair =>
        {
            var constructors = types[pair.Key].GetConstructors(Constructors).OrderBy(x => x.GetParameters().Length).ToArray();
            Assert.Equal(2, constructors.Length);
            var narrow = constructors[0].GetParameters().Select(x => x.ParameterType);
            var extra = constructors[1].GetParameters().Select(x => x.ParameterType).Except(narrow);
            Assert.Equal(pair.Value.OrderBy(x => x.Name), extra.OrderBy(x => x.Name));
        });
    }

    [Fact]
    public void The_scan_reaches_types_the_container_builds_from_a_factory_and_across_every_component()
    {
        using var composed = Composed.Build();
        var built = composed.Built().Select(x => x.Assembly.GetName().Name).ToHashSet();
        Assert.All(new[] { "Dfo.Server.Application", "Dfo.Server.Persistence", "Dfo.Server.Infrastructure", "Dfo.Server.Protocol",
            "Dfo.Server.Networking", "Dfo.Server.HttpApi", "Dfo.Server.GmWeb", "Dfo.Server.GameServer" }, name => Assert.Contains(name, built));
        // Registered only through a factory: found by building it, not by reading the descriptor.
        Assert.Contains(composed.Built(), x => x.Name == "SqliteItemDomainStore");
    }

    [Fact]
    public void The_rule_refuses_nullable_and_defaulted_services_and_a_second_constructor_but_not_optional_data()
    {
        var services = new ServiceCollection();
        services.AddSingleton<Registered>();
        services.AddSingleton<TwoConstructors>();
        Assert.Equal(
        [
            "NullableService: service parameter 'logger' (ILogger) is nullable",
            "DefaultedService: service parameter 'registered' (Registered) has a default value",
            "NullableAndDefaulted: service parameter 'logger' (ILogger`1) is nullable and has a default value",
            "TwoConstructors: registered implementation has 2 public constructors",
        ], Violations([typeof(NullableService), typeof(DefaultedService), typeof(NullableAndDefaulted), typeof(TwoConstructors),
            typeof(OptionalData), typeof(Required)], services));
    }

    [Fact]
    public void Validation_refuses_a_required_service_that_is_not_registered()
    {
        var services = new ServiceCollection().AddSingleton<Required>();
        var error = Assert.Throws<AggregateException>(() =>
            services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true }).Dispose());
        Assert.Contains(nameof(Registered), error.Message, StringComparison.Ordinal);
    }

    private static IReadOnlyList<string> Violations(IEnumerable<Type> types, IServiceCollection services)
    {
        var registered = services.Select(x => x.ServiceType).ToHashSet();
        var implementations = services.Select(x => x.ImplementationType).OfType<Type>().ToHashSet();
        var nullability = new NullabilityInfoContext();
        var violations = new List<string>();
        foreach (var type in types.Where(x => x.IsClass && !x.IsAbstract && !IsRecord(x)))
        {
            var constructors = type.GetConstructors(Constructors);
            foreach (var parameter in constructors.SelectMany(x => x.GetParameters()))
            {
                var parameterType = parameter.ParameterType;
                if (!IsService(parameterType, registered)) continue;
                var nullable = nullability.Create(parameter).WriteState == NullabilityState.Nullable;
                var defaulted = parameter.HasDefaultValue;
                if (!nullable && !defaulted) continue;
                var shape = (nullable, defaulted) switch
                {
                    (true, true) => "is nullable and has a default value",
                    (true, false) => "is nullable",
                    _ => "has a default value",
                };
                violations.Add($"{Name(type)}: service parameter '{parameter.Name}' ({parameterType.Name}) {shape}");
            }
            if (implementations.Contains(type) && constructors.Length > 1 && !NetworkingConstructors.ContainsKey(type.FullName!))
                violations.Add($"{Name(type)}: registered implementation has {constructors.Length} public constructors");
        }
        return violations.Distinct().ToArray();
    }

    private static string Name(Type type) =>
        type.DeclaringType == typeof(RequiredDependencyArchitectureTests) ? type.Name : type.FullName!;

    /// <remarks>A record is a value the caller fills in; an absent member is data, not a missing registration.</remarks>
    private static bool IsRecord(Type type) => type.GetMethod("<Clone>$", BindingFlags.Public | BindingFlags.Instance) is not null;

    private static bool IsService(Type type, IReadOnlySet<Type> registered)
    {
        if (registered.Contains(type)) return true;
        if (type == typeof(ILogger) || type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ILogger<>)) return true;
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>)) return false;
        return (type.IsInterface || type.IsAbstract) && ServiceAssemblies.Contains(type.Assembly.GetName().Name!);
    }

    /// <summary>The executable's composition, every component switched on, over a throwaway database; built, not started.</summary>
    private sealed class Composed : IDisposable
    {
        private readonly string directory;
        private readonly IHost host;

        private Composed(string directory, IHost host, IServiceCollection services) =>
            (this.directory, this.host, Services) = (directory, host, services);

        public IServiceCollection Services { get; }

        /// <summary>
        /// This server's classes the container constructs: registered implementation types, and the instances factory
        /// registrations produce. Objects a service assembles for itself are not here; their owner holds the dependency.
        /// </summary>
        public IReadOnlyList<Type> Built()
        {
            using var scope = host.Services.CreateScope();
            var produced = Services
                .Where(x => !x.IsKeyedService && x.ImplementationFactory is not null && !x.ServiceType.ContainsGenericParameters)
                .Select(x => x.ServiceType).Distinct()
                .SelectMany(x => scope.ServiceProvider.GetServices(x)).OfType<object>().Select(x => x.GetType());
            return Services.Where(x => !x.IsKeyedService).Select(x => x.ImplementationType).OfType<Type>().Concat(produced)
                .Where(x => x.Assembly.GetName().Name!.StartsWith("Dfo.", StringComparison.Ordinal) && !x.ContainsGenericParameters)
                .Distinct().ToArray();
        }

        public static Composed Build()
        {
            var directory = Path.Combine(Path.GetTempPath(), $"required-dependencies-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            // The shipped appsettings, as the executable loads them, with both HTTP features on so their services are in
            // the graph; the host is built, never started, so nothing listens.
            using var configuration = ServerConfiguration.Load(
            [
                $"--Database:Path={Path.Combine(directory, "test.db")}", "--ContentWarmup:Enabled=false", "--Networking:Enabled=false",
                "--LauncherAuth:Enabled=true", "--LauncherAuth:ListenUrl=http://127.0.0.1:41831/",
                "--GmWeb:Enabled=true", "--GmWeb:ListenUrl=http://127.0.0.1:41832/",
            ], out _);
            IServiceCollection? captured = null;
            var host = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
                .UseEnvironment(Environments.Production)
                .UseContentRoot(AppContext.BaseDirectory)
                .ConfigureAppConfiguration((_, sources) =>
                {
                    sources.Sources.Clear();
                    sources.AddConfiguration(configuration);
                })
                .Compose(configuration)
                .ConfigureServices(services => captured = services)
                .Build();
            return new(directory, host, captured!);
        }

        public void Dispose()
        {
            host.Dispose();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
        }
    }

    private sealed class Registered;
    private sealed class Required(Registered registered) { public Registered Value => registered; }
    private sealed class NullableService(ILogger? logger) { public ILogger? Value => logger; }
    private sealed class DefaultedService(Registered registered = null!) { public Registered Value => registered; }
    private sealed class NullableAndDefaulted(ILogger<NullableAndDefaulted>? logger = null) { public object? Value => logger; }
    private sealed class OptionalData(string? name = null, IReadOnlyList<int>? values = null) { public object? Value => (name, values); }

    private sealed class TwoConstructors
    {
        public TwoConstructors() { }
        public TwoConstructors(Registered registered) => _ = registered;
    }
}
