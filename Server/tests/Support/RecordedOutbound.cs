using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Execution.Outbox;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Tests.Outbox;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dfo.Server.Testing;

/// <summary>
/// What a test sees of its sends, through the real registration (TASK-25 §3): the real registration adapter,
/// connection closer and outbox register every send and close of a job, and the entries are recorded where the
/// outbox hands them to the sender. A test reads them through the members the old "recorded as sent" doubles had.
/// </summary>
/// <remarks>
/// <para>
/// Read inside the job that registered them, the entries still in that job's outbox are included (minus those of a
/// unit that was rolled back), so a read right after the call sees what the old double saw. A read after the job
/// sees only what the outbox handed over: that is where a rollback (RQ-031(b)) and the end-of-job flush
/// (RQ-031(a)) become visible.
/// </para>
/// <para>
/// Failures are injected only at the registration point's encoder (<see cref="Encoder"/>), so the call that
/// registers a refused message throws and registers nothing. The callbacks (<see cref="BeforeSend"/> and the
/// others) run at the registration point too, in the calling job, before the adapter encodes.
/// </para>
/// </remarks>
internal sealed partial class RecordedOutbound
{
    private static readonly FieldInfo Slots = typeof(GameplayOutbox).GetField("slots", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new MissingFieldException(nameof(GameplayOutbox), "slots");

    private readonly AsyncLocal<object?> inFlight = new();
    private readonly ConcurrentQueue<BatchCall> batchCalls = new();
    private readonly ConcurrentQueue<ExplicitCall> explicitCalls = new();
    private readonly ConcurrentDictionary<object, bool> hidden = new(ReferenceEqualityComparer.Instance);
    private readonly RegistrationEncoder registrationEncoder;
    private INetworkMessageSender? network;
    private INetworkConnectionCloser? closer;
    private GameplayOutboxes? outboxes;

    /// <summary>A standalone set: its own outbox registry, adapters and connection table.</summary>
    /// <param name="encoder">The encoder behind the fault injection; message names when omitted.</param>
    public RecordedOutbound(IOutboundMessageEncoder? encoder = null, IOutboundSender? deliver = null)
    {
        registrationEncoder = encoder as RegistrationEncoder ?? new RegistrationEncoder();
        Encoder = new FaultingEncoder(encoder ?? new NameEncoder());
        Recorder = new XoSubmissions(deliver ?? new Discard());
        Connections.AddOnFirstSight = true;
        Bind(new GameplayOutboxes(Recorder, NullLogger<GameplayOutbox>.Instance), NullLoggerFactory.Instance);
    }

    private RecordedOutbound(bool registered)
    {
        registrationEncoder = new RegistrationEncoder();
        Encoder = new FaultingEncoder(registrationEncoder);
        Recorder = new XoSubmissions(new Discard());
        Connections.AddOnFirstSight = true;
    }

    /// <summary>
    /// Replaces the container's sending side with the real adapters over this recorder: the protocol's own
    /// encoders encode at the registration point, every connection is known on first sight.
    /// </summary>
    public static RecordedOutbound AddTo(IServiceCollection services)
    {
        var recorded = new RecordedOutbound(registered: true);
        services.AddSingleton(provider => recorded.Bound(provider));
        services.AddSingleton(recorded.Connections);
        services.AddSingleton<INetworkConnectionContexts>(recorded.Connections);
        services.AddSingleton<IOutboundSender>(recorded.Recorder);
        services.AddSingleton<IOutboundMessageEncoder>(provider =>
        {
            recorded.registrationEncoder.Use(provider);
            return recorded.Encoder;
        });
        services.AddSingleton(provider => new GameplayOutboxes(
            provider.GetRequiredService<IOutboundSender>(), provider.GetRequiredService<ILogger<GameplayOutbox>>()));
        services.AddSingleton<INetworkMessageSender>(provider => recorded.Bound(provider).network!);
        services.AddSingleton<ICommandMessageOutbox>(provider => (ICommandMessageOutbox)recorded.Bound(provider).network!);
        services.AddSingleton<INetworkConnectionCloser>(provider => recorded.Bound(provider).closer!);
        return recorded;
    }

    public FaultingEncoder Encoder { get; }

    public XoConnections Connections { get; } = new();

    /// <summary>The entries the outbox handed to the sender, in hand-off order.</summary>
    public XoSubmissions Recorder { get; }

    public GameplayOutboxes Outboxes => outboxes ?? throw new InvalidOperationException("The container has not been built.");

    /// <summary>The registration adapter, wrapped only to run the callbacks and note the kind of each call.</summary>
    public INetworkMessageSender Network => network ?? throw new InvalidOperationException("The container has not been built.");

    public INetworkConnectionCloser Closer => closer ?? throw new InvalidOperationException("The container has not been built.");

    /// <summary>Runs at the registration point of every single send and prepared batch, before it is encoded.</summary>
    public Action? BeforeSend { get; set; }

    /// <summary>Runs at the registration point of every explicit delivery, before it is encoded.</summary>
    public Action? BeforeExplicit { get; set; }

    /// <summary>
    /// Calls that reached the registration point to register a single send or a prepared batch (explicit deliveries
    /// are not counted). Since F-102 a notification registers each recipient's messages as one prepared batch, so a
    /// recipient counts once however many messages he is sent.
    /// </summary>
    public int Attempts => attempts;

    private int attempts;

    /// <summary>
    /// The registered entries a test can see now: those handed to the sender and, inside a job, those still in its
    /// outbox; without the ones hidden by <see cref="Clear"/>.
    /// </summary>
    public IReadOnlyList<OutboundEntry> Entries
    {
        get
        {
            var visible = new List<OutboundEntry>();
            foreach (var entry in Recorder.Entries.Concat(Pending()))
            {
                if (!hidden.ContainsKey(Key(entry)))
                    visible.Add(entry);
            }
            return visible;
        }
    }

    /// <summary>Registered messages in registration order, close requests left out.</summary>
    public RecordedMessages<IServerNetworkMessage> Messages => new(this);

    /// <summary><see cref="Messages"/> typed as <typeparamref name="TMessage"/>, for a double whose list was typed so.</summary>
    public RecordedMessages<TMessage> MessagesAs<TMessage>() where TMessage : IServerNetworkMessage => new(this);

    /// <summary>The messages of <see cref="Calls"/>: an explicit delivery's targets left out, as some old doubles kept only its direct messages.</summary>
    public RecordedMessages<IServerNetworkMessage> CallMessages => new(this, callsOnly: true);

    /// <summary>The registered entries that came from prepared batches, with their frames message by message.</summary>
    public RecordedBatches Batches => new(this);

    /// <summary>Connections whose close was registered, in registration order.</summary>
    public IReadOnlyList<NetworkConnectionId> Closes =>
        [.. Entries.Where(entry => entry.IsClose).Select(entry => entry.Connection.ConnectionId)];

    /// <summary>Every registered message's frames, in registration order (close requests have none).</summary>
    public IReadOnlyList<byte[]> Frames => [.. Entries.Where(entry => !entry.IsClose).SelectMany(FramesOf)];

    /// <summary>Messages registered for one connection.</summary>
    public IReadOnlyList<IServerNetworkMessage> MessagesFor(NetworkConnectionId connection) =>
        [.. Entries.Where(entry => !entry.IsClose && entry.Connection.ConnectionId == connection).SelectMany(entry => entry.Messages)];

    /// <summary>
    /// The explicit deliveries that were registered, each rebuilt from the entries its call registered: the direct
    /// messages as their Required batch, each target that was registered as its BestEffort batch. A call none of whose
    /// entries can be seen any more (rolled back, or hidden by <see cref="Clear"/>) is left out.
    /// </summary>
    public IReadOnlyList<(NetworkConnectionId Direct, ExplicitMessageDelivery Delivery)> Explicits
    {
        get
        {
            var visible = Entries.Select(Key).ToHashSet(ReferenceEqualityComparer.Instance);
            var result = new List<(NetworkConnectionId, ExplicitMessageDelivery)>();
            foreach (var call in explicitCalls)
            {
                if (hidden.ContainsKey(call) || !call.Registered.All(entry => visible.Contains(Key(entry))))
                    continue;
                var required = call.Registered.FirstOrDefault(entry => entry.Delivery == OutboundDelivery.Required
                    && entry.Connection.ConnectionId == call.Direct);
                result.Add((call.Direct, new ExplicitMessageDelivery(
                    required?.Messages ?? [],
                    [.. call.Registered.Where(entry => !ReferenceEquals(entry, required))
                        .Select(entry => new ExplicitMessageTarget(entry.Connection.ConnectionId, entry.Messages))])));
            }
            return result;
        }
    }

    /// <summary>
    /// The registered entries call by call, as the old doubles saw a call: a single send or prepared batch is its own
    /// entry, an explicit delivery is its direct messages' entry; the targets of an explicit delivery are left out.
    /// </summary>
    public IReadOnlyList<OutboundEntry> Calls
    {
        get
        {
            var targets = explicitCalls.SelectMany(call => call.Registered.Where(entry =>
                    !(entry.Delivery == OutboundDelivery.Required && entry.Connection.ConnectionId == call.Direct)))
                .Select(Key).ToHashSet(ReferenceEqualityComparer.Instance);
            return [.. Entries.Where(entry => !entry.IsClose && !targets.Contains(Key(entry)))];
        }
    }

    /// <summary>
    /// <see cref="Calls"/> as delivery plans, in registration order: an explicit delivery rebuilt as in
    /// <see cref="Explicits"/>, a single send or prepared batch as a plan of its messages with no targets.
    /// </summary>
    public IReadOnlyList<(NetworkConnectionId Direct, ExplicitMessageDelivery Delivery)> CallDeliveries
    {
        get
        {
            var explicits = Explicits;
            var result = new List<(NetworkConnectionId, ExplicitMessageDelivery)>();
            foreach (var entry in Calls)
            {
                var call = explicits.FirstOrDefault(value => ReferenceEquals(value.Delivery.RequiredDirectMessages, entry.Messages));
                result.Add(call.Delivery is not null
                    ? call
                    : (entry.Connection.ConnectionId, new ExplicitMessageDelivery(entry.Messages, [])));
            }
            return result;
        }
    }

    /// <summary>Whether <paramref name="message"/>, now being encoded, is a direct message: of a single send, a batch, or an explicit delivery's direct part.</summary>
    public bool IsDirect(IServerNetworkMessage message) =>
        ExplicitInFlight is not { } delivery || delivery.RequiredDirectMessages.Any(direct => ReferenceEquals(direct, message));

    /// <summary>Hides everything visible now, as the old doubles' <c>Clear()</c> forgot what they had recorded.</summary>
    public void Clear()
    {
        foreach (var entry in Entries) hidden[Key(entry)] = true;
        foreach (var call in explicitCalls) hidden[call] = true;
    }

    /// <summary>Every message for <paramref name="connection"/> matching <paramref name="matches"/> fails to encode.</summary>
    public void Fail(NetworkConnectionId connection, Func<IServerNetworkMessage, bool> matches, Func<Exception> error) =>
        Encoder.Fail(connection, matches, error);

    /// <summary>Every message for any connection matching <paramref name="matches"/> fails to encode.</summary>
    public void FailAny(Func<IServerNetworkMessage, bool> matches, Func<Exception> error) =>
        Encoder.FailAny(matches, error);

    /// <summary>
    /// The direct messages of every explicit delivery whose direct recipient is <paramref name="connection"/> fail to
    /// encode where they are registered: the call throws and registers nothing.
    /// </summary>
    public void FailDirect(NetworkConnectionId connection, Func<Exception> error) =>
        Encoder.Fail(connection, message => ExplicitInFlight is { } delivery
            && delivery.RequiredDirectMessages.Any(direct => ReferenceEquals(direct, message)), error);

    /// <summary>
    /// A single send or prepared batch fails where it is registered, for a test whose old double accepted only
    /// explicit deliveries; the call throws as the old double's refusal did.
    /// </summary>
    public void RefuseOutsideExplicit(Func<Exception>? error = null) =>
        Encoder.FailAny(_ => ExplicitInFlight is null, error ?? (() => new NotSupportedException()));

    /// <summary>The direct messages of an explicit delivery fail where they are registered, for a test whose old double refused one.</summary>
    public void RefuseExplicit(Func<Exception>? error = null) =>
        Encoder.FailAny(_ => ExplicitInFlight is not null, error ?? (() => new NotSupportedException()));

    /// <summary>The explicit delivery being registered by the running call, if that call is one.</summary>
    internal ExplicitMessageDelivery? ExplicitInFlight => inFlight.Value as ExplicitMessageDelivery;

    /// <summary>Whether the running call registers a prepared batch.</summary>
    internal bool BatchInFlight => inFlight.Value is IReadOnlyList<IServerNetworkMessage>;

    /// <summary>The frames each message was encoded to at the registration point, when a protocol encoder made them.</summary>
    internal byte[][] FramesOf(OutboundEntry entry) =>
        registrationEncoder.FramesOf(entry) ?? [entry.Frames.ToArray()];

    internal bool CameFromBatch(OutboundEntry entry) =>
        !entry.IsClose && batchCalls.Any(call => call.Matches(entry));

    private static object Key(OutboundEntry entry) => entry.IsClose ? entry : entry.Messages;

    private IEnumerable<OutboundEntry> Pending()
    {
        if (outboxes is null || !GameplayJob.IsOnExecutor)
            return [];
        GameplayOutbox? outbox;
        try
        {
            outbox = outboxes.CurrentOrNull;
        }
        catch (GameplayOffExecutorException)
        {
            return [];
        }
        if (outbox is null)
            return [];
        var pending = new List<OutboundEntry>();
        foreach (var slot in (IEnumerable)Slots.GetValue(outbox)!)
        {
            var type = slot.GetType();
            if (type.GetProperty("Unit")!.GetValue(slot) is GameplayOutboxUnit { State: GameplayOutboxUnitState.RolledBack or GameplayOutboxUnitState.Uncertain })
                continue;
            if (type.GetProperty("Entry")!.GetValue(slot) is OutboundEntry entry)
                pending.Add(entry);
            else if (type.GetProperty("Composition")!.GetValue(slot) is IOutboxComposition composition)
                pending.AddRange(composition.ComposedEntries);
        }
        return pending;
    }

    private RecordedOutbound Bound(IServiceProvider provider)
    {
        lock (batchCalls)
        {
            if (outboxes is null)
            {
                Bind(provider.GetRequiredService<GameplayOutboxes>(), provider.GetRequiredService<ILoggerFactory>());
                _ = provider.GetRequiredService<IOutboundMessageEncoder>();
            }
        }
        return this;
    }

    private void Bind(GameplayOutboxes registry, ILoggerFactory loggers)
    {
        outboxes = registry;
        network = new Registering(this, new OutboxNetworkMessageSender(
            registry, Connections, Encoder, loggers.CreateLogger<OutboxNetworkMessageSender>()));
        closer = new Closing(Connections, new OutboxConnectionCloser(
            registry, Connections, loggers.CreateLogger<OutboxConnectionCloser>()));
    }

    /// <summary>Forwards every call to the real adapter; it records nothing and throws nothing of its own.</summary>
    private sealed class Registering(RecordedOutbound owner, OutboxNetworkMessageSender inner) : INetworkMessageSender, ICommandMessageOutbox
    {
        public ValueTask<bool> RegisterWithOutcomeAsync(NetworkConnectionId id, IServerNetworkMessage message, CancellationToken ct)
        {
            Interlocked.Increment(ref owner.attempts); owner.BeforeSend?.Invoke();
            return inner.RegisterWithOutcomeAsync(id, message, ct);
        }
        public async ValueTask<bool> RegisterBatchWithOutcomeAsync(NetworkConnectionContext connection, IReadOnlyList<IServerNetworkMessage> messages, CancellationToken ct)
        {
            Interlocked.Increment(ref owner.attempts); owner.BeforeSend?.Invoke(); owner.inFlight.Value = messages;
            bool accepted;
            try { accepted = await inner.RegisterBatchWithOutcomeAsync(connection, messages, ct); }
            finally { owner.inFlight.Value = null; }
            if (messages.Count > 0 && accepted) owner.batchCalls.Enqueue(new BatchCall(connection, [.. messages]));
            return accepted;
        }
        public ValueTask SendAsync(NetworkConnectionId connectionId, IServerNetworkMessage message, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref owner.attempts);
            owner.BeforeSend?.Invoke();
            return inner.SendAsync(connectionId, message, cancellationToken);
        }

        public async ValueTask SendBatchAsync(NetworkConnectionContext expectedConnection,
            IReadOnlyList<IServerNetworkMessage> messages, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref owner.attempts);
            owner.BeforeSend?.Invoke();
            owner.inFlight.Value = messages;
            try
            {
                await inner.SendBatchAsync(expectedConnection, messages, cancellationToken);
            }
            finally
            {
                owner.inFlight.Value = null;
            }
            if (messages.Count > 0)
                owner.batchCalls.Enqueue(new BatchCall(expectedConnection, [.. messages]));
        }

