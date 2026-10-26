using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Execution;
using Dfo.Server.Application.Execution.Outbox;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.Dispatching;

/// <summary>
/// Runs the connection observers as one lifecycle job on the gameplay executor and waits for it.
/// The loop is the one the transport plugin used to run itself, in DI registration order.
/// </summary>
/// <remarks>
/// <c>lifecycle:connected</c> is a new root. <c>lifecycle:disconnected</c> is the child of the CMD whose outbox
/// carried the close (RQ-050, <see cref="GameplayCloseCauses"/>); any other teardown (a client that went away, a
/// close the sender made on its own, RQ-036) is a new root. There is one CMD type for both: the chain tells them
/// apart (TASK-29 ruling 3). <c>lifecycle:connected</c> carries its connection as the job's source, so a failed
/// connect closes it after what its outbox keeps (RQ-031(c)); a disconnected connection has no source left to close.
/// </remarks>
internal sealed class NetworkConnectionLifecycleDispatcher(
    Commands.ICommandBus commands,
    IEnumerable<Commands.INetworkConnectionLifecycleHandler> observers,
    ILogger<NetworkConnectionLifecycleDispatcher> logger,
    GameplayCloseCauses causes) : INetworkConnectionLifecycleDispatcher
{
    private const string Disconnected = "lifecycle:disconnected";
    private readonly Commands.INetworkConnectionLifecycleHandler[] observers = [.. observers];

    public ValueTask DispatchConnectedAsync(NetworkConnectionContext connection, CancellationToken cancellationToken) =>
        RunAsync(connection, "lifecycle:connected", NewRoot(connection), connection,
            new Commands.ConnectedLifecycleCommand(connection), cancellationToken);

    public async ValueTask DispatchDisconnectedAsync(NetworkConnectionContext connection, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        var chain = causes.TryTake(connection, out var cause) ? cause.Child(GameplayCallSource.Lifecycle) : NewRoot(connection);
        try
        {
            await RunAsync(connection, Disconnected, chain, source: null, new Commands.DisconnectedLifecycleCommand(connection), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (GameplayDepthExceededException refused) when (!GameplayJob.EndedAJob(refused))
        {
            // RQ-053(d): the connection is already gone and nothing else would tear it down, so the teardown
            // runs as a new root; the refused chain's root connection is closed as for any other refusal.
            LogTeardownPastTheLimit(connection, refused.Chain);
            await RunAsync(connection, Disconnected, NewRoot(connection), source: null, new Commands.DisconnectedLifecycleCommand(connection), cancellationToken)
                .ConfigureAwait(false);
            if (refused.Chain.SourceConnection is { } root && root != connection.ConnectionId)
                await RunAsync(connection, "lifecycle:depth-exceeded", GameplayCallChain.NewRoot(GameplayCallSource.Lifecycle),
                    source: null, new Commands.CloseLifecycleDepthRootCommand(refused.Chain), cancellationToken).ConfigureAwait(false);
        }
    }

    private static GameplayCallChain NewRoot(NetworkConnectionContext connection) =>
        GameplayCallChain.NewRoot(GameplayCallSource.Lifecycle, connection.ConnectionId);

    private void LogTeardownPastTheLimit(NetworkConnectionContext connection, GameplayCallChain refused)
    {
        try
        {
            logger.LogWarning(
                "The teardown of {ConnectionId} exceeded the depth limit and runs as a new root: refused root {RootId}, " +
                "parent {ParentId}, depth {Depth}",
                connection.ConnectionId, refused.RootId, refused.ParentId, refused.Depth);
        }
        catch (Exception)
        {
            // Logging never decides the outcome.
        }
    }

    private async ValueTask RunAsync(
        NetworkConnectionContext connection,
        string commandType,
        GameplayCallChain chain,
        NetworkConnectionContext? source,
        Commands.ICommand<bool> command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (observers.Length == 0) return;
        try
        {
            await commands.SendInternalAsync(command, new(GameplayCallSource.Lifecycle, chain, commandType, SourceConnection: source), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (GameplayExecutorStoppedException stopped)
        {
            // The shutdown order exists to make this zero (RQ-009); a teardown lost here is a lost position.
            try
            {
                logger.LogWarning(
                    stopped,
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
