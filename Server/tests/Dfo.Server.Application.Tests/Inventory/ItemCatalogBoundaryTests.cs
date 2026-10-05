using System.Reflection;
using Dfo.Server.Application.Initialization;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Abstractions.Inventory;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class ItemCatalogBoundaryTests
{
    [Theory]
    [InlineData(typeof(ItemCatalogDomainService))]
    [InlineData(typeof(ItemCatalogRuleService))]
    [InlineData(typeof(ItemEquipmentRuleService))]
    [InlineData(typeof(InitializationRuleService))]
    [InlineData(typeof(DailyRefillConfigurationRuleService))]
    public void Catalog_and_non_mail_purposes_do_not_depend_on_administration(Type service)
    {
        var dependencies = service.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .SelectMany(constructor => constructor.GetParameters()).Select(parameter => parameter.ParameterType);
        Assert.DoesNotContain(dependencies, type => type.Namespace == "Dfo.Server.Abstractions.Administration");
    }

    [Fact]
    public void Catalog_contracts_do_not_expose_administration_DTOs()
    {
        var contracts = new[] { typeof(IItemCatalogDomainService), typeof(IItemCatalogRuleService),
            typeof(IItemEquipmentRuleService), typeof(IItemCatalogSelectionRuleService) };
        var types = contracts.SelectMany(type => type.GetMethods())
            .SelectMany(method => method.GetParameters().Select(parameter => parameter.ParameterType).Append(method.ReturnType));
        Assert.DoesNotContain(types.SelectMany(ContainedTypes), type => type.Namespace == "Dfo.Server.Data.Administration");
    }

    private static IEnumerable<Type> ContainedTypes(Type type)
    {
        yield return type;
        foreach (var argument in type.GetGenericArguments().SelectMany(ContainedTypes)) yield return argument;
    }
}
