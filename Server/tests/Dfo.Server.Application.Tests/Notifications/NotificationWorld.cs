using System.Diagnostics.CodeAnalysis;
using System.Diagnostics;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Application.DomainEvents;
using Dfo.Server.Application.Execution;
using Dfo.Server.Application.Notifications;
using Dfo.Server.Application.Sessions;
using Dfo.Server.Application.Users;
using Dfo.Server.Testing;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Networking.Outbound;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.Tests.Notifications;

internal sealed record TownBellRang(string Reason) : INotification<TownBellRang>;

internal sealed record TownBellMessage(string Text) : IServerNetworkMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameInitialConnection;
}

/// <summary>What the handler answers with, so each case can shape it.</summary>
internal sealed record ComposeRule(
    Func<NotificationRecipient, IReadOnlyList<IServerNetworkMessage>> Compose);

internal sealed class TownBellHandler(ComposeRule rule) : INotificationHandler<TownBellRang>
{
    public ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(
        NotificationRecipient recipient,
        TownBellRang notification,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(rule.Compose(recipient));
}

/// <summary>The real directory, with one character it refuses to answer about.</summary>
/// <remarks>
/// A stand-in rather than a real failure because the real one cannot be provoked from here:
/// <c>FindByCharacter</c> uses <c>SingleOrDefault</c>, and the directory's own admission check
/// makes two sessions on one character impossible today. That it cannot happen today is not
/// the same as it cannot happen, and this is what says which way the code falls if it does.
/// </remarks>
internal sealed class AwkwardSessions(ISelectedSessionDomainService inner)
    : ISelectedSessionDomainService
{
    // Every answer is the real directory's, except that looking up a refused character throws.
    // A lookup by character inside the bus goes through FindByCharacter, so it throws too.
    public void EnterWorld(SelectedSessionIdentity session) => inner.EnterWorld(session);

    public bool Fence(SelectedSessionIdentity session) => inner.Fence(session);

    public SelectedSessionStanding? StandingOf(SelectedSessionIdentity session) => inner.StandingOf(session);

    public bool IsSelected(SelectedSessionIdentity session) => inner.IsSelected(session);

    public bool IsInWorld(SelectedSessionIdentity session) => inner.IsInWorld(session);

    public bool TryGetInWorld(CharacterId characterId, [NotNullWhen(true)] out SelectedSessionIdentity? session)
    {
        session = FindByCharacter(characterId);
        if (session is not null && !inner.IsInWorld(session)) session = null;
        return session is not null;
    }

    public bool TryGetInWorld(ushort wireUserId, [NotNullWhen(true)] out SelectedSessionIdentity? session) =>
        inner.TryGetInWorld(wireUserId, out session);

    public bool TryGetInWorld(NetworkConnectionId connectionId, [NotNullWhen(true)] out SelectedSessionIdentity? session) =>
        inner.TryGetInWorld(connectionId, out session);

    private readonly HashSet<CharacterId> refusing = [];

    public void ThrowFor(CharacterId characterId) => refusing.Add(characterId);

    public SelectedSessionIdentity? FindByCharacter(CharacterId characterId) =>
        refusing.Contains(characterId)
            ? throw new InvalidOperationException("Sequence contains more than one matching element")
            : inner.FindByCharacter(characterId);

    public SelectedSessionIdentity? Select(SelectedSessionIdentity session) => inner.Select(session);

    public bool ReleaseExact(SelectedSessionIdentity session) => inner.ReleaseExact(session);

    public SelectedSessionIdentity? Find(AccountId accountId) => inner.Find(accountId);

    public SelectedSessionIdentity? FindByWireUserId(ushort wireUserId) =>
        inner.FindByWireUserId(wireUserId);

    public SelectedSessionIdentity? FindByConnection(NetworkConnectionId connectionId) =>
        inner.FindByConnection(connectionId);
}

/// <summary>
/// What became of the world's outbound entries: the bus registers them in the publishing job's outbox,
/// the job hands them to a real <see cref="OutboundSender"/>, and this records the submissions and
/// plays the transport. A connection can be told to fail its sends, as a socket that went away.
/// </summary>
internal sealed class WorldDelivery : IOutboundSender, IOutboundDeliveryObserver, IOutboundTransport
{
    private readonly object sync = new();
    private readonly List<OutboundEntry> submitted = [];
    private readonly Dictionary<OutboundEntry, OutboundOutcome> outcomes = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<NetworkConnectionContext, WorldConnection> live = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<NetworkConnectionId> failing = [];
    private OutboundSender? sender;

