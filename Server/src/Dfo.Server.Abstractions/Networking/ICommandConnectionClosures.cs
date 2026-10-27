using Dfo.Server.Data.Networking;

namespace Dfo.Server.Abstractions.Networking;

public interface ICommandConnectionClosures
{
    ValueTask ResolveAndRequestCloseAsync(NetworkConnectionId connectionId, CancellationToken cancellationToken) =>
        ValueTask.FromException(new NotSupportedException("This adapter cannot resolve pending lifecycle closures."));
    ValueTask RequestCloseAsync(NetworkConnectionContext expectedConnection, CancellationToken cancellationToken);
}
