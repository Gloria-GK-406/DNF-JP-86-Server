using System.Runtime.CompilerServices;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Initialization;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Application.Tests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Characters;

/// <summary>
/// Guards the production dependency graph against loops and against the deferrals that used to hide them.
/// </summary>
/// <remarks>
/// A <c>Func</c>, <c>Lazy</c> or service provider in a constructor lets a loop build, and the loop then shows up as a
/// service that reads the world from below it. So no constructor defers, apart from the named mechanisms that resolve an
/// open set of handlers inside a job's scope; and the graph the container would build is checked for loops directly.
/// Each rule has a case that feeds it a violation, so a rule that lets everything through cannot pass for a clean repository.
/// </remarks>
public sealed class DependencyGraphArchitectureTests
{
    /// <summary>Mechanisms that resolve by registered type at run time; each names why that cannot be a constructor edge.</summary>
    private static readonly Dictionary<string, string> Mechanisms = new(StringComparer.Ordinal)
    {
        ["Dfo.Server.Application.DomainEvents.EventService"] =
            "Opens a scope per projection and resolves the notification subscriber registered for that type.",
        ["Dfo.Server.Application.DomainEvents.DomainEventDispatcher"] =
            "Resolves each subscriber by the handler type its registration names, inside the event CMD's scope.",
        ["Dfo.Server.Application.Scheduling.CommandScheduledTaskDispatch"] =
            "Resolves the scheduled task by the handler type its registration names, inside the job's scope.",
    };

    private static Type[] ProductionTypes() =>
        new[] { typeof(ApplicationModule), typeof(Dfo.Server.Protocol.ProtocolModule), typeof(Dfo.Server.Persistence.PersistenceModule),
                typeof(Dfo.Server.Infrastructure.InfrastructureModule) }
            .Select(x => x.Assembly).Distinct()
            .SelectMany(x => x.GetTypes())
            .Where(x => x.IsClass && x.Namespace?.StartsWith("Dfo.", StringComparison.Ordinal) == true
                && !x.IsDefined(typeof(CompilerGeneratedAttribute), false))
            .ToArray();

    private static void None(IReadOnlyList<string> violations) =>
        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));

    [Fact]
    public void No_constructor_defers_a_dependency_outside_the_named_mechanisms() =>
        None(ServiceGraph.Deferrals(ProductionTypes().Where(x => !Mechanisms.ContainsKey(x.FullName!))));

    [Fact]
    public void Every_named_mechanism_still_exists_and_still_defers()
    {
        var types = ProductionTypes().ToDictionary(x => x.FullName!);
        Assert.All(Mechanisms.Keys, name => Assert.NotEmpty(ServiceGraph.Deferrals([types[name]])));
    }

    [Fact]
    public void The_deferral_rule_refuses_func_lazy_and_the_provider_but_not_a_callback() =>
        Assert.Equal(["FuncRelay defers Func`1", "FactoryRelay defers Func`2", "LazyRelay defers Lazy`1", "ProviderRelay defers IServiceProvider"],
            ServiceGraph.Deferrals([typeof(FuncRelay), typeof(FactoryRelay), typeof(LazyRelay), typeof(ProviderRelay), typeof(CallbackHolder)]));

    [Fact]
    public void The_production_graph_has_no_loop()
    {
        using var production = new ServiceGraph.Production();
        None(production.Graph.Cycles());
    }

    [Fact]
    public void The_loop_rule_finds_a_loop_behind_an_interface_and_a_factory()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoopHead, LoopHead>();
        services.AddSingleton<ILoopTail>(provider => new LoopTail(null!));
        services.AddSingleton<Straight>();
        using var provider = services.BuildServiceProvider();
        Assert.Equal(["LoopHead, LoopTail"], new ServiceGraph(services, provider).Cycles());
    }

    /// <summary>The character entity sits below everything that acts on a character.</summary>
    [Fact]
    public void The_character_entity_reaches_neither_the_world_nor_the_effects_nor_the_roster()
    {
        using var production = new ServiceGraph.Production();
        Assert.Empty(production.Graph.ReachViolations(typeof(ICharacterDomainService),
        [
            typeof(IDungeonRunDomainService), typeof(IIllusionTowerDomainService), typeof(IDungeonParticipationDomainService),
            typeof(IApplyEffectsDomainService), typeof(ICharacterRosterDomainService), typeof(IInitializationDomainService),
            typeof(ICharacterAppearanceDomainService), typeof(ICharacterRepository),
        ]));
        Assert.Empty(production.Graph.ReachViolations(typeof(IHerasQuotaDomainService),
            [typeof(IHerasDungeonDomainService), typeof(IDungeonAdmissionDomainService)]));
        // A rule service is handed its facts; it never asks the world for them.
        Assert.Empty(production.Graph.ReachViolations(typeof(IIndependentDropRuleService), [typeof(ICharacterDomainService)]));
    }

    private sealed class FuncRelay(Func<IApplyEffectsDomainService> effects) { public object Held => effects; }

    private sealed class FactoryRelay(Func<int, ICharacter> characters) { public object Held => characters; }

    private sealed class LazyRelay(Lazy<IApplyEffectsDomainService> effects) { public object Held => effects; }

    private sealed class ProviderRelay(IServiceProvider provider) { public object Held => provider; }

    private sealed class CallbackHolder(Func<bool> authority, Func<int, string> resolve) { public object[] Held => [authority, resolve]; }

    private interface ILoopHead;

    private interface ILoopTail;

    private sealed class LoopHead(ILoopTail tail) : ILoopHead { public object Held => tail; }

    private sealed class LoopTail(ILoopHead head) : ILoopTail { public object Held => head; }

    private sealed class Straight(ILoopTail tail) { public object Held => tail; }
}
