using Dfo.Server.Data.Networking;

namespace Dfo.Server.Abstractions.Networking;

public interface INetworkMessageDispatcher
{
    ValueTask DispatchAsync(
        NetworkDispatchContext context,
        IClientNetworkMessage message,
        CancellationToken cancellationToken);
}

/// <summary>An explicitly addressed best-effort message batch for one connection.</summary>
public sealed record ExplicitMessageTarget(
    NetworkConnectionId ConnectionId,
    IReadOnlyList<IServerNetworkMessage> OrderedMessages);

/// <summary>
/// A protocol-neutral explicit-recipient delivery plan. Required messages are delivered first to
/// the direct connection; secondary targets are already selected by the caller and are attempted
/// best-effort afterwards.
/// </summary>
/// <remarks>
/// This is a delivery plan, not a message. Each contained message still resolves through its own
/// CLR-type encoder; the plan adds only recipient order and failure isolation.
/// </remarks>
public sealed record ExplicitMessageDelivery(
    IReadOnlyList<IServerNetworkMessage> RequiredDirectMessages,
    IReadOnlyList<ExplicitMessageTarget> BestEffortTargets);

/// <summary>
/// Registers outbound messages in the running gameplay job's outbox (RQ-030): they are encoded here and
/// delivered after the job, never during it. A send therefore never reports a transport failure; a
/// connection that fails to take its frames is closed by the sender (RQ-036).
/// </summary>
public interface INetworkMessageSender
{
    /// <summary>Validates and encodes the whole ordered group before registering it for the exact captured connection.</summary>
    ValueTask SendBatchAsync(NetworkConnectionContext expectedConnection,
        IReadOnlyList<IServerNetworkMessage> messages, CancellationToken cancellationToken) =>
        ValueTask.FromException(new NotSupportedException("This sender does not support prepared typed batches."));

    ValueTask SendAsync(
        NetworkConnectionId connectionId,
        IServerNetworkMessage message,
        CancellationToken cancellationToken);

    ValueTask SendExplicitAsync(
        NetworkConnectionId requiredDirectConnectionId,
        ExplicitMessageDelivery delivery,
        CancellationToken cancellationToken);
}

/// <summary>
/// The connection currently registered under an ID, exactly as it was registered (RQ-035): an entry
/// registered against it never reaches a connection that later reuses the ID.
/// </summary>
public interface INetworkConnectionContexts
{
    bool TryGet(NetworkConnectionId connectionId, out NetworkConnectionContext? connection);
}

/// <summary>Closes connections from a gameplay job: the close is carried out after everything registered before it.</summary>
public interface INetworkConnectionCloser
{
    /// <summary>
    /// Registers the close in the running job's outbox and returns; the connection is closed once the
    /// job's earlier entries for it are delivered, and its close callbacks run later.
    /// </summary>
    ValueTask RequestCloseExactAsync(NetworkConnectionContext expected, CancellationToken cancellationToken);

    ValueTask RequestCloseAsync(
        NetworkConnectionId connectionId,
        CancellationToken cancellationToken);
}

/// <summary>
/// What the transport calls when a connection is established or lost (RQ-012): every
/// registered lifecycle handler runs, in registration order, as one lifecycle CMD.
/// Returns once that CMD has run (not delivered), like <see cref="INetworkMessageDispatcher"/>.
/// </summary>
public interface INetworkConnectionLifecycleDispatcher
{
    ValueTask DispatchConnectedAsync(NetworkConnectionContext connection, CancellationToken cancellationToken);

    ValueTask DispatchDisconnectedAsync(NetworkConnectionContext connection, CancellationToken cancellationToken);
}
