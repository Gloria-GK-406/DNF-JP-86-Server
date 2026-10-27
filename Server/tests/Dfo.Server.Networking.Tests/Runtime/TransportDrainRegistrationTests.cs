using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Networking.Runtime;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Networking.Tests.Runtime;

/// <summary>
/// The shutdown tests stay green without the transport drain (TouchSocket usually closes every
/// session itself), so its registration is held here.
/// </summary>
public sealed class TransportDrainRegistrationTests
{
    [Theory]
    [InlineData("false")]
    [InlineData("true")]
    public void The_networking_module_registers_the_transport_drain_as_a_producer_drain(string enabled)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Networking:Enabled"] = enabled,
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNetworkMessageDispatching();
        new NetworkingModule().ConfigureServices(services, configuration);
        using var provider = services.BuildServiceProvider();

        Assert.Single(provider.GetServices<IGameplayProducerDrain>().OfType<TouchSocketTransportDrain>());
    }
}
