using System.Buffers;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;
using TouchSocket.Sockets;

namespace Dfo.Server.Networking.Runtime;

internal sealed class TouchSocketNetworkMessageSender(
    TouchSocketConnectionRegistry connections,
    ServerMessageEncoderRegistry encoders,
    ISelectCharacterProjectionComposer projectionComposer) : INetworkMessageSender
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

    /// <remarks>
    /// Implemented explicitly rather than left to the interface default. The default forwards to
    /// <see cref="SendAsync"/>, which is why that method used to need a type test for the delivery
    /// message; taking the method over removes the reason for the test.
    /// </remarks>
    public ValueTask SendProjectionAsync(
        NetworkConnectionId requiredDirectConnectionId,
        SelectCharacterProjectionDelivery delivery,
        CancellationToken cancellationToken) =>
        DeliverProjectionAsync(
            requiredDirectConnectionId,
            delivery,
            projectionComposer,
            SendFrameAsync,
            cancellationToken);

    internal static async ValueTask DeliverProjectionAsync(
        NetworkConnectionId requiredDirectConnectionId,
        SelectCharacterProjectionDelivery delivery,
        ISelectCharacterProjectionComposer composer,
        Func<NetworkConnectionId, ReadOnlyMemory<byte>, CancellationToken, ValueTask> sendFrame,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        ArgumentNullException.ThrowIfNull(sendFrame);
        ArgumentNullException.ThrowIfNull(composer);
        ValidateProjectionDelivery(delivery);

        foreach (var frame in composer.Compose(delivery.RequiredDirectProjections))
        {
            await sendFrame(requiredDirectConnectionId, frame, cancellationToken);
        }

        foreach (var target in delivery.BestEffortTargets)
        {
            if (target.ConnectionId == requiredDirectConnectionId)
                continue;

            foreach (var frame in composer.Compose(target.OrderedProjections))
            {
                try
                {
                    await sendFrame(target.ConnectionId, frame, cancellationToken);
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

    private async ValueTask SendFrameAsync(
        NetworkConnectionId connectionId,
        ReadOnlyMemory<byte> frame,
        CancellationToken cancellationToken)
    {
        var connection = connections.Get(connectionId);
        if (connection.Context.EndpointKind != NetworkEndpointKind.Game)
        {
            throw new InvalidOperationException(
                $"Projection delivery requires a game endpoint, not '{connection.Context.EndpointKind}'.");
        }

        await ((ISender)connection.Session).SendAsync(
            new ReadOnlySequence<byte>(frame),
            cancellationToken);
    }

    private static void ValidateProjectionDelivery(SelectCharacterProjectionDelivery delivery)
    {
        if (delivery.RequiredDirectProjections is null)
            throw new InvalidDataException("Required direct projections are unavailable.");
        if (delivery.BestEffortTargets is null)
            throw new InvalidDataException("Best-effort projection targets are unavailable.");
        foreach (var projection in delivery.RequiredDirectProjections)
        {
            if (projection is null)
                throw new InvalidDataException("A required direct projection is unavailable.");
        }
        foreach (var target in delivery.BestEffortTargets)
        {
            if (target is null || target.OrderedProjections is null)
                throw new InvalidDataException("An explicit projection target is unavailable.");
            foreach (var projection in target.OrderedProjections)
            {
                if (projection is null)
                    throw new InvalidDataException("A best-effort projection is unavailable.");
            }
        }
    }
}
