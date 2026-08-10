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
    INetworkMessageDispatcher dispatcher)
    : NetworkPluginBase(connections, observers, NetworkEndpointKind.Channel), ITcpReceivedPlugin
{
    public async Task OnTcpReceived(ITcpSession client, ReceivedDataEventArgs e)
    {
        if (e.RequestInfo is ChannelFrameRequestInfo frame && frame.MessageNumber == 0x0B)
        {
            var connectionId = TouchSocketConnectionRegistry.CreateConnectionId(client, NetworkEndpointKind.Channel);
            var connection = Connections.Get(connectionId).Context;
            var metadata = new ChannelMessageMetadata(
                frame.Classification,
                frame.MessageNumber,
                frame.Checksum,
                frame.Acknowledgement);
            await dispatcher.DispatchAsync(
                new NetworkDispatchContext(connection),
                new ChannelConnectRequest(metadata),
                CancellationToken.None);
        }

        await e.InvokeNext();
    }
}

internal sealed class GameNetworkPlugin(
    TouchSocketConnectionRegistry connections,
    IEnumerable<INetworkConnectionObserver> observers)
    : NetworkPluginBase(connections, observers, NetworkEndpointKind.Game);
