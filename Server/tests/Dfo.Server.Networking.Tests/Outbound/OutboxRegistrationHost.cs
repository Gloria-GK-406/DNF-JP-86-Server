using System.Collections.Concurrent;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Execution;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Networking.Tests.Outbound;

/// <summary>
/// The production registration path without a socket: the real outbox adapters and outbox on a real
/// executor, given connections, an encoder and a sender. A job's entries reach the sender when it ends.
/// </summary>
internal sealed class OutboxRegistrationHost : IAsyncDisposable
{
    private readonly ServiceProvider provider;

    public OutboxRegistrationHost(INetworkConnectionContexts contexts, IOutboundMessageEncoder encoder, IOutboundSender? sender = null)
    {
        Recorder = new RecordingOutboundSender(sender);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(contexts);
        services.AddSingleton(encoder);
        services.AddSingleton<IOutboundSender>(Recorder);
        services.AddGameplayExecution();
        provider = services.BuildServiceProvider();
    }

    public RecordingOutboundSender Recorder { get; }

    public INetworkMessageSender Sender => provider.GetRequiredService<INetworkMessageSender>();

    public INetworkConnectionCloser Closer => provider.GetRequiredService<INetworkConnectionCloser>();

    public IOutboundOrdering Ordering => provider.GetRequiredService<IOutboundOrdering>();

    /// <summary>Every entry handed to the sender, in submission order.</summary>
    public IReadOnlyList<OutboundEntry> Entries => [.. Recorder.Submissions.SelectMany(batch => batch)];

    public Task RunAsync(Func<Task> body, NetworkConnectionContext? source = null) =>
        provider.GetRequiredService<GameplayExecutor>().RunAsync(new GameplayWork(
            GameplayShardKey.World,
            GameplayCallChain.NewRoot(GameplayCallSource.Connection),
            "test:outbox-registration",
            async _ => await body(),
            source), CancellationToken.None).AsTask();

    public ValueTask DisposeAsync() => provider.DisposeAsync();
}

/// <summary>Records each submission, then passes it on when a real sender is given.</summary>
internal sealed class RecordingOutboundSender(IOutboundSender? inner = null) : IOutboundSender
{
    public ConcurrentQueue<OutboundEntry[]> Submissions { get; } = new();

    public IOutboundSender? Inner { get; set; } = inner;

    public void Submit(IReadOnlyList<OutboundEntry> entries)
    {
        Submissions.Enqueue([.. entries]);
        Inner?.Submit(entries);
    }
}

/// <summary>Connections by ID, as the transport registry answers for them.</summary>
internal sealed class FixedConnections : INetworkConnectionContexts
{
    private readonly ConcurrentDictionary<NetworkConnectionId, NetworkConnectionContext> contexts = new();

    public NetworkConnectionContext Add(string id)
    {
        var context = new NetworkConnectionContext(new NetworkConnectionId(id), NetworkEndpointKind.Game, 10011);
        contexts[context.ConnectionId] = context;
        return context;
    }

    public void Remove(NetworkConnectionId id) => contexts.TryRemove(id, out _);

    public bool TryGet(NetworkConnectionId connectionId, out NetworkConnectionContext? connection) =>
        contexts.TryGetValue(connectionId, out connection);
}
