using System.Reflection;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Application.Marriage;
using Dfo.Server.Application.Quests;
using Dfo.Server.Application.Tests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Marriage;

/// <summary>
/// Guards the one-way marriage graph: the use cases (<see cref="MarriageDomainService"/>) write relations, the pre-wedding
/// quests (<see cref="MarriageQuestDomainService"/>) only read them, and neither the quests nor the relations reach back up.
/// </summary>
/// <remarks>
/// The quest services take the pre-wedding quests, and the effect handlers behind love point coupons reach the quest
/// services; a pre-wedding quest service that reached the use cases would close that loop again. Each rule has a case that
/// feeds it a violation, so a rule that lets everything through cannot pass for a clean repository.
/// </remarks>
public sealed class MarriageArchitectureTests
{
    private static readonly Type[] Upward =
    [
        typeof(MarriageDomainService), typeof(IApplyEffectsDomainService), typeof(IMarriageQueryDomainService),
        typeof(IMarriageCoupleDomainService), typeof(IMarriageVenueDomainService),
    ];

    private static IEnumerable<Type> MarriageStores() => typeof(IMarriageRelationDomainStore).Assembly.GetTypes()
        .Where(x => x.IsInterface && x.Namespace == typeof(IMarriageRelationDomainStore).Namespace && x.Name.EndsWith("DomainStore", StringComparison.Ordinal));

