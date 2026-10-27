using Dfo.Server.Data.Configuration;
namespace Dfo.Server.Abstractions.Configuration;

/// <summary>Deployment facts read only when a successful login needs its response.</summary>
public interface IChannelLoginDeploymentSource
{
    byte ReadDefaultEnvironment();
    PublicGameLoginEndpoints ReadEndpoints();
}
