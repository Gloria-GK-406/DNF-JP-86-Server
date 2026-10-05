using System.Reflection;
using Dfo.Server.Application.Inventory;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class DailyRefillConfigurationBoundaryTests
{
    [Fact]
    public void Configuration_domain_does_not_receive_PVF_content_sources()
    {
        var parameters = Dependencies(typeof(DailyRefillConfigurationDomainService));
        Assert.NotEmpty(parameters);
        Assert.DoesNotContain(parameters, type => type.Namespace == "Dfo.Server.Abstractions.Content");
    }

    [Fact]
    public void Configuration_rules_do_not_receive_persisted_or_player_state()
    {
        var parameters = Dependencies(typeof(DailyRefillConfigurationRuleService));
        Assert.NotEmpty(parameters);
        Assert.DoesNotContain(parameters, type => type.Name.EndsWith("Persistence", StringComparison.Ordinal)
            || type.Name.EndsWith("Table", StringComparison.Ordinal)
            || type.Name.EndsWith("Store", StringComparison.Ordinal)
            || type.Name.EndsWith("DomainService", StringComparison.Ordinal)
            || type.Namespace == "Dfo.Server.Abstractions.Transactions");
    }

    private static Type[] Dependencies(Type service) => service
        .GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
        .SelectMany(constructor => constructor.GetParameters()).Select(parameter => parameter.ParameterType).ToArray();
}