    private static Type[] MarriageTypes() => typeof(MarriageDomainService).Assembly.GetTypes()
        .Where(x => x.Namespace == typeof(MarriageDomainService).Namespace && !x.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute)))
        .ToArray();

    private static IEnumerable<Type> Parameters(Type type) => ServiceGraph.Parameters(type);

    private static IReadOnlyList<string> StoreViolations(IReadOnlyCollection<Type> types, Type relationOwner, Type useCases)
    {
        var violations = new List<string>();
        foreach (var store in MarriageStores())
        {
            var takers = types.Where(x => Parameters(x).Contains(store)).ToArray();
            if (takers.Length > 1) violations.Add($"{store.Name} is taken by {string.Join(", ", takers.Select(x => x.Name))}");
            if (store == typeof(IMarriageRelationDomainStore))
                violations.AddRange(takers.Where(x => x != relationOwner).Select(x => $"{x.Name} takes the relation store"));
        }
        violations.AddRange(Parameters(useCases).Where(x => x.Name.EndsWith("DomainStore", StringComparison.Ordinal))
            .Select(x => $"{useCases.Name} takes {x.Name}"));
        return violations;
    }

    private static IReadOnlyList<string> RelationAccessViolations(IReadOnlyCollection<Type> types, Type relationOwner, Type useCases, Type quests)
    {
        var violations = new List<string>();
        foreach (var type in types)
        {
            var parameters = Parameters(type).ToArray();
            if (parameters.Contains(relationOwner)) violations.Add($"{type.Name} takes {relationOwner.Name}");
            if (parameters.Contains(typeof(IMarriageRelationWriter)) && type != useCases) violations.Add($"{type.Name} takes the relation writer");
        }
        if (!Parameters(quests).Contains(typeof(IMarriageRelations))) violations.Add($"{quests.Name} does not read relations");
        violations.AddRange(Parameters(relationOwner)
            .Where(x => x != typeof(IMarriageRelationDomainStore) && x != typeof(IMarriageProposalDomainStore)
                && x != typeof(IMarriageProposalRuleService) && x != typeof(TimeProvider))
            .Select(x => $"{relationOwner.Name} takes {x.Name}"));
        return violations;
    }

    /// <summary>The use cases' concrete collaborators, other than the quests, never take one another; their only interface is the relation writer.</summary>
    private static IReadOnlyList<string> UseCaseShapeViolations(Type useCases, Type quests)
    {
        var violations = Parameters(useCases)
            .Where(x => x.IsInterface && x != typeof(IMarriageRelationWriter))
            .Select(x => $"{useCases.Name} takes {x.Name}").ToList();
        var components = Parameters(useCases).Where(x => x.IsClass && !x.IsAbstract && x != typeof(TimeProvider) && x != quests).ToArray();
        foreach (var component in components)
            violations.AddRange(Parameters(component).Where(x => components.Contains(x) && x != typeof(HousingState)).Select(x => $"{component.Name} takes component {x.Name}"));
        return violations;
    }

    private static IReadOnlyList<string> DeferredViolations(IEnumerable<Type> types) =>
        types.SelectMany(type => Parameters(type)
            .Where(x => x == typeof(IServiceProvider) || x.IsGenericType && x.GetGenericTypeDefinition() is var g && (g == typeof(Lazy<>)
                || g.FullName!.StartsWith("System.Func`", StringComparison.Ordinal)))
            .Select(x => $"{type.Name} defers {x.Name}"))
            .ToArray();

    [Fact]
    public void Each_marriage_store_has_one_owner_and_the_use_cases_take_none()
    {
        var types = typeof(MarriageDomainService).Assembly.GetTypes();
        Assert.Empty(StoreViolations(types, typeof(CoupleLedger), typeof(MarriageDomainService)));
        Assert.All(MarriageStores(), store => Assert.Single(types, x => Parameters(x).Contains(store)));
    }

    [Fact]
    public void The_store_rule_refuses_a_second_owner_and_a_store_in_the_use_cases()
    {
        var violations = StoreViolations([typeof(BadUseCases), typeof(RelationReader), typeof(SecondRelationReader)],
            typeof(RelationReader), typeof(BadUseCases));
        Assert.Contains("BadUseCases takes IMarriageWeddingDomainStore", violations);
        Assert.Contains(violations, x => x.StartsWith("IMarriageRelationDomainStore is taken by", StringComparison.Ordinal));
        Assert.Contains("SecondRelationReader takes the relation store", violations);
    }

    [Fact]
    public void Only_the_use_cases_write_relations_and_the_quests_read_them()
    {
        Assert.Empty(RelationAccessViolations(typeof(MarriageDomainService).Assembly.GetTypes(), typeof(CoupleLedger),
            typeof(MarriageDomainService), typeof(MarriageQuestDomainService)));
        Assert.Contains(typeof(IMarriageRelationWriter), Parameters(typeof(MarriageDomainService)));
    }

    [Fact]
    public void The_relation_access_rule_refuses_a_second_writer_a_concrete_ledger_and_quests_that_do_not_read()
    {
        var violations = RelationAccessViolations([typeof(WritingQuests), typeof(LedgerHolder), typeof(LeaningLedger)], typeof(LeaningLedger),
            typeof(MarriageDomainService), typeof(WritingQuests));
        Assert.Contains("WritingQuests takes the relation writer", violations);
        Assert.Contains("WritingQuests does not read relations", violations);
        Assert.Contains("LedgerHolder takes LeaningLedger", violations);
        Assert.Contains("LeaningLedger takes IMarriageQuestDomainStore", violations);
    }

    [Fact]
    public void The_use_case_components_stand_apart_and_take_only_the_relation_writer() =>
        Assert.Empty(UseCaseShapeViolations(typeof(MarriageDomainService), typeof(MarriageQuestDomainService)));

    [Fact]
    public void The_use_case_shape_rule_refuses_a_leaning_component_and_another_interface() =>
        Assert.Equal(["ShapelessUseCases takes IMarriageQueryDomainService", "LeaningComponent takes component StandingComponent"],
            UseCaseShapeViolations(typeof(ShapelessUseCases), typeof(MarriageQuestDomainService)));

    [Fact]
    public void No_marriage_constructor_defers_a_dependency()
    {
        Assert.Empty(DeferredViolations(MarriageTypes()));
    }

    [Fact]
    public void The_deferral_rule_refuses_func_lazy_and_the_provider()
    {
        var violations = DeferredViolations([typeof(FuncRelay), typeof(LazyRelay), typeof(ProviderRelay)]);
        Assert.Equal(["FuncRelay defers Func`1", "LazyRelay defers Lazy`1", "ProviderRelay defers IServiceProvider"], violations);
    }

    [Fact]
    public void Neither_the_quests_nor_the_relations_reach_the_use_cases_or_the_effects()
    {
        using var production = new ServiceGraph.Production();
        Assert.Empty(production.Graph.ReachViolations(typeof(IMarriageQuestDomainService),
            [.. Upward, typeof(QuestFactsDomainService), typeof(IQuestFactsDomainService), typeof(IMarriageRelationWriter)]));
        Assert.Empty(production.Graph.ReachViolations(typeof(MarriageTrialClearSubscriber), Upward));
        Assert.Empty(production.Graph.ReachViolations(typeof(IMarriageRelations),
            [.. Upward, typeof(MarriageQuestDomainService), typeof(IMarriageQuestDomainService)]));
        Assert.Contains(typeof(CoupleLedger), production.Graph.Reach(typeof(IMarriageQuestDomainService)));
        Assert.Empty(production.Graph.ReachViolations(typeof(IMarriageHousingQueryDomainService),
            [.. Upward, typeof(IMarriageHousingDomainService), typeof(IMarriageRelationWriter)]));
        Assert.Empty(production.Graph.ReachViolations(typeof(IMarriageCapsuleQueryDomainService),
            [.. Upward, typeof(IMarriageCapsuleDomainService), typeof(IMarriageRelationWriter)]));
        Assert.Empty(production.Graph.ReachViolations(typeof(CapsuleClearSubscriber),
            [.. Upward, typeof(IMarriageCapsuleDomainService), typeof(IMarriageRelationWriter)]));
    }

    [Fact]
    public void The_reach_rule_finds_edges_behind_an_interface_a_func_and_a_factory()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IRelay, FuncRelay>();
        services.AddSingleton<IFactoryRelay>(_ => new QueryRelay(null!));
        using var provider = services.BuildServiceProvider();
        Assert.Equal(["LeakyQuests reaches IApplyEffectsDomainService", "LeakyQuests reaches IMarriageQueryDomainService"],
            new ServiceGraph(services, provider).ReachViolations(typeof(LeakyQuests), Upward));
    }

    [Fact]
    public void The_production_modules_resolve_every_marriage_face_and_one_relation_instance()
    {
        using var production = new ServiceGraph.Production();
        var provider = production.Provider;
        Assert.IsType<MarriageQuestDomainService>(provider.GetRequiredService<IMarriageQuestDomainService>());
        var useCases = Assert.IsType<MarriageDomainService>(provider.GetRequiredService<IMarriageQueryDomainService>());
        Assert.Same(useCases, provider.GetRequiredService<IMarriageCoupleDomainService>());
        Assert.Same(useCases, provider.GetRequiredService<IMarriageVenueDomainService>());
        Assert.NotNull(provider.GetRequiredService<IApplyEffectsDomainService>());
        using (var scope = provider.CreateScope())
            Assert.NotNull(scope.ServiceProvider.GetRequiredService<MarriageTrialClearSubscriber>());
        var relations = provider.GetRequiredService<IMarriageRelations>();
        Assert.Same(relations, provider.GetRequiredService<IMarriageRelationWriter>());
        Assert.Same(relations, provider.GetRequiredService<CoupleLedger>());
    }

    private sealed class StandingComponent { }

    private sealed class LeaningComponent(StandingComponent standing) { public object Held => standing; }

    private sealed class ShapelessUseCases(LeaningComponent leaning, StandingComponent standing, IMarriageQueryDomainService query,
        IMarriageRelationWriter relations, TimeProvider clock) { public object[] Held => [leaning, standing, query, relations, clock]; }

    private sealed class BadUseCases(RelationReader reader, IMarriageWeddingDomainStore weddings) { public object[] Held => [reader, weddings]; }

    private sealed class RelationReader(IMarriageRelationDomainStore relations) { public object Held => relations; }

    private sealed class SecondRelationReader(IMarriageRelationDomainStore relations) { public object Held => relations; }

    private sealed class WritingQuests(IMarriageRelationWriter relations) { public object Held => relations; }

    private sealed class LeaningLedger(IMarriageRelationDomainStore relations, IMarriageQuestDomainStore bindings) { public object[] Held => [relations, bindings]; }

    private sealed class LedgerHolder(LeaningLedger ledger) { public object Held => ledger; }

    private interface IRelay;

    private interface IFactoryRelay;

    private sealed class FuncRelay(Func<IApplyEffectsDomainService> effects) : IRelay { public object Held => effects; }

    private sealed class LazyRelay(Lazy<IApplyEffectsDomainService> effects) { public object Held => effects; }

    private sealed class ProviderRelay(IServiceProvider provider) { public object Held => provider; }

    private sealed class QueryRelay(IMarriageQueryDomainService query) : IFactoryRelay { public object Held => query; }

    private sealed class LeakyQuests(IRelay relay, IFactoryRelay other) { public object[] Held => [relay, other]; }
}
