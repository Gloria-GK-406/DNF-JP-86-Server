using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;
using Dfo.Server.Networking.Framing;
using Microsoft.Extensions.Logging;
using TouchSocket.Core;
using TouchSocket.Sockets;

namespace Dfo.Server.Networking.Runtime;

internal abstract class NetworkPluginBase(
    TouchSocketConnectionRegistry connections,
    IEnumerable<INetworkConnectionObserver> observers,
    NetworkEndpointKind endpointKind) : PluginBase, ITcpConnectedPlugin, ITcpClosedPlugin
{
    protected TouchSocketConnectionRegistry Connections { get; } = connections;

    protected NetworkEndpointKind EndpointKind { get; } = endpointKind;

    public async Task OnTcpConnected(ITcpSession client, ConnectedEventArgs e)
    {
        var context = Connections.Add(client, EndpointKind);
        foreach (var observer in observers)
        {
            await observer.ConnectedAsync(context, CancellationToken.None);
        }

        await e.InvokeNext();
    }

    public async Task OnTcpClosed(ITcpSession client, ClosedEventArgs e)
    {
        var connectionId = TouchSocketConnectionRegistry.CreateConnectionId(client, EndpointKind);
        if (Connections.TryRemove(connectionId, out var context))
        {
            foreach (var observer in observers)
            {
                await observer.DisconnectedAsync(context!, CancellationToken.None);
            }
        }

        await e.InvokeNext();
    }
}

internal sealed class ChannelNetworkPlugin(
    TouchSocketConnectionRegistry connections,
    IEnumerable<INetworkConnectionObserver> observers,
    INetworkMessageDispatcher dispatcher,
    ChannelMessageDecoderRegistry decoders,
    ILogger<ChannelNetworkPlugin> logger)
    : NetworkPluginBase(connections, observers, NetworkEndpointKind.Channel), ITcpReceivedPlugin
{
    public async Task OnTcpReceived(ITcpSession client, ReceivedDataEventArgs e)
    {
        if (e.RequestInfo is ChannelFrameRequestInfo frame)
        {
            var connectionId = TouchSocketConnectionRegistry.CreateConnectionId(client, NetworkEndpointKind.Channel);
            if (Decode(frame, connectionId) is { } message)
            {
                var connection = Connections.Get(connectionId).Context;
                await dispatcher.DispatchAsync(
                    new NetworkDispatchContext(connection),
                    message,
                    CancellationToken.None);
            }
        }

        await e.InvokeNext();
    }

    private IClientNetworkMessage? Decode(ChannelFrameRequestInfo frame, NetworkConnectionId connectionId)
    {
        var decoder = decoders.Find(frame.MessageNumber);
        if (decoder is null)
        {
            logger.LogWarning(
                "Unhandled Channel frame {ConnectionId}: message=0x{MessageNumber:X2} " +
                "classification=0x{Classification:X2} body({BodyLength}B)={Body}",
                connectionId,
                frame.MessageNumber,
                frame.Classification,
                frame.Body.Length,
                Convert.ToHexString(frame.Body.Span));
            return null;
        }

        var message = decoder.Decode(
            new ChannelMessageMetadata(
                frame.Classification,
                frame.MessageNumber,
                frame.Checksum,
                frame.Acknowledgement),
            frame.Body.Span);

        if (message is null)
        {
            logger.LogWarning(
                "Channel decoder {Decoder} declined {ConnectionId}: message=0x{MessageNumber:X2} " +
                "body({BodyLength}B); body withheld",
                decoder.GetType().Name,
                connectionId,
                frame.MessageNumber,
                frame.Body.Length);
        }

        return message;
    }
}

internal sealed class GameNetworkPlugin(
    TouchSocketConnectionRegistry connections,
    IEnumerable<INetworkConnectionObserver> observers,
    INetworkMessageDispatcher dispatcher,
    GameMessageDecoderRegistry decoders,
    ILogger<GameNetworkPlugin> logger)
    : NetworkPluginBase(connections, observers, NetworkEndpointKind.Game), ITcpReceivedPlugin
{
    public async Task OnTcpReceived(ITcpSession client, ReceivedDataEventArgs e)
    {
        if (e.RequestInfo is GameClientFrameRequestInfo frame)
        {
            var connectionId = TouchSocketConnectionRegistry.CreateConnectionId(client, NetworkEndpointKind.Game);
            if (Decode(frame, connectionId) is { } message)
            {
                var connection = Connections.Get(connectionId).Context;
                await dispatcher.DispatchAsync(
                    new NetworkDispatchContext(connection),
                    message,
                    CancellationToken.None);
            }
        }

        await e.InvokeNext();
    }

    /// <remarks>
    /// The first-byte selector is not interpreted here. It is part of the wire key, so a frame whose
    /// selector no decoder registered under cannot match and is dropped by the lookup itself. Its
    /// protocol meaning is stated where the decoders are registered.
    ///
    /// The two ways a frame reaches no handler are logged apart, because they mean opposite things.
    /// An unregistered wire key is an ordinary fact about traffic -- the client sends protocols this
    /// server has not implemented yet -- and its body is dumped, since recovering that body is the
    /// whole point of noticing. A registered decoder returning null is a server-side defect instead,
    /// and its body is withheld: the decoders that can decline include ones whose bodies carry
    /// account credentials, and a decode failure must not turn into a credential dump. Naming the
    /// decoder identifies the defect without printing what it failed to read.
    /// </remarks>
    private IClientNetworkMessage? Decode(GameClientFrameRequestInfo frame, NetworkConnectionId connectionId)
    {
        var decoder = decoders.Find(frame.Selector, frame.Type);
        if (decoder is null)
        {
            logger.LogWarning(
                "Unhandled Game frame {ConnectionId}: selector=0x{Selector:X2} type=0x{Type:X4} " +
                "seq={Sequence} body({BodyLength}B)={Body}",
                connectionId,
                frame.Selector,
                frame.Type,
                frame.Sequence,
                frame.Body.Length,
                Convert.ToHexString(frame.Body.Span));
            return null;
        }

        var message = decoder.Decode(
            new GameMessageMetadata(
                frame.Selector,
                frame.Type,
                frame.Checksum,
                frame.Sequence),
            frame.Body.Span);

        if (message is null)
        {
            logger.LogWarning(
                "Game decoder {Decoder} declined {ConnectionId}: selector=0x{Selector:X2} " +
                "type=0x{Type:X4} body({BodyLength}B); body withheld",
                decoder.GetType().Name,
                connectionId,
                frame.Selector,
                frame.Type,
                frame.Body.Length);
        }

        return message;
    }
}
