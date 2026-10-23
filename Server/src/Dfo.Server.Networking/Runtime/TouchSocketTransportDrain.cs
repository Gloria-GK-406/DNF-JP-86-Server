using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Networking;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Networking.Runtime;

/// <summary>
/// Before the executor stops (RQ-009), every connection still registered is closed and torn down, and
/// every close callback in flight has seen its lifecycle CMD run, so no teardown arrives at a stopped
/// executor.
/// </summary>
/// <remarks>
/// TouchSocket 4.3.1 normally closes its sessions and awaits their close callbacks while its service
/// stops (measured for TASK-10), but a client that disconnects while the service is stopping can lose
/// its close callback: the entry stays registered and no callback ever comes. A connection still
/// registered here is therefore torn down by the drain itself, with the removal from the registry
/// deciding between the drain and a late callback, so the teardown runs exactly once. Closing goes
/// through the transport session itself, off the executor and unawaited, never through the outbox:
/// the drain is not a job.
/// </remarks>
internal sealed class TouchSocketTransportDrain(
    TouchSocketConnectionRegistry connections,
    INetworkConnectionLifecycleDispatcher lifecycle,
    ILogger<TouchSocketTransportDrain> logger) : IGameplayProducerDrain
{
    public async ValueTask DrainAsync(CancellationToken cancellationToken)
    {
        foreach (var entry in connections.Snapshot())
        {
            cancellationToken.ThrowIfCancellationRequested();
            CloseDetached(entry);
            if (!connections.TryRemove(entry.Context.ConnectionId, out var context)) continue;
            logger.LogInformation(
                "Connection {ConnectionId} was still registered after the transport stopped; the drain tears it down",
                context!.ConnectionId);
            // One failed teardown must not cost the others theirs, as with independent close callbacks.
            try
            {
                await lifecycle.DispatchDisconnectedAsync(context, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception error)
            {
                logger.LogError(error, "The drain's teardown of connection {ConnectionId} failed", context.ConnectionId);
            }
        }
        await connections.WhenIdleAsync(cancellationToken).ConfigureAwait(false);
    }

    // Not awaited: TouchSocket waits for the active receive callback while closing. The drain runs off the
    // executor, so the close carries no job's flow.
    private void CloseDetached(TouchSocketConnectionRegistry.ConnectionEntry entry)
    {
        if (connections.TryGet(entry.Context.ConnectionId, out var current) && current is not null
            && ReferenceEquals(current.Context, entry.Context))
            _ = CloseAsync(current.Session);
    }

    private async Task CloseAsync(TouchSocket.Sockets.IClosableClient session)
    {
        await Task.Yield();
        try
        {
            await session.CloseAsync("transport drain", CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            try
            {
                logger.LogWarning(error, "Closing a connection still registered at the drain failed");
            }
            catch (Exception)
            {
                // Nothing is left to report to.
            }
        }
    }
}