    public void Attach(OutboundSender real) => sender = real;

    /// <summary>Every entry the publishing jobs handed over, in the order they were registered.</summary>
    public IReadOnlyList<OutboundEntry> Registered
    {
        get { lock (sync) return [.. submitted]; }
    }

    /// <summary>What reached a connection, in registration order.</summary>
    public IReadOnlyList<(NetworkConnectionId ConnectionId, IServerNetworkMessage Message)> Sent
    {
        get
        {
            lock (sync)
                return
                [
                    .. submitted
                        .Where(entry => outcomes.TryGetValue(entry, out var outcome) && outcome == OutboundOutcome.Delivered)
                        .SelectMany(entry => entry.Messages.Select(message => (entry.Connection.ConnectionId, message))),
                ];
        }
    }

    public void FailFor(NetworkConnectionId connectionId)
    {
        lock (sync) failing.Add(connectionId);
    }

    public bool IsClosed(NetworkConnectionId connectionId)
    {
        lock (sync) return live.Values.Any(connection => connection.Id == connectionId && connection.Closed);
    }

    public void Connect(NetworkConnectionContext context)
    {
        lock (sync) live[context] = new WorldConnection(this, context.ConnectionId);
    }

    public void Submit(IReadOnlyList<OutboundEntry> entries)
    {
        lock (sync) submitted.AddRange(entries);
        sender!.Submit(entries);
    }

    /// <summary>Waits until every submitted entry has an outcome.</summary>
    public async Task SettledAsync()
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (true)
        {
            lock (sync)
            {
                if (submitted.All(outcomes.ContainsKey))
                    return;
            }
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("The world's outbound entries did not settle.");
            await Task.Delay(5);
        }
    }

    public void OnEntryCompleted(OutboundDeliveryReport report)
    {
        lock (sync) outcomes[report.Entry] = report.Outcome;
    }

    public void OnConnectionFailed(NetworkConnectionContext connection, OutboundOutcome reason)
    {
    }

    IOutboundConnection? IOutboundTransport.Resolve(NetworkConnectionContext expected)
    {
        lock (sync) return live.TryGetValue(expected, out var connection) && !connection.Closed ? connection : null;
    }

    private bool Fails(NetworkConnectionId connectionId)
    {
        lock (sync) return failing.Contains(connectionId);
    }

    private sealed class WorldConnection(WorldDelivery owner, NetworkConnectionId id) : IOutboundConnection
    {
        public NetworkConnectionId Id { get; } = id;

        public bool Closed { get; private set; }

        public ValueTask SendAsync(ReadOnlyMemory<byte> frames, CancellationToken cancellationToken) =>
            owner.Fails(Id)
                ? ValueTask.FromException(new IOException($"Connection '{Id}' went away."))
                : ValueTask.CompletedTask;

        public bool HasClosedSendSide(Exception error) => false;

        public ValueTask CloseAsync()
        {
            Closed = true;
            return ValueTask.CompletedTask;
        }
    }
}

/// <summary>The connections the world's players are on, as the transport would answer for them.</summary>
internal sealed class WorldConnections : INetworkConnectionContexts
{
    private readonly Dictionary<NetworkConnectionId, NetworkConnectionContext> contexts = [];
    private readonly List<NetworkConnectionId> lookups = [];

    /// <summary>Every connection asked about, in order.</summary>
    public IReadOnlyList<NetworkConnectionId> Lookups
    {
        get { lock (contexts) return [.. lookups]; }
    }

    public NetworkConnectionContext Add(NetworkConnectionId connectionId, int listenerPort)
    {
        var context = new NetworkConnectionContext(connectionId, NetworkEndpointKind.Game, listenerPort);
        lock (contexts) contexts[connectionId] = context;
        return context;
    }

    public bool TryGet(NetworkConnectionId connectionId, out NetworkConnectionContext? connection)
    {
        lock (contexts)
        {
            lookups.Add(connectionId);
            return contexts.TryGetValue(connectionId, out connection);
        }
    }
}

