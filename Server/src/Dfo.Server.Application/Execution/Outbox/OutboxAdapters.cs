using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.Execution.Outbox;

/// <summary>
/// The only <see cref="INetworkMessageSender"/>: it registers, it never touches the transport (RQ-030).
/// Every message is encoded here, on the job, so an unencodable message still fails the call that sent
/// it (TASK-23 ruling 1); the encoded batch then waits in the job's outbox until the job ends.
/// </summary>
/// <remarks>
/// Order inside each call: find the connection, check the caller is a job (RQ-061, with its evidence),
/// encode, register. The position check comes first (F-97, TASK-30 ruling 2) so that a call off the
/// executor is refused as the program error it is, with its evidence, whatever it sends. A connection
/// that is no longer registered is not an error: the entry is dropped after the position check and the
/// job goes on (F-83).
/// </remarks>
internal sealed class OutboxNetworkMessageSender : INetworkMessageSender, ICommandMessageOutbox
{
    private readonly GameplayOutboxes outboxes;
    private readonly INetworkConnectionContexts? contexts;
    private readonly IOutboundMessageEncoder? encoder;
    private readonly ILogger<OutboxNetworkMessageSender> logger;

    /// <summary>Without networking every connection is unknown, so every send is dropped.</summary>
    public OutboxNetworkMessageSender(GameplayOutboxes outboxes, ILogger<OutboxNetworkMessageSender> logger)
    {
        this.outboxes = outboxes;
        this.logger = logger;
    }

    public OutboxNetworkMessageSender(
        GameplayOutboxes outboxes,
        INetworkConnectionContexts contexts,
        IOutboundMessageEncoder encoder,
        ILogger<OutboxNetworkMessageSender> logger)
        : this(outboxes, logger)
    {
        this.contexts = contexts;
        this.encoder = encoder;
    }

