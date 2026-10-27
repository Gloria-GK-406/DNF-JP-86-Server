using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Data.Configuration;
namespace Dfo.Server.Infrastructure.Configuration;

/// <summary>Reads the existing immutable server configuration snapshot, without eager validation.</summary>
internal sealed class ChannelLoginDeploymentSource(IServerConfigService configuration) : IChannelLoginDeploymentSource
{
    public byte ReadDefaultEnvironment() => configuration.GetConfig<byte>("GameLogin:Environment");

    public PublicGameLoginEndpoints ReadEndpoints()
    {
        var connectionPort = configuration.GetConfig<int>("GameLogin:ConnectionPort");
        var host = configuration.GetConfig<string>("Game:EndpointHost");
        var endpointPortA = configuration.GetConfig<int>("GameLogin:EndpointPortA");
        var endpointPortB = configuration.GetConfig<int>("GameLogin:EndpointPortB");
        return new PublicGameLoginEndpoints(host, connectionPort, endpointPortA, endpointPortB);
    }
}
