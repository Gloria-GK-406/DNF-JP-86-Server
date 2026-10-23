using System.Collections.Concurrent;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Application.Execution;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Networking.Outbound;
using Dfo.Server.Protocol.Game;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dfo.Server.Protocol.Tests.Outbox;

/// <summary>
/// The sending side of spec appendix D's evidence (TASK-23 §7): a production path runs as a job with the
/// real registration adapters, the real outbox and its ordered groups, and the real outbound sender over a
/// transport whose connections can be slowed or failed one by one. What is asserted is the order in which
/// entries are delivered, not the order they arrive on a wire (appendix D "口径").
/// </summary>
/// <remarks>
/// TASK-24 (appendix E evidence): <see cref="Encoder"/> can refuse chosen messages at the registration point, the
/// non-transport failure a kept branch still sees; <c>configure</c> adds the real event service and notification
/// bus, or anything else a production path needs, to the same container.
/// </remarks>
internal sealed class XoDelivery : IAsyncDisposable
{
    private readonly ServiceProvider provider;
    private readonly XoConnections connections = new();
    private readonly OutboundSender sender;

    public XoDelivery(OutboundSendOptions? options = null, Action<IServiceCollection>? configure = null)
    {
        sender = new OutboundSender(Transport, [Observer], NullLogger<OutboundSender>.Instance,
            options ?? OutboundSendOptions.Default);
        Recorder = new XoSubmissions(sender);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<INetworkConnectionContexts>(connections);
        services.AddSingleton<IOutboundMessageEncoder>(Encoder);
        services.AddSingleton<IOutboundSender>(Recorder);
        services.AddGameplayExecution();
        configure?.Invoke(services);
        provider = services.BuildServiceProvider();
        // The protocol's CMD/NOTI sender over the registration adapter; created by name because it is internal to
        // Dfo.Server.Protocol and this file is linked into test projects that cannot see it.
        Game = (IGameMessageSender)Activator.CreateInstance(
            typeof(Dfo.Server.Protocol.ProtocolModule).Assembly.GetType("Dfo.Server.Protocol.Game.GameMessageSender", throwOnError: true)!,
            Network)!;
        Projections = new XoProjections(Network);
    }

    public XoTransport Transport { get; } = new();

    /// <summary>The registration point's encoder: every message by name, except the ones told to fail.</summary>
    public FaultingEncoder Encoder { get; } = new();

    public IServiceProvider Services => provider;

    /// <summary>A registered connection's context, as the registration adapters see it.</summary>
    public NetworkConnectionContext Context(NetworkConnectionId connection) => connections.Add(connection);

    /// <summary>Whether a close was registered for <paramref name="connection"/> (not whether the sender closed it).</summary>
    public bool CloseRegistered(NetworkConnectionId connection) =>
        Recorder.Entries.Any(entry => entry.IsClose && entry.Connection.ConnectionId == connection);

    /// <summary>The messages registered for <paramref name="connection"/>, in registration order.</summary>
    public IReadOnlyList<IServerNetworkMessage> Messages(NetworkConnectionId connection) =>
        [.. Recorder.Entries.Where(entry => !entry.IsClose && entry.Connection.ConnectionId == connection)
            .SelectMany(entry => entry.Messages)];

    public XoObserver Observer { get; } = new();

    public XoSubmissions Recorder { get; }

    public INetworkMessageSender Network => provider.GetRequiredService<INetworkMessageSender>();

    public IGameMessageSender Game { get; }

    public INetworkConnectionCloser Closer => provider.GetRequiredService<INetworkConnectionCloser>();

    public IOutboundOrdering Ordering => provider.GetRequiredService<IOutboundOrdering>();

    /// <summary>An immediate projection that registers, per recipient, one message naming the notification.</summary>
    public XoProjections Projections { get; }

    public void Connect(params SelectedSessionIdentity[] sessions)
    {
        foreach (var session in sessions) Connect(session.ConnectionId);
    }

    public void Connect(NetworkConnectionId connectionId) => Transport.Add(connections.Add(connectionId));

    /// <summary>Registers this exact context, so an exact close of it is carried out.</summary>
    public void Connect(NetworkConnectionContext context) => Transport.Add(connections.Add(context));

    /// <summary>The body as one job; its outbox is handed to the sender when it ends.</summary>
    public Task RunAsync(Func<Task> body) =>
        provider.GetRequiredService<GameplayExecutor>().RunAsync(new GameplayWork(
            GameplayShardKey.World,
            GameplayCallChain.NewRoot(GameplayCallSource.Connection),
            "test:xo",
            async _ => await body()), CancellationToken.None).AsTask();

    /// <summary>
    /// The body as one inbound job from <paramref name="source"/>, which opens its outbox first as the dispatchers
    /// do: a job that fails closes its source after what the outbox kept (RQ-031(c)).
    /// </summary>
    public Task RunFromAsync(NetworkConnectionId source, Func<Task> body) =>
        provider.GetRequiredService<GameplayExecutor>().RunAsync(new GameplayWork(
            GameplayShardKey.World,
            GameplayCallChain.NewRoot(GameplayCallSource.Connection),
            "test:xo",
            async _ =>
            {
                GC.KeepAlive(provider.GetRequiredService<Dfo.Server.Application.Execution.Outbox.GameplayOutboxes>().Current);
                await body();
            })
        {
            Source = connections.Add(source),
        }, CancellationToken.None).AsTask();

    /// <summary>The body as one job that returns a value.</summary>
    public Task<T> RunAsync<T>(Func<Task<T>> body) =>
        provider.GetRequiredService<GameplayExecutor>().RunAsync(new GameplayWork<T>(
            GameplayShardKey.World,
            GameplayCallChain.NewRoot(GameplayCallSource.Connection),
            "test:xo",
            async _ => await body()), CancellationToken.None).AsTask();

