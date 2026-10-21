using Dfo.Server.Data.Networking;
using Dfo.Server.Networking.Framing;
using Dfo.Server.Networking.Runtime;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Networking.Outbound;

internal interface IOutboundEncoder
{
    /// <summary>The whole batch's frames, back to back; throws if any message cannot be encoded.</summary>
    byte[] Encode(NetworkConnectionContext connection, IReadOnlyList<IServerNetworkMessage> messages);
}

/// <summary>Same checks as <c>TouchSocketNetworkMessageSender.SendBatchAsync</c>: all of the batch or none.</summary>
internal sealed class RegistryOutboundEncoder(
    ServerMessageEncoderRegistry encoders,
    ILogger<RegistryOutboundEncoder> logger) : IOutboundEncoder
{
    public byte[] Encode(NetworkConnectionContext connection, IReadOnlyList<IServerNetworkMessage> messages)
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
            {
                if (frame is null || frame.Length > DnfFrameProtocol.MaximumFrameLength)
                    throw new InvalidDataException("An encoded batch frame exceeds the transport limit or is unavailable.");
                if (logger.IsEnabled(LogLevel.Debug) && connection.EndpointKind == NetworkEndpointKind.Game
                    && frame.Length >= 3)
                    logger.LogDebug("S->C {ConnectionId} selector=0x{Selector:X2} type=0x{Type:X4} frame={Length} {Message}",
                        connection.ConnectionId, frame[0], (ushort)(frame[1] | (frame[2] << 8)),
                        frame.Length, messages[i].GetType().Name);
                // Copied before the next message is encoded: an encoder may reuse its buffer.
                buffer.Write(frame);
            }
        }

        return buffer.ToArray();
    }
}
