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

public interface INetworkMessageSender
{
    ValueTask SendAsync(
        NetworkConnectionId connectionId,
        IServerNetworkMessage message,
        CancellationToken cancellationToken);

    ValueTask SendExplicitAsync(
        NetworkConnectionId requiredDirectConnectionId,
        ExplicitMessageDelivery delivery,
        CancellationToken cancellationToken);
}

public interface INetworkConnectionCloser
{
    /// <summary>
    /// Requests transport closure and returns once that request has been scheduled, not when the
    /// connection's close callbacks have completed.
    /// </summary>
    ValueTask RequestCloseAsync(
        NetworkConnectionId connectionId,
        CancellationToken cancellationToken);
}

public interface INetworkConnectionObserver
{
    ValueTask ConnectedAsync(NetworkConnectionContext connection, CancellationToken cancellationToken);

    ValueTask DisconnectedAsync(NetworkConnectionContext connection, CancellationToken cancellationToken);
}
