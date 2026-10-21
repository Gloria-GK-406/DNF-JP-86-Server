using System.Buffers;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;
using Dfo.Server.Networking.Framing;
using Microsoft.Extensions.Logging;
using TouchSocket.Sockets;

namespace Dfo.Server.Networking.Runtime;

internal sealed class TouchSocketNetworkMessageSender(
    TouchSocketConnectionRegistry connections,
    ServerMessageEncoderRegistry encoders,
    ILogger<TouchSocketNetworkMessageSender> logger) : INetworkMessageSender
{
    /// <summary>Per transport write, and only on the executor (<see cref="ExecutorSendDeadline"/>).</summary>
    internal TimeSpan DirectSendDeadline { get; init; } = TimeSpan.FromSeconds(2);

    public async ValueTask SendBatchAsync(NetworkConnectionContext expectedConnection,
        IReadOnlyList<IServerNetworkMessage> messages, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(expectedConnection);
        ArgumentNullException.ThrowIfNull(messages);
        cancellationToken.ThrowIfCancellationRequested();
        var connection = connections.Get(expectedConnection.ConnectionId);
        if (!ReferenceEquals(connection.Context, expectedConnection))
            throw new InvalidOperationException("The batch recipient transport has been replaced.");
        var selected = messages.ToArray();
        var entries = selected.Select(message =>
        {
            ArgumentNullException.ThrowIfNull(message);
            var entry = encoders.Find(message.GetType()) ?? throw new InvalidOperationException(
                $"Server message '{message.GetType().Name}' has no registered encoder.");
            if (entry.Endpoint != expectedConnection.EndpointKind)
                throw new InvalidOperationException($"Server message '{message.GetType().Name}' has an incompatible endpoint.");
            return entry;
        }).ToArray();
        var frames = new List<(byte[] Bytes, string Message)>();
        for (var i = 0; i < selected.Length; i++)
        {
            foreach (var frame in entries[i].Encoder.Encode(selected[i]))
            {
                if (frame is null || frame.Length > DnfFrameProtocol.MaximumFrameLength)
                    throw new InvalidDataException("An encoded batch frame exceeds the transport limit or is unavailable.");
                // Own buffers before encoding the next message, which may reuse an encoder buffer.
                frames.Add((frame.ToArray(), selected[i].GetType().Name));
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        // Keep the captured session throughout; never route a later frame through the ID again.
        foreach (var frame in frames)
        {
            if (logger.IsEnabled(LogLevel.Debug) && expectedConnection.EndpointKind == NetworkEndpointKind.Game
                && frame.Bytes.Length >= 3)
                logger.LogDebug("S->C {ConnectionId} selector=0x{Selector:X2} type=0x{Type:X4} frame={Length} {Message}",
                    expectedConnection.ConnectionId, frame.Bytes[0], (ushort)(frame.Bytes[1] | (frame.Bytes[2] << 8)),
                    frame.Bytes.Length, frame.Message);
            await WriteAsync(connection.Session, expectedConnection.ConnectionId, frame.Bytes, frame.Message,
                cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask SendAsync(
        NetworkConnectionId connectionId,
        IServerNetworkMessage message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var connection = connections.Get(connectionId);
        var entry = encoders.Find(message.GetType())
            ?? throw new InvalidOperationException(
                $"Server message '{message.GetType().Name}' has no registered encoder.");

        // The endpoint rule is applied once, here, rather than inside each encoder: which endpoint
        // may receive a message is a routing fact, and keeping it out of the encoders is what lets
        // it be reviewed without reading any encoding.
        if (entry.Endpoint != connection.Context.EndpointKind)
        {
            throw new InvalidOperationException(
                $"Server message '{message.GetType().Name}' is not supported for endpoint " +
                $"'{connection.Context.EndpointKind}'.");
        }

        foreach (var frame in entry.Encoder.Encode(message))
        {
            // One line per outbound frame, at Debug. The client-side capture records no selector
            // for inbound frames, so without this the CMD/NOTI family of everything the server
            // sends can only be inferred -- and inference is wrong precisely where it matters,
            // because the same type number is two unrelated protocols in the two families.
            if (logger.IsEnabled(LogLevel.Debug) && connection.Context.EndpointKind == NetworkEndpointKind.Game
                && frame.Length >= 3)
            {
                logger.LogDebug(
                    "S->C {ConnectionId} selector=0x{Selector:X2} type=0x{Type:X4} frame={Length} {Message}",
                    connectionId,
                    frame[0],
                    (ushort)(frame[1] | (frame[2] << 8)),
                    frame.Length,
                    message.GetType().Name);
            }

            await WriteAsync(connection.Session, connectionId, frame, message.GetType().Name, cancellationToken);
        }
    }

    // The session this write used is the one closed on timeout: looking the ID up again could reach
    // a connection that has replaced it.
    private Task WriteAsync(ITcpSessionClient session, NetworkConnectionId connectionId, byte[] frame,
        string message, CancellationToken cancellationToken) =>
        ExecutorSendDeadline.AwaitAsync(
            ((ISender)session).SendAsync(new ReadOnlySequence<byte>(frame), cancellationToken),
            DirectSendDeadline,
            connectionId,
            () =>
            {
                ExecutorSendDeadline.StartDetachedClose(
                    () => session.CloseAsync("direct send deadline exceeded", CancellationToken.None), logger);
                try
                {
                    logger.LogWarning(
                        "Direct send of {Message} to {ConnectionId} did not complete within {Deadline} on the gameplay executor; closing the connection",
                        message, connectionId, DirectSendDeadline);
                }
                catch (Exception)
                {
                    // A failing log provider must not replace the timeout the caller is owed.
                }
            },
            cancellationToken);

    public ValueTask SendExplicitAsync(
        NetworkConnectionId requiredDirectConnectionId,
        ExplicitMessageDelivery delivery,
        CancellationToken cancellationToken) =>
        DeliverExplicitAsync(
            requiredDirectConnectionId,
            delivery,
            SendAsync,
            cancellationToken);

    internal static async ValueTask DeliverExplicitAsync(
        NetworkConnectionId requiredDirectConnectionId,
        ExplicitMessageDelivery delivery,
        Func<NetworkConnectionId, IServerNetworkMessage, CancellationToken, ValueTask> sendMessage,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        ArgumentNullException.ThrowIfNull(sendMessage);
        ValidateExplicitDelivery(delivery);

        foreach (var message in delivery.RequiredDirectMessages)
            await sendMessage(requiredDirectConnectionId, message, cancellationToken);

        foreach (var target in delivery.BestEffortTargets)
        {
            if (target.ConnectionId == requiredDirectConnectionId)
                continue;

            foreach (var message in target.OrderedMessages)
            {
                try
                {
                    await sendMessage(target.ConnectionId, message, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    // A disconnected or failed optional recipient does not prevent
                    // later explicit recipients from receiving their own ordered batch.
                    break;
                }
            }
        }
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
