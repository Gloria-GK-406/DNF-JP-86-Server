using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;
using Dfo.Server.Networking.Framing;
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
    ChannelMessageDecoderRegistry decoders)
    : NetworkPluginBase(connections, observers, NetworkEndpointKind.Channel), ITcpReceivedPlugin
{
    public async Task OnTcpReceived(ITcpSession client, ReceivedDataEventArgs e)
    {
        if (e.RequestInfo is ChannelFrameRequestInfo frame && Decode(frame) is { } message)
        {
            var connectionId = TouchSocketConnectionRegistry.CreateConnectionId(client, NetworkEndpointKind.Channel);
            var connection = Connections.Get(connectionId).Context;
            await dispatcher.DispatchAsync(
                new NetworkDispatchContext(connection),
                message,
                CancellationToken.None);
        }

        await e.InvokeNext();
    }

    private IClientNetworkMessage? Decode(ChannelFrameRequestInfo frame)
    {
        var decoder = decoders.Find(frame.MessageNumber);
        return decoder?.Decode(
            new ChannelMessageMetadata(
                frame.Classification,
                frame.MessageNumber,
                frame.Checksum,
                frame.Acknowledgement),
            frame.Body.Span);
    }
}

internal sealed class GameNetworkPlugin(
    TouchSocketConnectionRegistry connections,
    IEnumerable<INetworkConnectionObserver> observers,
    INetworkMessageDispatcher dispatcher,
    GameMessageDecoderRegistry decoders)
    : NetworkPluginBase(connections, observers, NetworkEndpointKind.Game), ITcpReceivedPlugin
{
    public async Task OnTcpReceived(ITcpSession client, ReceivedDataEventArgs e)
    {
        if (e.RequestInfo is GameClientFrameRequestInfo frame && Decode(frame) is { } message)
        {
            var connectionId = TouchSocketConnectionRegistry.CreateConnectionId(client, NetworkEndpointKind.Game);
            var connection = Connections.Get(connectionId).Context;
            await dispatcher.DispatchAsync(
                new NetworkDispatchContext(connection),
                message,
                CancellationToken.None);
        }

        await e.InvokeNext();
    }

    /// <remarks>
    /// The command byte is no longer checked here. Every Game decoder registers under command
    /// <c>0x01</c>, so a frame carrying any other command cannot match a
    /// registered wire key and is dropped by the lookup itself -- the same observable outcome the
    /// explicit check produced.
    /// </remarks>
    private IClientNetworkMessage? Decode(GameClientFrameRequestInfo frame)
    {
        var decoder = decoders.Find(frame.Command, frame.Type);
        return decoder?.Decode(
            new GameMessageMetadata(
                frame.Command,
                frame.Type,
                frame.Checksum,
                frame.Sequence),
            frame.Body.Span);
    }
}