        public async ValueTask SendExplicitAsync(NetworkConnectionId requiredDirectConnectionId, ExplicitMessageDelivery delivery,
            CancellationToken cancellationToken)
        {
            owner.BeforeExplicit?.Invoke();
            var before = owner.Pending().Select(Key).ToHashSet(ReferenceEqualityComparer.Instance);
            owner.inFlight.Value = delivery;
            try
            {
                await inner.SendExplicitAsync(requiredDirectConnectionId, delivery, cancellationToken);
            }
            finally
            {
                owner.inFlight.Value = null;
            }
            owner.explicitCalls.Enqueue(new ExplicitCall(requiredDirectConnectionId,
                [.. owner.Pending().Where(entry => !before.Contains(Key(entry)))]));
        }
        public ValueTask RegisterAsync(NetworkConnectionId id, IServerNetworkMessage message, CancellationToken ct) => SendAsync(id, message, ct);
        public ValueTask RegisterBatchAsync(NetworkConnectionContext connection, IReadOnlyList<IServerNetworkMessage> messages, CancellationToken ct) => SendBatchAsync(connection, messages, ct);
        public ValueTask RegisterExplicitAsync(NetworkConnectionId id, ExplicitMessageDelivery delivery, CancellationToken ct) => SendExplicitAsync(id, delivery, ct);
        public async ValueTask RegisterBatchAsync(NetworkConnectionContext connection, IReadOnlyList<IServerNetworkMessage> messages, ICommandRegistrationAttempt attempt, CancellationToken ct)
        {
            Interlocked.Increment(ref owner.attempts);
            owner.BeforeSend?.Invoke();
            owner.inFlight.Value = messages;
            try { await inner.RegisterBatchAsync(connection, messages, attempt, ct); }
            finally { owner.inFlight.Value = null; }
            if (messages.Count > 0) owner.batchCalls.Enqueue(new BatchCall(connection, [.. messages]));
        }
        public async ValueTask RegisterExplicitAsync(NetworkConnectionId id, ExplicitMessageDelivery delivery, ICommandRegistrationAttempt attempt, CancellationToken ct)
        {
            owner.BeforeExplicit?.Invoke();
            var before = owner.Pending().Select(Key).ToHashSet(ReferenceEqualityComparer.Instance);
            owner.inFlight.Value = delivery;
            try { await inner.RegisterExplicitAsync(id, delivery, attempt, ct); }
            finally { owner.inFlight.Value = null; }
            owner.explicitCalls.Enqueue(new ExplicitCall(id, [.. owner.Pending().Where(entry => !before.Contains(Key(entry)))]));
        }

    }

    /// <summary>
    /// The real closer; an exact close of a connection never seen before registers that context first, as every
    /// connection is known on first sight here.
    /// </summary>
    private sealed class Closing(XoConnections connections, INetworkConnectionCloser inner) : INetworkConnectionCloser
    {
        public ValueTask RequestCloseExactAsync(NetworkConnectionContext expected, CancellationToken cancellationToken)
        {
            if (connections.AddOnFirstSight)
                connections.Add(expected);
            return inner.RequestCloseExactAsync(expected, cancellationToken);
        }

        public ValueTask RequestCloseAsync(NetworkConnectionId connectionId, CancellationToken cancellationToken) =>
            inner.RequestCloseAsync(connectionId, cancellationToken);
    }

    private sealed record BatchCall(NetworkConnectionContext Connection, IServerNetworkMessage[] Messages)
    {
        public bool Matches(OutboundEntry entry) =>
            ReferenceEquals(entry.Connection, Connection)
            && entry.Messages.Count == Messages.Length
            && entry.Messages.Select((message, index) => ReferenceEquals(message, Messages[index])).All(same => same);
    }

    private sealed record ExplicitCall(NetworkConnectionId Direct, IReadOnlyList<OutboundEntry> Registered);

    private sealed class Discard : IOutboundSender
    {
        public void Submit(IReadOnlyList<OutboundEntry> entries)
        {
        }
    }
}

