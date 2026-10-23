using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Data.Networking;
using Dfo.Server.Networking.Framing;
using Dfo.Server.Networking.Runtime;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Networking.Outbound;

/// <summary>
/// Encodes at the registration point, on the job, with the checks and wording the direct send had:
/// a single message is refused as not supported for the connection's endpoint, a prepared batch as
/// having an incompatible endpoint, and a batch is all or nothing.
/// </summary>
internal sealed class RegistryOutboundEncoder(
    ServerMessageEncoderRegistry encoders,
    ILogger<RegistryOutboundEncoder> logger) : IOutboundMessageEncoder
{
    public byte[] EncodeMessage(NetworkConnectionContext connection, IServerNetworkMessage message)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(message);
        var entry = encoders.Find(message.GetType())
            ?? throw new InvalidOperationException(
                $"Server message '{message.GetType().Name}' has no registered encoder.");

        // The endpoint rule is applied once, here, rather than inside each encoder: which endpoint
        // may receive a message is a routing fact, and keeping it out of the encoders is what lets
        // it be reviewed without reading any encoding.
        if (entry.Endpoint != connection.EndpointKind)
        {
            throw new InvalidOperationException(
                $"Server message '{message.GetType().Name}' is not supported for endpoint " +
                $"'{connection.EndpointKind}'.");
        }

        using var buffer = new MemoryStream();
        foreach (var frame in entry.Encoder.Encode(message))
            Write(buffer, connection, frame, message, checkLength: false);
        return buffer.ToArray();
    }

    public byte[] EncodeBatch(NetworkConnectionContext connection, IReadOnlyList<IServerNetworkMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(messages);
        var entries = messages.Select(message =>
        {
            ArgumentNullException.ThrowIfNull(message);
            var entry = encoders.Find(message.GetType()) ?? throw new InvalidOperationException(
                $"Server message '{message.GetType().Name}' has no registered encoder.");
            if (entry.Endpoint != connection.EndpointKind)
                throw new InvalidOperationException($"Server message '{message.GetType().Name}' has an incompatible endpoint.");
            return entry;
        }).ToArray();
        using var buffer = new MemoryStream();
        for (var i = 0; i < messages.Count; i++)
        {
            foreach (var frame in entries[i].Encoder.Encode(messages[i]))
                Write(buffer, connection, frame, messages[i], checkLength: true);
        }

        return buffer.ToArray();
    }

    private void Write(MemoryStream buffer, NetworkConnectionContext connection, byte[] frame,
        IServerNetworkMessage message, bool checkLength)
    {
        if (checkLength && (frame is null || frame.Length > DnfFrameProtocol.MaximumFrameLength))
            throw new InvalidDataException("An encoded batch frame exceeds the transport limit or is unavailable.");
        ArgumentNullException.ThrowIfNull(frame);
        // One line per outbound frame, at Debug. The client-side capture records no selector
        // for inbound frames, so without this the CMD/NOTI family of everything the server
        // sends can only be inferred -- and inference is wrong precisely where it matters,
        // because the same type number is two unrelated protocols in the two families.
        if (logger.IsEnabled(LogLevel.Debug) && connection.EndpointKind == NetworkEndpointKind.Game
            && frame.Length >= 3)
            logger.LogDebug("S->C {ConnectionId} selector=0x{Selector:X2} type=0x{Type:X4} frame={Length} {Message}",
                connection.ConnectionId, frame[0], (ushort)(frame[1] | (frame[2] << 8)),
                frame.Length, message.GetType().Name);
        // Copied before the next message is encoded: an encoder may reuse its buffer.
        buffer.Write(frame);
    }
}