    /// <summary>Waits until every submitted entry has an outcome.</summary>
    public async Task SettledAsync()
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (Recorder.Entries.Any(entry => Observer.OutcomeOf(entry) is null))
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("The XO entries did not settle.");
            await Task.Delay(5);
        }
    }

    /// <summary>
    /// Waits until the sender has closed <paramref name="connection"/>. A failed entry is reported before the sender's
    /// detached close of its connection runs, so a close is read through this rather than right after SettledAsync.
    /// </summary>
    public async Task<bool> ClosedAsync(NetworkConnectionId connection)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!Transport.IsClosed(connection))
        {
            if (DateTime.UtcNow > deadline)
                return false;
            await Task.Delay(5);
        }
        return true;
    }

    /// <summary>Position of <paramref name="connection"/>'s first delivered batch among all deliveries, or -1.</summary>
    public int Delivered(NetworkConnectionId connection) => Transport.Delivered.ToList().IndexOf(connection.Value);

    /// <summary>The outcomes of the batches registered for <paramref name="connection"/>, in registration order.</summary>
    public IReadOnlyList<OutboundOutcome?> Outcomes(NetworkConnectionId connection) =>
        [.. Recorder.Entries.Where(entry => !entry.IsClose && entry.Connection.ConnectionId == connection).Select(Observer.OutcomeOf)];

    public async ValueTask DisposeAsync()
    {
        await sender.DisposeAsync();
        await provider.DisposeAsync();
    }
}

internal sealed class XoObserver : IOutboundDeliveryObserver
{
    private readonly ConcurrentDictionary<OutboundEntry, OutboundOutcome> outcomes = new(ReferenceEqualityComparer.Instance);

    public OutboundOutcome? OutcomeOf(OutboundEntry entry) => outcomes.TryGetValue(entry, out var outcome) ? outcome : null;

    public void OnEntryCompleted(OutboundDeliveryReport report) => outcomes[report.Entry] = report.Outcome;

    public void OnConnectionFailed(NetworkConnectionContext connection, OutboundOutcome reason)
    {
    }
}

/// <summary>Connections that deliver at once unless told to wait, never finish, or fail.</summary>
internal sealed class XoTransport : IOutboundTransport
{
    private readonly ConcurrentDictionary<NetworkConnectionContext, Connection> live = new(ReferenceEqualityComparer.Instance);
    private readonly ConcurrentDictionary<string, Func<CancellationToken, Task>> gates = new();
    private readonly ConcurrentDictionary<string, bool> failing = new();

    /// <summary>Connection IDs in the order their batches were delivered, one line per batch.</summary>
    public ConcurrentQueue<string> Delivered { get; } = new();

    public void Add(NetworkConnectionContext context) => live[context] = new Connection(this, context.ConnectionId.Value);

    /// <summary>Each send to the connection takes this long first.</summary>
    public void Slow(NetworkConnectionId connection, TimeSpan delay) =>
        gates[connection.Value] = token => Task.Delay(delay, token);

    /// <summary>Each send to the connection never finishes by itself.</summary>
    public void Stall(NetworkConnectionId connection) =>
        gates[connection.Value] = token => Task.Delay(Timeout.Infinite, token);

    public void Fail(NetworkConnectionId connection) => failing[connection.Value] = true;

    public bool IsClosed(NetworkConnectionId connection) =>
        live.Values.Any(value => value.Id == connection.Value && value.Closed);

    public IOutboundConnection? Resolve(NetworkConnectionContext expected) =>
        live.TryGetValue(expected, out var connection) && !connection.Closed ? connection : null;

    private sealed class Connection(XoTransport owner, string id) : IOutboundConnection
    {
        public string Id { get; } = id;

        public bool Closed { get; private set; }

        public async ValueTask SendAsync(ReadOnlyMemory<byte> frames, CancellationToken cancellationToken)
        {
            if (owner.gates.TryGetValue(Id, out var gate))
                await gate(cancellationToken);
            if (owner.failing.ContainsKey(Id))
                throw new IOException($"Connection '{Id}' went away.");
            owner.Delivered.Enqueue(Id);
        }

        public ValueTask CloseAsync()
        {
            Closed = true;
            return ValueTask.CompletedTask;
        }
    }
}

/// <summary>A projected notification, as the message registered for one recipient.</summary>
internal sealed record XoProjected(string Notification) : IGameNotiMessage
{
    public Dfo.Server.Data.Networking.ServerMessageType MessageType => default;
}

/// <summary>
/// Immediate projections the way the notification bus makes them, without its composers: every exact
/// session in the audience gets one message registered through the real sender, each its own batch.
/// </summary>
internal sealed class XoProjections(INetworkMessageSender sender) : Dfo.Server.Testing.ProjectionEventService
{
    public ConcurrentQueue<(NotificationAudience Audience, object Notification)> Published { get; } = new();

    public override async ValueTask<NotificationDelivery> ProjectAsync<TNotification>(
        NotificationAudience audience, TNotification notification, CancellationToken cancellationToken = default)
    {
        Published.Enqueue((audience, notification));
        var sessions = audience is NotificationAudience.TheseSessions exact ? exact.Sessions : [];
        var attempts = new List<NotificationAttempt>();
        foreach (var session in sessions)
        {
            await sender.SendAsync(session.ConnectionId, new XoProjected(typeof(TNotification).Name), cancellationToken);
            attempts.Add(new NotificationAttempt(session.CharacterId, NotificationOutcome.Delivered));
        }
        return new NotificationDelivery(attempts);
    }
}
