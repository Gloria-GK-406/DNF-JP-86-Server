using System.Collections.Concurrent;
using Dfo.Server.Data.Networking;
using TouchSocket.Sockets;

namespace Dfo.Server.Networking.Runtime;

internal sealed class TouchSocketConnectionRegistry
{
    private readonly ConcurrentDictionary<NetworkConnectionId, ConnectionEntry> connections = new();

    public NetworkConnectionContext Add(ITcpSession session, NetworkEndpointKind endpointKind)
    {
        var sessionClient = (ITcpSessionClient)session;
        var connectionId = CreateConnectionId(session, endpointKind);
        var listenerPort = session.LocalEndPoint is System.Net.IPEndPoint endpoint ? endpoint.Port : 0;
        var remoteAddress = session.RemoteEndPoint is System.Net.IPEndPoint remote
            ? remote.Address
            : null;
        var context = new NetworkConnectionContext(
            connectionId, endpointKind, listenerPort, remoteAddress);
        if (!connections.TryAdd(connectionId, new ConnectionEntry(context, sessionClient)))
        {
            throw new InvalidOperationException($"Connection '{connectionId}' is already registered.");
        }

        return context;
    }

    public bool TryRemove(NetworkConnectionId connectionId, out NetworkConnectionContext? context)
    {
        if (connections.TryRemove(connectionId, out var entry))
        {
            context = entry.Context;
            return true;
        }

        context = null;
        return false;
    }

    public ConnectionEntry Get(NetworkConnectionId connectionId) => connections.TryGetValue(connectionId, out var entry)
        ? entry
        : throw new InvalidOperationException($"Connection '{connectionId}' is not active.");

    public static NetworkConnectionId CreateConnectionId(ITcpSession session, NetworkEndpointKind endpointKind)
    {
        var sessionClient = (ITcpSessionClient)session;
        return new NetworkConnectionId($"{endpointKind}:{sessionClient.Id}");
    }

    public sealed record ConnectionEntry(NetworkConnectionContext Context, ITcpSessionClient Session);
}
