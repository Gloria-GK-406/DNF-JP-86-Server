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
        if (e.RequestInfo is ChannelFrameRequestInfo frame)
        {
            var message = CreateMessage(frame);
            if (message is null)
            {
                await e.InvokeNext();
                return;
            }

            var connectionId = TouchSocketConnectionRegistry.CreateConnectionId(client, NetworkEndpointKind.Channel);
            var connection = Connections.Get(connectionId).Context;
            await dispatcher.DispatchAsync(
                new NetworkDispatchContext(connection),
                message,
                CancellationToken.None);
        }

        await e.InvokeNext();
    }

    private static IClientNetworkMessage? CreateMessage(ChannelFrameRequestInfo frame)
    {
        var metadata = new ChannelMessageMetadata(
            frame.Classification,
            frame.MessageNumber,
            frame.Checksum,
            frame.Acknowledgement);
        return (ClientMessageType)frame.MessageNumber switch
        {
            ClientMessageType.ChannelScriptVersion => new ChannelScriptVersionRequest(metadata),
            ClientMessageType.ChannelScript => new ChannelScriptRequest(metadata),
            ClientMessageType.ChannelConnect => new ChannelConnectRequest(metadata),
            ClientMessageType.ChannelInfo => new ChannelInfoRequest(metadata),
            _ => null,
        };
    }
}

internal sealed class GameNetworkPlugin(
    TouchSocketConnectionRegistry connections,
    IEnumerable<INetworkConnectionObserver> observers,
    INetworkMessageDispatcher dispatcher)
    : NetworkPluginBase(connections, observers, NetworkEndpointKind.Game), ITcpReceivedPlugin
{
    public async Task OnTcpReceived(ITcpSession client, ReceivedDataEventArgs e)
    {
        if (e.RequestInfo is GameClientFrameRequestInfo frame && CreateMessage(frame) is { } message)
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

    private static IClientNetworkMessage? CreateMessage(GameClientFrameRequestInfo frame)
    {
        if (frame.Command != GameProtocol.Command || frame.Type != GameProtocol.ExitType)
        {
            return null;
        }

        return new GameExitRequest(new GameMessageMetadata(
            frame.Command,
            frame.Type,
            frame.Checksum,
            frame.Sequence));
    }
}