/// <summary>Encodes a bell message as its text; a text starting "unencodable" cannot be encoded.</summary>
internal sealed class WorldEncoder : IOutboundMessageEncoder
{
    public byte[] EncodeMessage(NetworkConnectionContext connection, IServerNetworkMessage message) =>
        ((TownBellMessage)message).Text.StartsWith("unencodable", StringComparison.Ordinal)
            ? throw new InvalidDataException($"'{((TownBellMessage)message).Text}' cannot be encoded.")
            : System.Text.Encoding.UTF8.GetBytes(((TownBellMessage)message).Text);

    public byte[] EncodeBatch(NetworkConnectionContext connection, IReadOnlyList<IServerNetworkMessage> messages) =>
        [.. messages.SelectMany(message => EncodeMessage(connection, message))];
}

/// <summary>Keeps what the bus logged, so a case can read it back.</summary>
/// <remarks>
/// A delivery that fails is not supposed to be silent. Without somewhere to read what was said,
/// that requirement is a line in a document -- the whole reason it was added is that the summary
/// carrying the same facts has no reader in production.
/// </remarks>
internal sealed class RecordingLoggerProvider : ILoggerProvider
{
    public List<(LogLevel Level, string Message, Exception? Error)> Entries { get; } = [];

    public ILogger CreateLogger(string categoryName) => new Sink(Entries);

    public void Dispose()
    {
    }

    private sealed class Sink(List<(LogLevel, string, Exception?)> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            entries.Add((logLevel, formatter(state, exception), exception));
        }
    }
}

/// <summary>A server with some characters playing on it and one notification wired up.</summary>
/// <remarks>
/// Every publish runs as a job on the world's own executor, as a CMD's projection does: the bus
/// composes and registers in that job's outbox, the job hands its outbox to a real outbound sender,
/// and the world waits for every entry to settle before the case reads what reached whom.
/// </remarks>
internal sealed class NotificationWorld
{
    private readonly OnlineStateRegistry state;
    private readonly SelectedSessionDomainService sessions;
    private readonly Dictionary<CharacterId, SelectedSessionIdentity> playing = [];
    private readonly ServiceProvider provider;
    private readonly WorldConnections connections = new();

