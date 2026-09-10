using Dfo.Server.Data.Configuration;

namespace Dfo.Server.Abstractions.Configuration;

public interface IPublicEndpointSource
{
    PublicEndpointSnapshot Snapshot { get; }
}
