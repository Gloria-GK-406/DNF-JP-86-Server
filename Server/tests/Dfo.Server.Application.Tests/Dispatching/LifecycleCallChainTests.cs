using System.Collections.Concurrent;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.Execution.Outbox;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.Tests.Dispatching;

/// <summary>
/// The teardown of a connection a CMD asked to close is that CMD's child (RQ-050); any other teardown is a new root.
/// A teardown whose inherited chain is past the limit is not lost (RQ-053(d)) and the root connection is closed (RQ-053(a)).
/// </summary>
public sealed class LifecycleCallChainTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task A_disconnect_after_a_cmd_registered_close_is_a_child_of_that_cmd()
    {
        await using var w = World.Build();
        var requester = w.Contexts.Add("Game:l1-a");
        var closed = w.Contexts.Add("Game:l1-b");

        var cmd = await w.RunAsync(GameplayCallChain.NewRoot(GameplayCallSource.Connection, requester.ConnectionId),
            ct => w.Closer.RequestCloseAsync(closed.ConnectionId, ct));
        Assert.Same(closed, Assert.Single(w.Sender.Entries, x => x.IsClose).Connection);
        await w.DisconnectAsync(closed);

        var chain = Assert.Single(w.Seen).Chain!.Value;
        Assert.Equal(cmd.RootId, chain.RootId);
        Assert.Equal(cmd.Id, chain.ParentId);
        Assert.Equal(1, chain.Depth);
        Assert.Equal(GameplayCallSource.Lifecycle, chain.Source);
        Assert.Equal(requester.ConnectionId, chain.SourceConnection);
        Assert.Equal("lifecycle:disconnected", chain.CommandType);
        var completed = Assert.Single(w.Logs.Entries, x => x.Message.StartsWith("Gameplay lifecycle:disconnected completed", StringComparison.Ordinal));
        Assert.Contains($"root {cmd.RootId}, parent {cmd.Id}, depth 1,", completed.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_disconnect_after_a_failed_cmd_closed_its_source_is_a_child_of_that_cmd()
    {
        await using var w = World.Build();
        var source = w.Contexts.Add("Game:l2");
        var outboxes = w.Services.GetRequiredService<GameplayOutboxes>();
        GameplayCallChain? cmd = null;

        await Assert.ThrowsAsync<IOException>(() => w.Queue.RunAsync(new GameplayWork(GameplayShardKey.World,
            GameplayCallChain.NewRoot(GameplayCallSource.Connection, source.ConnectionId), "test:failing", token =>
            {
                cmd = GameplayJob.Current;
                _ = outboxes.Current;
                throw new IOException("Injected CMD failure.");
            }, source), CancellationToken.None).AsTask().WaitAsync(Patience));
        Assert.Same(source, Assert.Single(w.Sender.Entries, x => x.IsClose).Connection);
        await w.DisconnectAsync(source);

        var chain = Assert.Single(w.Seen).Chain!.Value;
        Assert.Equal(cmd!.Value.Id, chain.ParentId);
        Assert.Equal(cmd!.Value.RootId, chain.RootId);
        Assert.Equal(1, chain.Depth);
        Assert.Equal(source.ConnectionId, chain.SourceConnection);
    }

    [Fact]
    public async Task A_disconnect_without_a_registered_close_is_a_new_root()
    {
        await using var w = World.Build();
        var closed = w.Contexts.Add("Game:l3");
        // The same ID, a different connection: the close was asked for the first one only.
        var reconnected = closed with { };

        await w.RunAsync(GameplayCallChain.NewRoot(GameplayCallSource.Connection),
            ct => w.Closer.RequestCloseExactAsync(closed, ct));
        await w.DisconnectAsync(reconnected);

        var chain = Assert.Single(w.Seen).Chain!.Value;
        Assert.Equal(0, chain.Depth);
        Assert.Null(chain.ParentId);
        Assert.Equal(chain.Id, chain.RootId);
        Assert.Equal(GameplayCallSource.Lifecycle, chain.Source);
        Assert.Equal(closed.ConnectionId, chain.SourceConnection);
    }

    [Fact]
    public async Task A_close_cause_is_used_once()
    {
        await using var w = World.Build();
        var closed = w.Contexts.Add("Game:l4");

        var cmd = await w.RunAsync(GameplayCallChain.NewRoot(GameplayCallSource.Connection),
            ct => w.Closer.RequestCloseAsync(closed.ConnectionId, ct));
        await w.DisconnectAsync(closed);
        await w.Lifecycle.DispatchDisconnectedAsync(closed, CancellationToken.None).AsTask().WaitAsync(Patience);

        var chains = w.Seen.Select(x => x.Chain!.Value).ToArray();
        Assert.Equal(2, chains.Length);
        Assert.Equal(cmd.Id, chains[0].ParentId);
        Assert.Equal(1, chains[0].Depth);
        Assert.Null(chains[1].ParentId);
        Assert.Equal(0, chains[1].Depth);
    }

    [Fact]
    public async Task A_teardown_whose_inherited_chain_is_past_the_limit_still_runs_and_closes_the_root_connection()
    {
        await using var w = World.Build();
        var root = w.Contexts.Add("Game:l5-a");
        var closed = w.Contexts.Add("Game:l5-b");
        var deep = GameplayCallChain.NewRoot(GameplayCallSource.Connection, root.ConnectionId).ForCommand("test:root");
        for (var depth = 0; depth < GameplayCallDepth.Limit; depth++)
            deep = deep.Child(GameplayCallSource.Event).ForCommand("test:hop");

        await w.RunAsync(deep, ct => w.Closer.RequestCloseAsync(closed.ConnectionId, ct));
        await w.DisconnectAsync(closed);

        var refused = Assert.Single(w.Logs.Entries, x => x.Message.StartsWith(
            "Gameplay lifecycle:disconnected exceeds the depth limit", StringComparison.Ordinal));
        Assert.Equal(LogLevel.Error, refused.Level);
        Assert.Contains("; chain Connection:test:root > ", refused.Message, StringComparison.Ordinal);
        var teardown = Assert.Single(w.Seen).Chain!.Value;
        Assert.Equal(0, teardown.Depth);
        Assert.Null(teardown.ParentId);
        Assert.Equal(GameplayCallSource.Lifecycle, teardown.Source);
        Assert.Equal(closed.ConnectionId, teardown.SourceConnection);
        Assert.Equal([closed, root], w.Sender.Entries.Where(x => x.IsClose).Select(x => x.Connection));
        Assert.Single(w.Logs.Entries, x => x.Level == LogLevel.Warning
            && x.Message.Contains("exceeded the depth limit", StringComparison.Ordinal)
            && x.Message.Contains(closed.ConnectionId.ToString(), StringComparison.Ordinal));
    }

    private sealed record Seen(bool OnExecutor, GameplayCallChain? Chain);

    private sealed class World : IAsyncDisposable
    {
        private World(ServiceProvider services, Recorder sender, Contexts contexts, ConcurrentQueue<Seen> seen, LogCapture logs)
        {
            Services = services;
            Sender = sender;
            Contexts = contexts;
            Seen = seen;
            Logs = logs;
        }

        public ServiceProvider Services { get; }
        public Recorder Sender { get; }
        public Contexts Contexts { get; }
        public ConcurrentQueue<Seen> Seen { get; }
        public LogCapture Logs { get; }
        public IGameplayQueue Queue => Services.GetRequiredService<IGameplayQueue>();
        public INetworkConnectionCloser Closer => Services.GetRequiredService<INetworkConnectionCloser>();
        public INetworkConnectionLifecycleDispatcher Lifecycle => Services.GetRequiredService<INetworkConnectionLifecycleDispatcher>();

        public static World Build()
        {
            var sender = new Recorder();
            var contexts = new Contexts();
            var seen = new ConcurrentQueue<Seen>();
            var logs = new LogCapture();
            var services = new ServiceCollection();
            services.AddLogging(logging => logging.AddProvider(logs).SetMinimumLevel(LogLevel.Trace));
            services.AddNetworkMessageDispatching();
            services.AddSingleton<IOutboundSender>(sender);
            services.AddSingleton<INetworkConnectionContexts>(contexts);
            services.AddSingleton<INetworkConnectionObserver>(new Observer(seen));
            return new World(services.BuildServiceProvider(), sender, contexts, seen, logs);
        }

        /// <summary>Runs <paramref name="body"/> as one job of <paramref name="chain"/>; returns the job's chain.</summary>
        public async Task<GameplayCallChain> RunAsync(GameplayCallChain chain, Func<CancellationToken, ValueTask> body)
        {
            GameplayCallChain? current = null;
            await Queue.RunAsync(new GameplayWork(GameplayShardKey.World, chain, "test:closer", async ct =>
            {
                current = GameplayJob.Current;
                await body(ct);
            }), CancellationToken.None).AsTask().WaitAsync(Patience);
            return current!.Value;
        }

        /// <summary>What the transport does when a connection is gone: unregister it, then dispatch its teardown.</summary>
        public async Task DisconnectAsync(NetworkConnectionContext connection)
        {
            Contexts.Remove(connection);
            await Lifecycle.DispatchDisconnectedAsync(connection, CancellationToken.None).AsTask().WaitAsync(Patience);
        }

        public ValueTask DisposeAsync() => Services.DisposeAsync();
    }

    private sealed class Observer(ConcurrentQueue<Seen> seen) : INetworkConnectionObserver
    {
        public ValueTask ConnectedAsync(NetworkConnectionContext connection, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask DisconnectedAsync(NetworkConnectionContext connection, CancellationToken cancellationToken)
        {
            seen.Enqueue(new(GameplayJob.IsOnExecutor, GameplayJob.Current));
            return ValueTask.CompletedTask;
        }
    }

    private sealed class Recorder : IOutboundSender
    {
        public ConcurrentQueue<OutboundEntry> Entries { get; } = new();

        public void Submit(IReadOnlyList<OutboundEntry> entries)
        {
            foreach (var entry in entries) Entries.Enqueue(entry);
        }
    }

    private sealed class Contexts : INetworkConnectionContexts
    {
        private readonly ConcurrentDictionary<NetworkConnectionId, NetworkConnectionContext> registered = new();

        public bool TryGet(NetworkConnectionId connectionId, out NetworkConnectionContext? connection) =>
            registered.TryGetValue(connectionId, out connection);

        public NetworkConnectionContext Add(string id)
        {
            var context = new NetworkConnectionContext(new NetworkConnectionId(id), NetworkEndpointKind.Game, 10011);
            registered[context.ConnectionId] = context;
            return context;
        }

        public void Remove(NetworkConnectionContext context) => registered.TryRemove(context.ConnectionId, out _);
    }

    private sealed class LogCapture : ILoggerProvider
    {
        public ConcurrentQueue<(LogLevel Level, string Message)> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new Logger(this);

        public void Dispose()
        {
        }

        private sealed class Logger(LogCapture owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter) => owner.Entries.Enqueue((logLevel, formatter(state, exception)));
        }
    }
}