    public NotificationWorld()
    {
        sessions = TestSessions.Directory(out state);
        Sessions = new AwkwardSessions(sessions);
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddProvider(Log));
        services.AddSingleton<ISelectedSessionDomainService>(Sessions);
        services.AddSingleton<INetworkConnectionContexts>(connections);
        services.AddSingleton<IOutboundMessageEncoder>(new WorldEncoder());
        services.AddSingleton<IOutboundSender>(Sender);
        services.AddSingleton(new ComposeRule(recipient =>
        {
            Composing?.Invoke(recipient);
            Composed.Add(recipient.CharacterId);
            return Compose(recipient);
        }));
        services.AddEventDispatching();
        services.AddNotificationDispatching();
        services.AddNotificationHandler<TownBellRang, TownBellHandler>();
        provider = services.BuildServiceProvider();
        Sender.Attach(new OutboundSender(Sender, [Sender],
            provider.GetRequiredService<ILogger<OutboundSender>>(), OutboundSendOptions.Default));
    }

    /// <summary>What a recorded frame says, so a case can name it rather than compare objects.</summary>
    public static string Label(IServerNetworkMessage message) => ((TownBellMessage)message).Text;

    /// <summary>What the bus said about deliveries that did not arrive.</summary>
    public RecordingLoggerProvider Log { get; } = new();

    public WorldDelivery Sender { get; } = new();

    /// <summary>The connection table the registration side asks.</summary>
    public WorldConnections Connections => connections;

    public AwkwardSessions Sessions { get; }

    public Func<NotificationRecipient, IReadOnlyList<IServerNetworkMessage>> Compose { get; set; } =
        recipient => [new TownBellMessage($"for-{recipient.CharacterId.Value}")];

    /// <summary>Runs as each recipient is composed, before the handler answers.</summary>
    public Action<NotificationRecipient>? Composing { get; set; }

    /// <summary>Every recipient the handler was asked about, across publishes.</summary>
    public List<CharacterId> Composed { get; } = [];

    public void Play(CharacterId characterId)
    {
        playing[characterId] = sessions.InWorld(
            state, new AccountId(characterId.Value), characterId, $"Game:{characterId.Value}");
        Connect(playing[characterId]);
    }

    /// <summary>Selected but not yet in the world, as a character is while its entry is under way.</summary>
    public void SelectOnly(CharacterId characterId)
    {
        playing[characterId] = sessions.Selected(
            state, new AccountId(characterId.Value), characterId, $"Game:{characterId.Value}");
        Connect(playing[characterId]);
    }

    /// <summary>The connection is revoked under the session, which waits for its teardown.</summary>
    public void Revoke(CharacterId characterId) => TestGameplay.Run(() =>
        Assert.True(state.TryDisconnectConnection(playing[characterId].ConnectionId)));

    public void Leave(CharacterId characterId) => TestGameplay.Run(() => sessions.ReleaseExact(playing[characterId]));

    /// <summary>The same character, back on a connection this publish never resolved.</summary>
    public void Reconnect(CharacterId characterId) => TestGameplay.Run(() =>
    {
        var previous = playing[characterId];
        sessions.ReleaseExact(previous);
        state.TryDisconnectConnection(previous.ConnectionId);
        playing[characterId] = sessions.InWorld(
            state, previous.AccountId, characterId, $"Game:{characterId.Value}-again", previous.ListenerPort);
        Connect(playing[characterId]);
    });

    public void ReplaceKeepingConnection(CharacterId characterId, int listenerPort) => TestGameplay.Run(() =>
    {
        var previous = playing[characterId];
        sessions.ReleaseExact(previous);
        Assert.True(state.TryGetByConnection(previous.ConnectionId, out var owner));
        var session = previous with
        {
            ConnectionGeneration = owner!.Generation,
            ListenerPort = listenerPort,
        };
        sessions.Select(session);
        sessions.EnterWorld(session);
        playing[characterId] = session;
    });

    public NetworkConnectionId ConnectionOf(CharacterId characterId) =>
        playing[characterId].ConnectionId;

    public SelectedSessionIdentity SessionOf(CharacterId characterId) => playing[characterId];

    public ValueTask<NotificationDelivery> PublishToAsync(
        IReadOnlyList<CharacterId> audience, CancellationToken cancellationToken = default) =>
        PublishAsync(new NotificationAudience.TheseCharacters(audience), cancellationToken);

    public ValueTask<NotificationDelivery> PublishToExactAsync(
        IReadOnlyList<SelectedSessionIdentity> audience,
        CancellationToken cancellationToken = default) =>
        PublishAsync(new NotificationAudience.TheseSessions(audience), cancellationToken);

    /// <summary>An immediate projection made from wherever the caller is, through the event service.</summary>
    public ValueTask<NotificationDelivery> ProjectHereAsync(IReadOnlyList<CharacterId> audience) =>
        provider.GetRequiredService<IEventService>().ProjectAsync(
            new NotificationAudience.TheseCharacters(audience), new TownBellRang("closing time"));

    /// <summary>The publish as one job on the world's executor; returns once its entries have settled.</summary>
    private async ValueTask<NotificationDelivery> PublishAsync(NotificationAudience audience, CancellationToken cancellationToken)
    {
        var executor = provider.GetRequiredService<GameplayExecutor>();
        var bus = provider.GetRequiredService<INotificationBus>();
        try
        {
            return await executor.RunAsync(new GameplayWork<NotificationDelivery>(
                GameplayShardKey.World,
                GameplayCallChain.NewRoot(GameplayCallSource.Connection),
                "test:publish",
                ct => bus.PublishAsync(audience, new TownBellRang("closing time"), ct)), cancellationToken);
        }
        finally
        {
            await Sender.SettledAsync();
        }
    }

    /// <summary>Observes the real bus result inside its publishing job, before its outbox is submitted.</summary>
    public async ValueTask<NotificationDelivery> PublishToObservedAsync(
        IReadOnlyList<SelectedSessionIdentity> audience,
        Action<NotificationDelivery> observe)
    {
        var executor = provider.GetRequiredService<GameplayExecutor>();
        var bus = provider.GetRequiredService<INotificationBus>();
        try
        {
            return await executor.RunAsync(new GameplayWork<NotificationDelivery>(
                GameplayShardKey.World,
                GameplayCallChain.NewRoot(GameplayCallSource.Connection),
                "test:publish-observed",
                async ct =>
                {
                    var result = await bus.PublishAsync(
                        new NotificationAudience.TheseSessions(audience), new TownBellRang("closing time"), ct);
                    observe(result);
                    return result;
                }), CancellationToken.None);
        }
        finally
        {
            await Sender.SettledAsync();
        }
    }

    private void Connect(SelectedSessionIdentity session) =>
        Sender.Connect(connections.Add(session.ConnectionId, session.ListenerPort));
}
