using System.Runtime.CompilerServices;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Execution.Outbox;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.Execution;

/// <summary>
/// RQ-050: the chain of the CMD whose outbox carried a connection's close, so that the connection's teardown is
/// that CMD's child. Recorded when the outbox is handed to the sender, taken once by the teardown.
/// </summary>
/// <remarks>
/// Kept per context instance, not per connection ID: a later connection with the same ID inherits nothing, and a
/// close the sender never carries out leaves nothing behind once its context is collected. A close the transport
/// makes while a recorded one is still waiting is taken as the recorded one (TASK-29 ruling 5, a known residue).
/// </remarks>
internal sealed class GameplayCloseCauses
{
    private readonly ConditionalWeakTable<NetworkConnectionContext, Cause> causes = new();

    /// <summary>The first recorded cause wins: the first close request is the one carried out.</summary>
    public void Record(NetworkConnectionContext connection, GameplayCallChain chain) =>
        causes.TryAdd(connection, new Cause(chain));

    public bool TryTake(NetworkConnectionContext connection, out GameplayCallChain chain)
    {
        if (causes.TryGetValue(connection, out var cause) && causes.Remove(connection))
        {
            chain = cause.Chain;
            return true;
        }

        chain = default;
        return false;
    }

    private sealed record Cause(GameplayCallChain Chain);
}

/// <summary>
/// RQ-053(a): from a system CMD, closes the root connection of a chain the executor refused. That close is the
/// depth check's, not a CMD's, so the root connection's teardown is a new root and is never refused in turn.
/// </summary>
internal sealed class GameplayDepthGuard
{
    private readonly GameplayOutboxes outboxes;
    private readonly INetworkConnectionContexts? contexts;
    private readonly ILogger<GameplayDepthGuard> logger;

    public GameplayDepthGuard(GameplayOutboxes outboxes, ILogger<GameplayDepthGuard> logger)
    {
        this.outboxes = outboxes;
        this.logger = logger;
    }

    public GameplayDepthGuard(GameplayOutboxes outboxes, INetworkConnectionContexts contexts, ILogger<GameplayDepthGuard> logger)
        : this(outboxes, logger) => this.contexts = contexts;

    /// <summary>On the system CMD's job. "Its root came from a client connection" means the refused chain has one (ruling 6(b)).</summary>
    public void CloseRootConnection(GameplayCallChain refused)
    {
        if (refused.SourceConnection is not { } connectionId) return;
        if (contexts is not null && contexts.TryGet(connectionId, out var current) && current is not null)
        {
            outboxes.Current.RequestCloseWithoutCause(current);
            return;
        }

        if (logger.IsEnabled(LogLevel.Debug))
            logger.LogDebug("The root connection {ConnectionId} of a chain past the depth limit is no longer registered", connectionId);
    }
}
