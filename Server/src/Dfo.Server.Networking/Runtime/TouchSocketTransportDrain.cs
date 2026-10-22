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
/// through the transport itself, never a replaced closer.
/// </remarks>
internal sealed class TouchSocketTransportDrain(
    TouchSocketConnectionRegistry connections,
    INetworkConnectionLifecycleDispatcher lifecycle,
    ILogger<TouchSocketNetworkConnectionCloser> closeLogger,
    ILogger<TouchSocketTransportDrain> logger) : IGameplayProducerDrain
{
    private readonly TouchSocketNetworkConnectionCloser closer = new(connections, closeLogger);

    public async ValueTask DrainAsync(CancellationToken cancellationToken)
    {
        foreach (var entry in connections.Snapshot())
        {
            await closer.RequestCloseExactAsync(entry.Context, cancellationToken).ConfigureAwait(false);
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
}
