using System.Buffers;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;
using TouchSocket.Sockets;

namespace Dfo.Server.Networking.Runtime;

internal sealed class TouchSocketNetworkMessageSender(
    TouchSocketConnectionRegistry connections,
    ServerMessageEncoderRegistry encoders) : INetworkMessageSender
{
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
            await ((ISender)connection.Session).SendAsync(
                new ReadOnlySequence<byte>(frame),
                cancellationToken);
        }
    }

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
