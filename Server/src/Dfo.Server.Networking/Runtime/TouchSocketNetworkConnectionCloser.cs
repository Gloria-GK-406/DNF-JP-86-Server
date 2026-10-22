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

    public ValueTask RequestCloseExactAsync(NetworkConnectionContext expected, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (connections.TryGet(expected.ConnectionId, out var entry) && entry is not null && ReferenceEquals(entry.Context, expected))
            _ = CloseDetachedAsync(entry.Session);
        return ValueTask.CompletedTask;
    }

    // The close must not carry the caller's context (3e): close callbacks run inline by it would
    // otherwise see the requesting job's token and frames. CloseAsync returns at its Yield, still on
    // this thread, so the flow is restored where it was suppressed.
    private Task CloseDetachedAsync(IClosableClient session)
    {
        // Suppressing an already suppressed flow throws.
        if (ExecutionContext.IsFlowSuppressed()) return CloseAsync(session);
        using (ExecutionContext.SuppressFlow())
            return CloseAsync(session);
    }

    private async Task CloseAsync(IClosableClient session)
    {
        // TouchSocket waits for the active receive callback to finish while closing. Protocol
        // handlers run inside that callback, so awaiting the close here would wait on ourselves.
        // Yield rather than Task.Run: the close is scheduled exactly as before 3e.
        await Task.Yield();
        try
        {
            await session.CloseAsync("protocol handling failed", CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception error)
        {
            try
            {
                logger.LogWarning(error, "Requested network connection close failed");
            }
            catch (Exception)
            {
                // Nothing is left to report to.
            }
        }
    }
}
