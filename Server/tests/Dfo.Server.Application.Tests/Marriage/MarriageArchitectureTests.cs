using System.Reflection;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Application.Marriage;

namespace Dfo.Server.Application.Tests.Marriage;

/// <summary>
/// Guards the shape of the marriage entity: it takes only its components and the clock, the components never take each
/// other, every marriage store reaches exactly one constructor, and only the ledger reaches the relation store.
/// </summary>
public sealed class MarriageArchitectureTests
{
    private static IEnumerable<Type> MarriageStores() => typeof(IMarriageRelationDomainStore).Assembly.GetTypes()
        .Where(x => x.IsInterface && x.Namespace == typeof(IMarriageRelationDomainStore).Namespace && x.Name.EndsWith("DomainStore", StringComparison.Ordinal));

    private static IEnumerable<Type> Parameters(Type type) =>
        type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .SelectMany(x => x.GetParameters()).Select(x => x.ParameterType).Distinct();

    /// <summary>The entity's components: every concrete class among its constructor parameters.</summary>
    private static Type[] Components(Type entity) =>
        Parameters(entity).Where(x => x.IsClass && !x.IsAbstract && x != typeof(TimeProvider)).ToArray();

    private static IReadOnlyList<string> Violations(Type entity, IReadOnlyCollection<Type> types, Type ledger)
    {
        var violations = new List<string>();
        foreach (var parameter in Parameters(entity))
            if (parameter != typeof(TimeProvider) && !(parameter.IsClass && !parameter.IsAbstract))
                violations.Add($"{entity.Name} takes {parameter.Name}");
        var components = Components(entity);
        foreach (var component in components)
            foreach (var parameter in Parameters(component).Where(components.Contains))
                violations.Add($"{component.Name} takes component {parameter.Name}");
        foreach (var store in MarriageStores())
        {
            var takers = types.Where(x => Parameters(x).Contains(store)).ToArray();
            if (takers.Length > 1) violations.Add($"{store.Name} is taken by {string.Join(", ", takers.Select(x => x.Name))}");
            if (store == typeof(IMarriageRelationDomainStore))
                violations.AddRange(takers.Where(x => x != ledger).Select(x => $"{x.Name} takes the relation store"));
        }
        return violations;
    }

    [Fact]
    public void The_entity_is_composed_of_independent_components_and_each_store_has_one_owner()
    {
        var types = typeof(MarriageDomainService).Assembly.GetTypes();
        Assert.Empty(Violations(typeof(MarriageDomainService), types, typeof(CoupleLedger)));
        Assert.Equal(9, Components(typeof(MarriageDomainService)).Length);
        Assert.Contains(typeof(CoupleLedger), Components(typeof(MarriageDomainService)));
        Assert.All(MarriageStores(), store => Assert.Single(types, x => Parameters(x).Contains(store)));
    }

    [Fact]
    public void The_rule_refuses_a_store_in_the_entity_a_component_taking_another_and_a_second_relation_writer()
    {
        var violations = Violations(typeof(BadEntity), [typeof(BadEntity), typeof(LeaningComponent), typeof(RelationReader), typeof(SecondRelationReader)],
            typeof(CoupleLedger));
        Assert.Contains("BadEntity takes IMarriageWeddingDomainStore", violations);
        Assert.Contains("BadEntity takes IMarriageQueryDomainService", violations);
        Assert.Contains("LeaningComponent takes component RelationReader", violations);
        Assert.Contains(violations, x => x.StartsWith("IMarriageRelationDomainStore is taken by", StringComparison.Ordinal));
        Assert.Contains("RelationReader takes the relation store", violations);
        Assert.Contains("SecondRelationReader takes the relation store", violations);
    }

    private sealed class BadEntity(LeaningComponent component, RelationReader reader, IMarriageWeddingDomainStore weddings,
        IMarriageQueryDomainService other, TimeProvider clock)
    {
        public object[] Held => [component, reader, weddings, other, clock];
    }

    private sealed class LeaningComponent(RelationReader reader) { public object Held => reader; }

    private sealed class RelationReader(IMarriageRelationDomainStore relations) { public object Held => relations; }

    private sealed class SecondRelationReader(IMarriageRelationDomainStore relations) { public object Held => relations; }
}