    public ValueTask SendAsync(NetworkConnectionId connectionId, IServerNetworkMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryFind(connectionId, out var connection))
            return Dropped(connectionId);
        var outbox = outboxes.Current;
        var frames = encoder!.EncodeMessage(connection, message);
        outbox.Register(OutboundEntry.Batch(connection, [message], frames));
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Registers the whole batch for exactly <paramref name="expectedConnection"/>. Whether that connection
    /// is still the registered one is decided by the sender before delivery (Stale, RQ-035).
    /// </summary>
    public ValueTask SendBatchAsync(NetworkConnectionContext expectedConnection,
        IReadOnlyList<IServerNetworkMessage> messages, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(expectedConnection);
        ArgumentNullException.ThrowIfNull(messages);
        cancellationToken.ThrowIfCancellationRequested();
        IServerNetworkMessage[] batch = [.. messages];
        if (batch.Length == 0)
        {
            _ = outboxes.Current;
            return ValueTask.CompletedTask;
        }

        if (encoder is null)
            return Dropped(expectedConnection.ConnectionId);
        var outbox = outboxes.Current;
        var frames = encoder.EncodeBatch(expectedConnection, batch);
        outbox.Register(OutboundEntry.Batch(expectedConnection, batch, frames));
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// The direct messages become one Required batch for their connection, then each other target one
    /// BestEffort batch, in plan order. A target that cannot be encoded, or is no longer connected, is
    /// skipped without failing the call, as its failed send was before. Inside an ordered scope the
    /// targets keep BestEffort, so a target's failure never stops the group.
    /// </summary>
    public ValueTask SendExplicitAsync(
        NetworkConnectionId requiredDirectConnectionId,
        ExplicitMessageDelivery delivery,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        ValidateExplicitDelivery(delivery);
        cancellationToken.ThrowIfCancellationRequested();
        if (delivery.RequiredDirectMessages.Count > 0)
        {
            if (TryFind(requiredDirectConnectionId, out var direct))
            {
                var outbox = outboxes.Current;
                var frames = EncodeEach(direct, delivery.RequiredDirectMessages);
                outbox.Register(OutboundEntry.Batch(direct, delivery.RequiredDirectMessages, frames));
            }
            else
            {
                _ = Dropped(requiredDirectConnectionId);
            }
        }

        foreach (var target in delivery.BestEffortTargets)
        {
            if (target.ConnectionId == requiredDirectConnectionId || target.OrderedMessages.Count == 0)
                continue;
            if (!TryFind(target.ConnectionId, out var connection))
            {
                _ = Dropped(target.ConnectionId);
                continue;
            }

            var outbox = outboxes.Current;
            byte[] frames;
            try
            {
                frames = EncodeEach(connection, target.OrderedMessages);
            }
            catch (Exception error)
            {
                // A failed optional recipient does not stop the later explicit recipients.
                logger.LogWarning(error, "A best-effort message for {ConnectionId} could not be encoded; that target is skipped",
                    target.ConnectionId);
                continue;
            }

            outbox.Register(OutboundEntry.Batch(
                connection, target.OrderedMessages, frames, OutboundDelivery.BestEffort));
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask RegisterAsync(NetworkConnectionId connectionId, IServerNetworkMessage message, CancellationToken cancellationToken) =>
        SendAsync(connectionId, message, cancellationToken);
    public ValueTask RegisterBatchAsync(NetworkConnectionContext connection, IReadOnlyList<IServerNetworkMessage> messages, CancellationToken cancellationToken) =>
        SendBatchAsync(connection, messages, cancellationToken);
    public ValueTask RegisterExplicitAsync(NetworkConnectionId connectionId, ExplicitMessageDelivery delivery, CancellationToken cancellationToken) =>
        SendExplicitAsync(connectionId, delivery, cancellationToken);

    private byte[] EncodeEach(NetworkConnectionContext connection, IReadOnlyList<IServerNetworkMessage> messages)
    {
        using var buffer = new MemoryStream();
        foreach (var message in messages)
            buffer.Write(encoder!.EncodeMessage(connection, message));
        return buffer.ToArray();
    }

    private bool TryFind(NetworkConnectionId connectionId, out NetworkConnectionContext connection)
    {
        if (contexts is not null && encoder is not null && contexts.TryGet(connectionId, out var found) && found is not null)
        {
            connection = found;
            return true;
        }

        connection = null!;
        return false;
    }

    private ValueTask Dropped(NetworkConnectionId connectionId)
    {
        // Still a registration point: off the executor this is the program error it always is.
        _ = outboxes.Current;
        if (logger.IsEnabled(LogLevel.Debug))
            logger.LogDebug("Outbound messages for {ConnectionId} were dropped: the connection is no longer registered",
                connectionId);
        return ValueTask.CompletedTask;
    }

    private static void ValidateExplicitDelivery(ExplicitMessageDelivery delivery)
    {
        if (delivery.RequiredDirectMessages is null)
            throw new InvalidDataException("Required direct messages are unavailable.");
        if (delivery.BestEffortTargets is null)
            throw new InvalidDataException("Best-effort message targets are unavailable.");
        foreach (var message in delivery.RequiredDirectMessages)
        {
            if (message is null)
                throw new InvalidDataException("A required direct message is unavailable.");
        }
        foreach (var target in delivery.BestEffortTargets)
        {
            if (target is null || target.OrderedMessages is null)
                throw new InvalidDataException("An explicit message target is unavailable.");
            foreach (var message in target.OrderedMessages)
            {
                if (message is null)
                    throw new InvalidDataException("A best-effort message is unavailable.");
            }
        }
    }
}

/// <summary>
/// The only <see cref="INetworkConnectionCloser"/>: a close is a request in the job's outbox, carried
/// out after every entry registered for that connection before it (RQ-012), and never before the job
/// ends. A connection that is no longer registered, or was replaced, is not closed (F-83, RQ-035).
/// </summary>
internal sealed class OutboxConnectionCloser : INetworkConnectionCloser, ICommandConnectionClosures
{
    private readonly GameplayOutboxes outboxes;
    private readonly INetworkConnectionContexts? contexts;
    private readonly ILogger<OutboxConnectionCloser> logger;

    public OutboxConnectionCloser(GameplayOutboxes outboxes, ILogger<OutboxConnectionCloser> logger)
    {
        this.outboxes = outboxes;
        this.logger = logger;
    }

    public OutboxConnectionCloser(
        GameplayOutboxes outboxes, INetworkConnectionContexts contexts, ILogger<OutboxConnectionCloser> logger)
        : this(outboxes, logger) => this.contexts = contexts;

    public ValueTask RequestCloseAsync(NetworkConnectionContext expected, CancellationToken cancellationToken) =>
        RequestCloseExactAsync(expected, cancellationToken);

    public ValueTask RequestCloseExactAsync(NetworkConnectionContext expected, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(expected);
        cancellationToken.ThrowIfCancellationRequested();
        var outbox = outboxes.Current;
        if (contexts is not null && contexts.TryGet(expected.ConnectionId, out var current) && ReferenceEquals(current, expected))
            outbox.RequestClose(expected);
        else
            Dropped(expected.ConnectionId);
        return ValueTask.CompletedTask;
    }

    public ValueTask RequestCloseAsync(NetworkConnectionId connectionId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var outbox = outboxes.Current;
        if (contexts is not null && contexts.TryGet(connectionId, out var current) && current is not null)
            outbox.RequestClose(current);
        else
            Dropped(connectionId);
        return ValueTask.CompletedTask;
    }

    private void Dropped(NetworkConnectionId connectionId)
    {
        if (logger.IsEnabled(LogLevel.Debug))
            logger.LogDebug("The close of {ConnectionId} was dropped: the connection is no longer registered as asked",
                connectionId);
    }
}

/// <summary>The outbox's ordered groups (spec appendix D), exposed to the protocol layer.</summary>
internal sealed class GameplayOutboxOrdering(GameplayOutboxes outboxes) : IOutboundOrdering
{
    public IOutboundOrderingScope Begin(string name, OutboundDelivery delivery) =>
        outboxes.CurrentOrNull?.OpenGroup(name, delivery) ?? new Unordered(delivery);

    private sealed class Unordered(OutboundDelivery delivery) : IOutboundOrderingScope
    {
        public OutboundDelivery Delivery { get; set; } = delivery;

        public void Dispose()
        {
        }
    }
}
