using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Logging;
using TouchSocket.Sockets;

namespace Dfo.Server.Networking.Runtime;

internal sealed class TouchSocketNetworkConnectionCloser(
    TouchSocketConnectionRegistry connections,
    ILogger<TouchSocketNetworkConnectionCloser> logger)
    : INetworkConnectionCloser
{
    public ValueTask RequestCloseAsync(
        NetworkConnectionId connectionId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var connection = connections.Get(connectionId);
        _ = CloseDetachedAsync(connection.Session);
        return ValueTask.CompletedTask;
    }

    private async Task CloseDetachedAsync(IClosableClient session)
    {
        // TouchSocket waits for the active receive callback to finish while closing. Protocol
        // handlers run inside that callback, so awaiting the close here would wait on ourselves.
        await Task.Yield();
        try
        {
            await session.CloseAsync("protocol handling failed", CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception error)
        {
            logger.LogWarning(error, "Requested network connection close failed");
        }
    }
}
