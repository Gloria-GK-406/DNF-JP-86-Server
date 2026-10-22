using System.Collections.Concurrent;
using Dfo.Server.Data.Networking;
using TouchSocket.Sockets;

namespace Dfo.Server.Networking.Runtime;

internal sealed class TouchSocketConnectionRegistry
{
    private readonly ConcurrentDictionary<NetworkConnectionId, ConnectionEntry> connections = new();
    private readonly object idleGate = new();
    private readonly List<TaskCompletionSource> idleWaiters = [];
    private int closing;

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
            SignalIfIdle();
            return true;
        }

        context = null;
        return false;
    }

    public IReadOnlyList<ConnectionEntry> Snapshot() => [.. connections.Values];

    /// <summary>A close callback has started; it ends with <see cref="EndClosing"/>.</summary>
    public void BeginClosing()
    {
        lock (idleGate) closing++;
    }

    public void EndClosing()
    {
        lock (idleGate) closing--;
        SignalIfIdle();
    }

    /// <summary>Completes once no connection is registered and no close callback is in flight.</summary>
    public Task WhenIdleAsync(CancellationToken cancellationToken)
    {
        lock (idleGate)
        {
            if (IsIdle) return Task.CompletedTask;
            var waiter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            idleWaiters.Add(waiter);
            return waiter.Task.WaitAsync(cancellationToken);
        }
    }

    private bool IsIdle => closing == 0 && connections.IsEmpty;

    private void SignalIfIdle()
    {
        TaskCompletionSource[] ready;
        lock (idleGate)
        {
            if (idleWaiters.Count == 0 || !IsIdle) return;
            ready = [.. idleWaiters];
            idleWaiters.Clear();
        }
        foreach (var waiter in ready) waiter.TrySetResult();
    }

    public bool TryGet(NetworkConnectionId id, out ConnectionEntry? entry) => connections.TryGetValue(id, out entry);

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
