using Dfo.Server.Data.Networking;

namespace Dfo.Server.Abstractions.Networking;

public interface ICommandConnectionClosures
{
    ValueTask RequestCloseAsync(NetworkConnectionContext expectedConnection, CancellationToken cancellationToken);
}