/// <summary>The messages a <see cref="RecordedOutbound"/> sees, read the way the old doubles' lists were.</summary>
internal sealed class RecordedMessages<TMessage>(RecordedOutbound owner, bool callsOnly = false) : IReadOnlyList<TMessage>
    where TMessage : IServerNetworkMessage
{
    private IReadOnlyList<TMessage> Now =>
        [.. (callsOnly ? owner.Calls : owner.Entries.Where(entry => !entry.IsClose)).SelectMany(entry => entry.Messages).Cast<TMessage>()];

    public int Count => Now.Count;

    public TMessage this[int index] => Now[index];

    public void Clear() => owner.Clear();

    public IEnumerator<TMessage> GetEnumerator() => Now.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>Any read of a <see cref="RecordedOutbound"/>, as a list whose <c>Clear()</c> hides what it shows now.</summary>
internal sealed class RecordedView<T>(RecordedOutbound owner, Func<IReadOnlyList<T>> read) : IReadOnlyList<T>
{
    public int Count => read().Count;

    public T this[int index] => read()[index];

    public void Clear() => owner.Clear();

    public IEnumerator<T> GetEnumerator() => read().GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>The prepared batches a <see cref="RecordedOutbound"/> sees, with each message's frames.</summary>
internal sealed class RecordedBatches(RecordedOutbound owner)
    : IReadOnlyList<(NetworkConnectionContext Connection, IServerNetworkMessage[] Messages, byte[][] Frames)>
{
    private IReadOnlyList<(NetworkConnectionContext Connection, IServerNetworkMessage[] Messages, byte[][] Frames)> Now =>
        [.. owner.Entries.Where(owner.CameFromBatch).Select(entry => (entry.Connection, entry.Messages.ToArray(), owner.FramesOf(entry)))];

    public int Count => Now.Count;

    public (NetworkConnectionContext Connection, IServerNetworkMessage[] Messages, byte[][] Frames) this[int index] => Now[index];

    public void Clear() => owner.Clear();

    public IEnumerator<(NetworkConnectionContext Connection, IServerNetworkMessage[] Messages, byte[][] Frames)> GetEnumerator() =>
        Now.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// The protocol's registered encoders, applied as the networking module's registry encoder applies them (same
/// refusals, same wording, the frame limit for prepared batches); the frames of each message are kept for reading.
/// </summary>
internal sealed class RegistrationEncoder : IOutboundMessageEncoder
{
    private const int MaximumFrameLength = 1024 * 1024;
    private readonly ConditionalWeakTable<IServerNetworkMessage, byte[][]> frames = new();
    private readonly ConcurrentDictionary<Type, (IServerMessageEncoder Encoder, NetworkEndpointKind Endpoint)> resolved = new();
    private IReadOnlyDictionary<Type, ServerMessageEncoderRegistration>? registrations;
    private IServiceProvider? provider;

    /// <summary>Binds the container whose registrations encode; each encoder is resolved the first time it is needed.</summary>
    public void Use(IServiceProvider services)
    {
        if (registrations is not null) return;
        provider = services;
        registrations = services.GetServices<ServerMessageEncoderRegistration>().ToDictionary(item => item.MessageType);
    }

    public byte[][]? FramesOf(OutboundEntry entry)
    {
        var all = new List<byte[]>();
        foreach (var message in entry.Messages)
        {
            if (!frames.TryGetValue(message, out var own))
                return null;
            all.AddRange(own);
        }
        return [.. all];
    }

    public byte[] EncodeMessage(NetworkConnectionContext connection, IServerNetworkMessage message)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(message);
        var entry = Find(message);
        if (entry.Endpoint != connection.EndpointKind)
        {
            throw new InvalidOperationException(
                $"Server message '{message.GetType().Name}' is not supported for endpoint " +
                $"'{connection.EndpointKind}'.");
        }
        return Encode(message, entry.Encoder, checkLength: false);
    }

    public byte[] EncodeBatch(NetworkConnectionContext connection, IReadOnlyList<IServerNetworkMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(messages);
        var found = messages.Select(message =>
        {
            ArgumentNullException.ThrowIfNull(message);
            var entry = Find(message);
            if (entry.Endpoint != connection.EndpointKind)
                throw new InvalidOperationException($"Server message '{message.GetType().Name}' has an incompatible endpoint.");
            return entry;
        }).ToArray();
        using var buffer = new MemoryStream();
        for (var i = 0; i < messages.Count; i++)
            buffer.Write(Encode(messages[i], found[i].Encoder, checkLength: true));
        return buffer.ToArray();
    }

    private (IServerMessageEncoder Encoder, NetworkEndpointKind Endpoint) Find(IServerNetworkMessage message)
    {
        var known = registrations ?? throw new InvalidOperationException("The protocol encoders are not bound yet.");
        if (!known.TryGetValue(message.GetType(), out var registration))
            throw new InvalidOperationException($"Server message '{message.GetType().Name}' has no registered encoder.");
        return resolved.GetOrAdd(registration.MessageType, _ =>
            ((IServerMessageEncoder)provider!.GetRequiredService(registration.EncoderType), registration.Endpoint));
    }

    private byte[] Encode(IServerNetworkMessage message, IServerMessageEncoder encoder, bool checkLength)
    {
        var own = new List<byte[]>();
        using var buffer = new MemoryStream();
        foreach (var frame in encoder.Encode(message))
        {
            if (checkLength && (frame is null || frame.Length > MaximumFrameLength))
                throw new InvalidDataException("An encoded batch frame exceeds the transport limit or is unavailable.");
            ArgumentNullException.ThrowIfNull(frame);
            own.Add(frame.ToArray());
            buffer.Write(frame);
        }
        frames.AddOrUpdate(message, [.. own]);
        return buffer.ToArray();
    }
}
