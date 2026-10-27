using System.Collections.Concurrent;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Tests.Outbox;

// The parts XoDelivery and RecordedOutbound share (TASK-25 §3): one implementation each, in a file every test
// project that observes the real registration links, with nothing from Dfo.Server.Networking.

internal sealed class XoSubmissions(IOutboundSender inner) : IOutboundSender
{
    private readonly ConcurrentQueue<OutboundEntry> entries = new();

    public IReadOnlyList<OutboundEntry> Entries => [.. entries];

    public void Submit(IReadOnlyList<OutboundEntry> submitted)
    {
        foreach (var entry in submitted) entries.Enqueue(entry);
        inner.Submit(submitted);
    }
}

internal sealed class XoConnections : INetworkConnectionContexts
{
    private readonly ConcurrentDictionary<NetworkConnectionId, NetworkConnectionContext> contexts = new();

    /// <summary>
    /// Every connection asked about is registered (as a Game connection) the first time, the way a "recorded as sent"
    /// double took a send to any connection. A connection that is no longer there is then never dropped (F-83).
    /// </summary>
    public bool AddOnFirstSight { get; set; }

    public NetworkConnectionContext Add(NetworkConnectionId id) =>
        contexts.GetOrAdd(id, value => new NetworkConnectionContext(value, NetworkEndpointKind.Game, 10011));

    public NetworkConnectionContext Add(NetworkConnectionContext context) => contexts.GetOrAdd(context.ConnectionId, context);

    /// <summary>Unregisters <paramref name="id"/>, as a closed connection is.</summary>
    public void Remove(NetworkConnectionId id) => contexts.TryRemove(id, out _);

    public bool TryGet(NetworkConnectionId connectionId, out NetworkConnectionContext? connection)
    {
        if (contexts.TryGetValue(connectionId, out connection))
            return true;
        if (!AddOnFirstSight)
            return false;
        connection = Add(connectionId);
        return true;
    }
}

/// <summary>
/// The registration point refusing chosen messages, as an unencodable message or an endpoint mismatch does in
/// production: the call that registers it throws, and nothing of that call is registered.
/// </summary>
internal sealed class FaultingEncoder(IOutboundMessageEncoder inner) : IOutboundMessageEncoder
{
    private readonly ConcurrentQueue<(NetworkConnectionId? Connection, Func<IServerNetworkMessage, bool> Matches, Func<Exception> Error)> faults = new();
    private readonly ConcurrentQueue<Func<NetworkConnectionContext, IServerNetworkMessage, Exception?>> decided = new();

    public FaultingEncoder()
        : this(new NameEncoder())
    {
    }

    /// <summary>Every message of type <typeparamref name="TMessage"/> for <paramref name="connection"/> fails to encode.</summary>
    public void Fail<TMessage>(NetworkConnectionId connection, Func<Exception>? error = null)
        where TMessage : IServerNetworkMessage =>
        Fail(connection, message => message is TMessage, error);

    public void Fail(NetworkConnectionId connection, Func<IServerNetworkMessage, bool> matches, Func<Exception>? error = null) =>
        faults.Enqueue((connection, matches, error ?? (() => new InvalidDataException("Injected encoding failure."))));

    /// <summary>Every matching message fails to encode, whichever connection it is for.</summary>
    public void FailAny(Func<IServerNetworkMessage, bool> matches, Func<Exception>? error = null) =>
        faults.Enqueue((null, matches, error ?? (() => new InvalidDataException("Injected encoding failure."))));

    /// <summary>A message fails to encode whenever <paramref name="fault"/> answers an exception for it.</summary>
    public void Fail(Func<NetworkConnectionContext, IServerNetworkMessage, Exception?> fault) => decided.Enqueue(fault);

    public byte[] EncodeMessage(NetworkConnectionContext connection, IServerNetworkMessage message)
    {
        Check(connection, message);
        return inner.EncodeMessage(connection, message);
    }

    public byte[] EncodeBatch(NetworkConnectionContext connection, IReadOnlyList<IServerNetworkMessage> messages)
    {
        foreach (var message in messages) Check(connection, message);
        return inner.EncodeBatch(connection, messages);
    }

    private void Check(NetworkConnectionContext connection, IServerNetworkMessage message)
    {
        foreach (var fault in faults)
            if ((fault.Connection is null || fault.Connection == connection.ConnectionId) && fault.Matches(message))
                throw fault.Error();
        foreach (var fault in decided)
            if (fault(connection, message) is { } error)
                throw error;
    }
}

/// <summary>Any message, as its type name.</summary>
internal sealed class NameEncoder : IOutboundMessageEncoder
{
    public byte[] EncodeMessage(NetworkConnectionContext connection, IServerNetworkMessage message) =>
        System.Text.Encoding.UTF8.GetBytes(message.GetType().Name);

    public byte[] EncodeBatch(NetworkConnectionContext connection, IReadOnlyList<IServerNetworkMessage> messages) =>
        [.. messages.SelectMany(message => EncodeMessage(connection, message))];
}
