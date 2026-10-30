using System.Reflection;
using Dfo.Server.Abstractions;
using Dfo.Server.Abstractions.Content;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Host.Tests;

/// <summary>
/// A service is registered once, unless it is a set its consumers read whole.
/// </summary>
/// <remarks>
/// Registrations live beside the domain they belong to, so two of them can name the same service from two files. The
/// container then hands out the last one and nothing says the first was lost. A service type registered more than
/// once with different implementations is therefore refused, unless something takes every registration at once: a
/// constructor asking for <c>IEnumerable</c> of it, or one of the two startup sets read through
/// <c>GetServices</c>.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class RegistrationArchitectureTests
{
    /// <summary>Sets read through <c>GetServices</c> rather than a constructor; each names its reader.</summary>
    private static readonly Dictionary<Type, string> ReadAsSets = new()
    {
        [typeof(IServerModule)] = "GameServerStartupService logs every composed module.",
        [typeof(IContentWarmup)] = "GameServerStartupService runs every warmup before any listener starts.",
    };

    [Fact]
    public void No_service_is_registered_twice_with_different_implementations()
    {
        using var composed = ProductionComposition.Build();
        var violations = Conflicts(composed.Services, composed.Provider, ConsumedAsSets(composed.Services));
        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void The_rule_refuses_a_second_implementation_but_not_a_set_or_a_repeat()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISingle, First>();
        services.AddSingleton<ISingle, Second>();
        services.AddSingleton<IMember, First>();
        services.AddSingleton<IMember, Second>();
        services.AddSingleton<Reader>();
        services.AddTransient<First>();
        services.AddTransient<First>();
        using var provider = services.BuildServiceProvider();
        Assert.Equal(["ISingle: First, Second"], Conflicts(services, provider, ConsumedAsSets(services)));
    }

    private static HashSet<Type> ConsumedAsSets(IServiceCollection services) =>
        services.Select(x => x.ImplementationType).OfType<Type>()
            .SelectMany(x => x.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
            .SelectMany(x => x.GetParameters()).Select(x => x.ParameterType)
            .Where(x => x.IsGenericType && x.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            .Select(x => x.GetGenericArguments()[0])
            .Concat(ReadAsSets.Keys)
            .ToHashSet();

    private static IReadOnlyList<string> Conflicts(IServiceCollection services, IServiceProvider provider, HashSet<Type> sets)
    {
        using var scope = provider.CreateScope();
        return services
            .Where(x => !x.IsKeyedService && !x.ServiceType.ContainsGenericParameters
                && x.ServiceType.Assembly.GetName().Name!.StartsWith("Dfo.", StringComparison.Ordinal)
                || x.ServiceType.DeclaringType == typeof(RegistrationArchitectureTests))
            .GroupBy(x => x.ServiceType)
            .Where(x => x.Count() > 1 && !sets.Contains(x.Key))
            .Select(group => (group.Key, Implementations: group.Select(x => Implementation(x, scope.ServiceProvider)).Distinct().ToArray()))
            .Where(x => x.Implementations.Length > 1)
            .Select(x => $"{x.Key.Name}: {string.Join(", ", x.Implementations.Select(t => t.Name))}")
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static Type Implementation(ServiceDescriptor descriptor, IServiceProvider provider) =>
        descriptor.ImplementationType
        ?? descriptor.ImplementationInstance?.GetType()
        ?? descriptor.ImplementationFactory!(provider).GetType();

    private interface ISingle;
    private interface IMember;
    private sealed class First : ISingle, IMember;
    private sealed class Second : ISingle, IMember;
    private sealed class Reader(IEnumerable<IMember> members) { public int Count => members.Count(); }
}
