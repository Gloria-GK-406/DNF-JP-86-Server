using System.Collections.Concurrent;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.DomainEvents;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.Tests.DomainEvents;

/// <summary>
/// An event CMD is a child of the CMD that published its fact (RQ-050, RQ-051); a delivery whose chain is past
/// the depth limit is settled by a system CMD instead of spinning, and its root connection is closed (RQ-053, AC-016).
/// </summary>
public sealed partial class DomainEventDispatchTests
{
    private const string RefusedEventCounter = "Gameplay event:counter exceeds the depth limit";

    [Fact]
    public async Task An_event_cmd_is_a_child_of_the_cmd_that_published_its_fact()
    {
        await using var f = new Fixture(record: true, chain: true);
        var connection = new NetworkConnectionId("Game:chain-e1");
        var publisher = await PublishAsync(f, GameplayCallChain.NewRoot(GameplayCallSource.Connection, connection));

        Assert.Equal(1, await f.Dispatcher.DrainOnceAsync("counter"));
        Assert.Equal(1, await f.Dispatcher.DrainOnceAsync("live"));

        foreach (var type in new[] { "event:counter", "event:live" })
        {
            var chain = Assert.Single(f.Queue.Ran, x => x.CommandType == type).Current!.Value;
            Assert.Equal(publisher.RootId, chain.RootId);
            Assert.Equal(publisher.Id, chain.ParentId);
            Assert.Equal(1, chain.Depth);
            Assert.Equal(GameplayCallSource.Event, chain.Source);
            Assert.Equal(connection, chain.SourceConnection);
            Assert.Equal(new GameplayCallHop(GameplayCallSource.Connection, "test:publish"), Assert.Single(chain.Ancestors));
            var completed = Assert.Single(f.Logs.Entries, x => x.Message.StartsWith($"Gameplay {type} completed", StringComparison.Ordinal));
            Assert.Contains($"root {publisher.RootId}, parent {publisher.Id}, depth 1,", completed.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task A_fact_published_by_an_event_cmd_extends_the_chain_by_one()
    {
        await using var f = new Fixture(record: true, chain: true, relay: true);
        var publisher = await PublishAsync(f, GameplayCallChain.NewRoot(GameplayCallSource.Connection, new NetworkConnectionId("Game:chain-e2")));

        Assert.Equal(1, await f.Dispatcher.DrainOnceAsync("relay"));
        Assert.Equal(1, await f.Dispatcher.DrainOnceAsync("relayed"));

        var relay = Assert.Single(f.Queue.Ran, x => x.CommandType == "event:relay").Current!.Value;
        var relayed = Assert.Single(f.Probe.RelayedSeen).Current!.Value;
        Assert.Equal(2, relayed.Depth);
        Assert.Equal(relay.Id, relayed.ParentId);
        Assert.Equal(publisher.RootId, relayed.RootId);
        Assert.Equal([new GameplayCallHop(GameplayCallSource.Connection, "test:publish"),
            new GameplayCallHop(GameplayCallSource.Event, "event:relay")], relayed.Ancestors);
    }

    [Fact]
    public async Task A_retry_keeps_the_chain_of_its_first_consumption()
    {
        await using var f = new Fixture(record: true, chain: true);
        f.Clock.Frozen = DateTimeOffset.UtcNow;
        f.Probe.FailingAttempts = 1;
        var publisher = await PublishAsync(f, GameplayCallChain.NewRoot(GameplayCallSource.Connection, new NetworkConnectionId("Game:chain-e3")));

        Assert.Equal(1, await f.Dispatcher.DrainOnceAsync("counter"));
        f.Clock.Frozen += TimeSpan.FromMinutes(1);
        Assert.Equal(1, await f.Dispatcher.DrainOnceAsync("counter"));

        var attempts = f.Probe.CounterSeen.Select(x => x.Current!.Value).ToArray();
        Assert.Equal(2, attempts.Length);
        Assert.All(attempts, attempt =>
        {
            Assert.Equal(publisher.RootId, attempt.RootId);
            Assert.Equal(publisher.Id, attempt.ParentId);
            Assert.Equal(1, attempt.Depth);
        });
        Assert.NotEqual(attempts[0].Id, attempts[1].Id);
        Assert.Equal(3, f.Count("counter"));
    }

    [Fact]
    public async Task A_rolled_back_fact_leaves_no_chain_behind()
    {
        await using var f = new Fixture(record: true, chain: true);
        var chains = f.Services.GetRequiredService<DomainEventCallChains>();
        var recordedBeforeRollback = -1;
        await f.Services.GetRequiredService<IGameplayQueue>().RunAsync(new GameplayWork(GameplayShardKey.World,
            GameplayCallChain.NewRoot(GameplayCallSource.Connection), "test:rolled-back", async _ =>
            {
                await using var tx = f.Scope.Begin();
                await f.Events.EnqueueAsync(new Awarded("a", 1));
                recordedBeforeRollback = chains.Count;
            }), CancellationToken.None);
        Assert.Equal(3, recordedBeforeRollback);
        Assert.Equal(0, chains.Count);

        await PublishAsync(f, GameplayCallChain.NewRoot(GameplayCallSource.Connection));
        Assert.Equal(3, chains.Count);
        Assert.Equal(3, await f.Dispatcher.DrainOnceAsync());
        Assert.Equal(0, chains.Count);
    }

    [Fact]
    public async Task A_reliable_delivery_past_the_depth_limit_is_settled_terminal_and_its_root_connection_closed()
    {
        await using var f = new Fixture(record: true, chain: true);
        var root = f.Contexts.Add("Game:chain-d1");
        await PublishAsync(f, Deep(GameplayCallSource.Connection, root.ConnectionId));

        Assert.Equal(1, await f.Dispatcher.DrainOnceAsync("counter"));

        Assert.Equal(0, f.Probe.CounterAttempts);
        var refused = Assert.Single(f.Logs.Entries, x => x.Message.StartsWith(RefusedEventCounter, StringComparison.Ordinal));
        Assert.Equal(LogLevel.Error, refused.Level);
        Assert.Contains("depth 33,", refused.Message, StringComparison.Ordinal);
        Assert.Contains("; chain Connection:test:root > ", refused.Message, StringComparison.Ordinal);
        Assert.Equal(1, f.Scalar("SELECT COUNT(*) FROM domain_event_deliveries WHERE subscriber_id='counter' AND state=2 " +
            "AND last_error LIKE 'Depth limit exceeded%'"));
        var settle = Assert.Single(f.Queue.Ran, x => x.CommandType == "event:counter/depth-exceeded");
        Assert.True(settle.Completed);
        Assert.Equal(0, settle.Current!.Value.Depth);
        Assert.Null(settle.Current!.Value.ParentId);
        Assert.Equal(GameplayCallSource.Event, settle.Current!.Value.Source);
        Assert.Single(f.Logs.Entries, x => x.Level == LogLevel.Warning
            && x.Message.Contains("subscriber counter exceeded the depth limit", StringComparison.Ordinal)
            && x.Message.Contains("depth 33", StringComparison.Ordinal));
        Assert.Same(root, Assert.Single(f.Sender.Entries, x => x.IsClose).Connection);

        // The transport closes the connection; that teardown was not asked for by a CMD, so it is a new root.
        f.Contexts.Remove(root);
        await f.Lifecycle.DispatchDisconnectedAsync(root, CancellationToken.None);
        var teardown = Assert.Single(f.Probe.LifecycleSeen).Current!.Value;
        Assert.Equal(GameplayCallSource.Lifecycle, teardown.Source);
        Assert.Equal(0, teardown.Depth);
        Assert.Null(teardown.ParentId);
    }

    [Fact]
    public async Task A_live_delivery_past_the_depth_limit_is_completed_with_the_depth_reason()
    {
        await using var f = new Fixture(record: true, chain: true);
        var root = f.Contexts.Add("Game:chain-d2");
        await PublishAsync(f, Deep(GameplayCallSource.Connection, root.ConnectionId));

        Assert.Equal(1, await f.Dispatcher.DrainOnceAsync("live"));

        Assert.Empty(f.Probe.Live);
        Assert.Single(f.Logs.Entries, x => x.Message.StartsWith("Gameplay event:live exceeds the depth limit", StringComparison.Ordinal));
        Assert.Equal(1, f.Scalar("SELECT COUNT(*) FROM domain_event_deliveries WHERE subscriber_id='live' AND state=1 " +
            "AND last_error='Depth limit exceeded; live hint skipped.'"));
        Assert.Same(root, Assert.Single(f.Sender.Entries, x => x.IsClose).Connection);
    }

    [Fact]
    public async Task A_chain_without_a_connection_past_the_limit_is_settled_and_only_logged()
    {
        await using var f = new Fixture(record: true, chain: true);
        await PublishAsync(f, Deep(GameplayCallSource.Timer, connection: null));

        Assert.Equal(1, await f.Dispatcher.DrainOnceAsync("counter"));

        Assert.Equal(0, f.Probe.CounterAttempts);
        Assert.Single(f.Logs.Entries, x => x.Message.StartsWith(RefusedEventCounter, StringComparison.Ordinal));
        Assert.Equal(1, f.Scalar("SELECT COUNT(*) FROM domain_event_deliveries WHERE subscriber_id='counter' AND state=2"));
        Assert.DoesNotContain(f.Sender.Entries, x => x.IsClose);
    }

    [Fact]
    public async Task After_settling_a_delivery_past_the_limit_the_group_loop_waits_instead_of_spinning()
    {
        await using var f = new Fixture(record: true, chain: true, options: DomainEventDispatchOptions.Default with
        { RetryDelay = TimeSpan.FromMilliseconds(10), RecoveryInterval = TimeSpan.FromMinutes(1) });
        var root = f.Contexts.Add("Game:chain-d4");
        await PublishAsync(f, Deep(GameplayCallSource.Connection, root.ConnectionId));
        using var stop = new CancellationTokenSource();
        var worker = f.Dispatcher.RunAsync(stop.Token);

        var settled = await WaitUntilAsync(() => f.Scalar("SELECT COUNT(*) FROM domain_event_deliveries WHERE state=0") == 0);
        await Task.Delay(100);
        var reads = Volatile.Read(ref f.Probe.ReadDueCalls);
        var refusals = f.Logs.Entries.Count(x => x.Message.Contains(" exceeds the depth limit", StringComparison.Ordinal));
        await Task.Delay(500);
        var readsLater = Volatile.Read(ref f.Probe.ReadDueCalls);
        var refusalsLater = f.Logs.Entries.Count(x => x.Message.Contains(" exceeds the depth limit", StringComparison.Ordinal));
        await stop.CancelAsync();
        await worker.WaitAsync(Patience);

        Assert.True(settled, "the deliveries past the limit were never settled");
        Assert.Equal(3, refusals);
        Assert.Equal(refusals, refusalsLater);
        Assert.Equal(reads, readsLater);
        Assert.DoesNotContain(f.Logs.Entries, x => x.Message.Contains("could not persist or scan", StringComparison.Ordinal));
    }

    /// <summary>Publishes one <see cref="Awarded"/> from a job that runs as <paramref name="chain"/>; returns that job's chain.</summary>
    private static async Task<GameplayCallChain> PublishAsync(Fixture f, GameplayCallChain chain)
    {
        GameplayCallChain? publisher = null;
        await f.Services.GetRequiredService<IGameplayQueue>().RunAsync(new GameplayWork(GameplayShardKey.World, chain,
            "test:publish", async _ =>
            {
                publisher = GameplayJob.Current;
                await using var tx = f.Scope.Begin();
                await f.Events.EnqueueAsync(new Awarded("a", 3));
                await tx.CommitAsync(default);
            }), CancellationToken.None);
        return publisher!.Value;
    }

    /// <summary>A chain at exactly the depth limit: the executor still runs it, and every child is refused.</summary>
    private static GameplayCallChain Deep(GameplayCallSource source, NetworkConnectionId? connection)
    {
        var chain = GameplayCallChain.NewRoot(source, connection).ForCommand("test:root");
        for (var depth = 0; depth < GameplayCallDepth.Limit; depth++)
            chain = chain.Child(GameplayCallSource.Event).ForCommand("test:hop");
        return chain;
    }

    internal sealed record Relayed(string Owner) : IDomainEvent;

    private sealed class RelaySubscriber(IEventService events) : IDomainEventSubscriber<Awarded>
    {
        public async ValueTask HandleAsync(DomainEventEnvelope<Awarded> e, CancellationToken ct) =>
            await events.EnqueueAsync(new Relayed(e.Payload.Owner), ct);
    }

    private sealed class RelayedSubscriber(Probe probe) : IDomainEventSubscriber<Relayed>
    {
        public ValueTask HandleAsync(DomainEventEnvelope<Relayed> e, CancellationToken ct)
        {
            probe.RelayedSeen.Enqueue(Probe.Here());
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ChainObserver(Probe probe) : Dfo.Server.Application.Commands.INetworkConnectionLifecycleHandler
    {
        public ValueTask ConnectedAsync(Dfo.Server.Application.Commands.CommandExecutionContext execution, NetworkConnectionContext connection, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask DisconnectedAsync(Dfo.Server.Application.Commands.CommandExecutionContext execution, NetworkConnectionContext connection, CancellationToken cancellationToken)
        {
            probe.LifecycleSeen.Enqueue(Probe.Here("disconnected"));
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>Records what the outboxes hand over; carrying it out is the real sender's job, tested elsewhere.</summary>
    private sealed class ChainSender : IOutboundSender
    {
        public ConcurrentQueue<OutboundEntry> Entries { get; } = new();

        public void Submit(IReadOnlyList<OutboundEntry> entries)
        {
            foreach (var entry in entries) Entries.Enqueue(entry);
        }
    }

    private sealed class ChainContexts : INetworkConnectionContexts
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
}
