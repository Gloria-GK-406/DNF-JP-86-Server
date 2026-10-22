using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.Dispatching;

/// <summary>
/// Runs the connection observers as one lifecycle job on the gameplay executor and waits for it.
/// The loop is the one the transport plugin used to run itself, in DI registration order.
/// </summary>
/// <remarks>
/// Every lifecycle CMD is a new root in the transition: the close callback cannot tell a close a CMD
/// requested from one the transport made, so neither the parent chain nor <c>lifecycle:close-cleanup</c>
/// exists yet (T-007).
/// </remarks>
internal sealed class NetworkConnectionLifecycleDispatcher(
    IGameplayQueue queue,
    IEnumerable<INetworkConnectionObserver> observers,
    ILogger<NetworkConnectionLifecycleDispatcher> logger) : INetworkConnectionLifecycleDispatcher
{
    private readonly INetworkConnectionObserver[] observers = [.. observers];

    public ValueTask DispatchConnectedAsync(NetworkConnectionContext connection, CancellationToken cancellationToken) =>
        RunAsync(connection, "lifecycle:connected", async token =>
        {
            foreach (var observer in observers)
            {
                await observer.ConnectedAsync(connection, token);
            }
        }, cancellationToken);

    public ValueTask DispatchDisconnectedAsync(NetworkConnectionContext connection, CancellationToken cancellationToken) =>
        RunAsync(connection, "lifecycle:disconnected", async token =>
        {
            foreach (var observer in observers)
            {
                await observer.DisconnectedAsync(connection, token);
            }
        }, cancellationToken);

    private async ValueTask RunAsync(
        NetworkConnectionContext connection,
        string commandType,
        Func<CancellationToken, ValueTask> body,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (observers.Length == 0) return;
        try
        {
            await queue.RunAsync(new GameplayWork(
                GameplayShardKey.World,
                GameplayCallChain.NewRoot(GameplayCallSource.Lifecycle, connection.ConnectionId),
                commandType,
                body), cancellationToken).ConfigureAwait(false);
        }
        catch (GameplayExecutorStoppedException)
        {
            // The shutdown order exists to make this zero (RQ-009); a teardown lost here is a lost position.
            try
            {
                logger.LogWarning(
                    "The gameplay executor has stopped; {CommandType} for {ConnectionId} did not run",
                    commandType, connection.ConnectionId);
            }
            catch (Exception)
            {
                // Logging never decides the outcome.
            }
            throw;
        }
    }
}
