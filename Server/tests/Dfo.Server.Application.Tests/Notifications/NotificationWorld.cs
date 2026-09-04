using System.Diagnostics;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Notifications;
using Dfo.Server.Application.Sessions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
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

internal sealed class RecordingSender : INetworkMessageSender
{
    private readonly HashSet<NetworkConnectionId> failing = [];
    private readonly HashSet<NetworkConnectionId> stalling = [];

    public List<(NetworkConnectionId ConnectionId, IServerNetworkMessage Message)> Sent { get; } = [];

    /// <summary>Every connection a send was started for, whatever became of it.</summary>
    public List<NetworkConnectionId> Attempted { get; } = [];

    public Action<NetworkConnectionId>? OnSend { get; set; }

    public void FailFor(NetworkConnectionId connectionId) => failing.Add(connectionId);

    /// <summary>The connection recovers, so a retry has something to succeed at.</summary>
    public void StopFailing() => failing.Clear();

    public void StallFor(NetworkConnectionId connectionId) => stalling.Add(connectionId);

    public async ValueTask SendAsync(
        NetworkConnectionId connectionId,
        IServerNetworkMessage message,
        CancellationToken cancellationToken)
    {
        Attempted.Add(connectionId);
        OnSend?.Invoke(connectionId);
        if (failing.Contains(connectionId))
            throw new IOException($"Connection '{connectionId}' went away.");
        if (stalling.Contains(connectionId))
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        Sent.Add((connectionId, message));
    }

    public ValueTask SendExplicitAsync(
        NetworkConnectionId requiredDirectConnectionId,
        ExplicitMessageDelivery delivery,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("A notification never sends a delivery plan.");
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
/// Shared by the delivery cases and the retry ones because they need the same server: the same
/// people playing, the same handler, the same sender that can be told to fail, stall or refuse.
/// Two copies of this would drift, and the second copy is where a case that only passes because
/// its own stand-in was wrong would live.
/// </remarks>
internal sealed class NotificationWorld
{
    private readonly SelectedSessionDomainService sessions = new();
    private readonly Dictionary<CharacterId, SelectedSessionIdentity> playing = [];
    private readonly ServiceProvider provider;

    /// <param name="retryLimit">
    /// How many further goes a failed recipient gets. Zero by default, so a case that is not about
    /// retries sees a publish end where it ends.
    /// </param>
    /// <param name="timeout">Per recipient. Long by default, so no case waits on it by accident.</param>
    public NotificationWorld(int retryLimit = 0, TimeSpan? timeout = null)
    {
        Sessions = new AwkwardSessions(sessions);
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddProvider(Log));
        services.AddSingleton<ISelectedSessionDomainService>(Sessions);
        services.AddSingleton<INetworkMessageSender>(Sender);
        // The drain interval is infinite because every case here drains by hand. A loop on a clock
        // would make these cases wait for one, and make them flaky when the machine is busy.
        services.AddSingleton(new NotificationDeliveryOptions(
            timeout ?? TimeSpan.FromSeconds(5),
            RetryInterval: Timeout.InfiniteTimeSpan));
        services.AddSingleton(new ComposeRule(recipient => Compose(recipient)));
        services.AddNotificationDispatching();
        services.AddNotificationHandler<TownBellRang, TownBellHandler>(retryLimit);
        provider = services.BuildServiceProvider();
    }

    /// <summary>What a recorded frame says, so a case can name it rather than compare objects.</summary>
    public static string Label(IServerNetworkMessage message) => ((TownBellMessage)message).Text;

    /// <summary>What the bus said about deliveries that did not arrive.</summary>
    public RecordingLoggerProvider Log { get; } = new();

    public RecordingSender Sender { get; } = new();

    public AwkwardSessions Sessions { get; }

    public Func<NotificationRecipient, IReadOnlyList<IServerNetworkMessage>> Compose { get; set; } =
        recipient => [new TownBellMessage($"for-{recipient.CharacterId.Value}")];

    public void Play(CharacterId characterId)
    {
        var session = SelectedSessionIdentity.Create(
            new AccountId(characterId.Value),
            characterId,
            new NetworkConnectionId($"Game:{characterId.Value}"),
            connectionGeneration: 1,
            listenerPort: 10011);
        playing[characterId] = session;
        _ = sessions.Select(session);
    }

    public void Leave(CharacterId characterId) => sessions.ReleaseExact(playing[characterId]);

    /// <summary>The same character, back on a connection this publish never resolved.</summary>
    public void Reconnect(CharacterId characterId)
    {
        var previous = playing[characterId];
        sessions.ReleaseExact(previous);
        var session = SelectedSessionIdentity.Create(
            previous.AccountId,
            characterId,
            new NetworkConnectionId($"Game:{characterId.Value}-again"),
            connectionGeneration: previous.ConnectionGeneration + 1,
            listenerPort: previous.ListenerPort);
        playing[characterId] = session;
        _ = sessions.Select(session);
    }

    public NetworkConnectionId ConnectionOf(CharacterId characterId) =>
        playing[characterId].ConnectionId;

    /// <summary>How many recipients are waiting for another go.</summary>
    public int Waiting => provider.GetRequiredService<NotificationRetryQueue>().Count;

    public ValueTask<NotificationDelivery> PublishToAsync(
        IReadOnlyList<CharacterId> audience, CancellationToken cancellationToken = default) =>
        provider.GetRequiredService<INotificationBus>().PublishAsync(
            new NotificationAudience.TheseCharacters(audience),
            new TownBellRang("closing time"),
            cancellationToken);

    /// <summary>One retry pass, driven by hand rather than by a clock.</summary>
    public ValueTask DrainAsync(CancellationToken cancellationToken = default) =>
        provider.GetRequiredService<INotificationRetryDrain>().DrainAsync(cancellationToken);
}
