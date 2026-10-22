using System.Collections.Concurrent;
using System.Threading.Channels;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Commands.Pipeline;
using Dfo.Server.Application.DomainEvents;
using Dfo.Server.Application.Execution;
using Dfo.Server.Application.Tests.Execution;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Persistence.Database;
using Dfo.Server.Persistence.DomainEvents;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.DomainEvents;

public sealed partial class DomainEventDispatchTests
{
    [Fact]
    public async Task Enqueue_requires_a_writable_transaction_and_never_records_rejected_facts()
    {
        await using var f = new Fixture();
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Events.EnqueueAsync(new Awarded("a", 2)).AsTask());
        await using (var tx = f.Scope.Begin(readOnly: true))
            await Assert.ThrowsAsync<InvalidOperationException>(() => f.Events.EnqueueAsync(new Awarded("a", 2)).AsTask());
        Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM domain_events"));
    }

    [Fact]
    public async Task Business_write_and_all_event_rows_roll_back_together()
    {
        await using var f = new Fixture();
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            await using (var tx = f.Scope.Begin())
            {
                await f.Probe.WriteAsync("business", 10, default);
                await f.Events.EnqueueAsync(new Awarded("a", 2));
            }
        });
        Assert.Equal(0, f.Count("business"));
        Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM domain_events"));
        Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM domain_event_deliveries"));
        Assert.Equal(0, await f.Dispatcher.DrainOnceAsync());
    }

    [Fact]
    public async Task Each_fact_is_retained_with_one_transaction_batch_and_an_independent_delivery_per_subscriber()
    {
        await using var f = new Fixture();
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            await using (var tx = f.Scope.Begin())
            {
                await f.Probe.WriteAsync("business", 10, default);
                Assert.NotEqual(await f.Events.EnqueueAsync(new Awarded("a", 2)),
                    await f.Events.EnqueueAsync(new Awarded("a", 3)));
                await tx.CommitAsync(default);
            }
        });
        Assert.Equal(10, f.Count("business"));
        Assert.Equal(2, f.Scalar("SELECT COUNT(*) FROM domain_events"));
        Assert.Equal(1, f.Scalar("SELECT COUNT(DISTINCT batch_id) FROM domain_events"));
        Assert.Equal(6, f.Scalar("SELECT COUNT(*) FROM domain_event_deliveries"));
        Assert.Equal(6, await f.Dispatcher.DrainOnceAsync());
        Assert.Equal(5, f.Count("counter"));
        Assert.Equal(5, f.Count("audit"));
        Assert.Single(f.Probe.Live);
        Assert.Equal(0, await f.Dispatcher.DrainOnceAsync());
        await f.SendAsync(new Awarded("a", 7));
        await f.Dispatcher.DrainOnceAsync();
        Assert.Equal(2, f.Probe.Live.Count);
        Assert.Equal(2, f.Scalar("SELECT COUNT(DISTINCT batch_id) FROM domain_events"));
    }

    [Fact]
    public async Task Failed_reliable_handler_rolls_back_its_work_and_does_not_replay_successful_subscribers()
    {
        await using var f = new Fixture();
        f.Clock.Frozen = DateTimeOffset.UtcNow;
        f.Probe.FailingAttempts = 1;
        await f.SendAsync(new Awarded("a", 4));
        Assert.Equal(3, await f.Dispatcher.DrainOnceAsync());
        Assert.Equal(0, f.Count("counter"));
        Assert.Equal(4, f.Count("audit"));
        Assert.Equal(1, f.Scalar("SELECT COUNT(*) FROM domain_event_deliveries WHERE state=0"));
        Assert.Equal(0, await f.Dispatcher.DrainOnceAsync());
        f.Clock.Frozen += TimeSpan.FromMinutes(1);
        Assert.Equal(1, await f.Dispatcher.DrainOnceAsync());
        Assert.Equal(4, f.Count("counter"));
        Assert.Equal(4, f.Count("audit"));
        Assert.Equal(0, await f.Dispatcher.DrainOnceAsync());
    }

    [Fact]
    public async Task Failed_handler_is_retained_as_terminal_after_the_configured_attempt_limit()
    {
        await using var f = new Fixture(options: DomainEventDispatchOptions.Default with { MaximumAttempts = 2 });
        f.Clock.Frozen = DateTimeOffset.UtcNow;
        f.Probe.FailingAttempts = 10;
        await f.SendAsync(new Awarded("a", 4));
        await f.Dispatcher.DrainOnceAsync();
        f.Clock.Frozen += TimeSpan.FromMinutes(1);
        await f.Dispatcher.DrainOnceAsync();
        Assert.Equal(0, f.Count("counter"));
        Assert.Equal(1, f.Scalar("SELECT COUNT(*) FROM domain_event_deliveries WHERE subscriber_id='counter' AND state=2 AND attempt_count=2 AND length(last_error)>0"));
        Assert.Equal(1, f.Scalar("SELECT COUNT(*) FROM domain_events"));
        f.Clock.Frozen += TimeSpan.FromDays(1);
        Assert.Equal(0, await f.Dispatcher.DrainOnceAsync());
    }

    [Fact]
    public async Task Different_payload_types_rehydrate_through_the_same_typed_subscriber_group()
    {
        await using var f = new Fixture();
        await f.SendAsync(new Awarded("a", 2));
        await f.SendAsync(new QuestFinished(33, 8));
        await f.Dispatcher.DrainOnceAsync();
        Assert.Equal(10, f.Count("counter"));
        Assert.Contains(33, f.Probe.Quests);
        Assert.Equal(2, f.Scalar("SELECT COUNT(DISTINCT event_type) FROM domain_events"));
    }

    [Fact]
    public async Task Restart_recovers_reliable_facts_and_consumes_old_live_hints_without_replaying_them()
    {
        await using var f = new Fixture();
        await f.SendAsync(new Awarded("a", 5));
        await using var restored = new Fixture(path: f.Path);
        Assert.Equal(3, await restored.Dispatcher.DrainOnceAsync());
        Assert.Equal(5, restored.Count("counter"));
        Assert.Equal(5, restored.Count("audit"));
        Assert.Empty(restored.Probe.Live);
        Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM domain_event_deliveries WHERE state=0"));
    }

    [Fact]
    public async Task Expired_live_hints_do_not_prevent_reliable_processing()
    {
        await using var f = new Fixture();
        f.Clock.Frozen = DateTimeOffset.UtcNow;
        await f.SendAsync(new Awarded("a", 5));
        f.Clock.Frozen += TimeSpan.FromMinutes(2);
        await f.Dispatcher.DrainOnceAsync();
        Assert.Equal(5, f.Count("counter"));
        Assert.Empty(f.Probe.Live);
    }

    [Theory]
    [InlineData("UPDATE domain_events SET schema_version=99")]
    [InlineData("UPDATE domain_events SET event_type='unrecognized'")]
    [InlineData("UPDATE domain_events SET payload_json='not-json'")]
    public async Task Unreadable_events_are_retained_with_diagnostic_delivery_states(string corrupt)
    {
        await using var f = new Fixture();
        await f.SendAsync(new Awarded("a", 5));
        f.Sql(corrupt);
        Assert.Equal(3, await f.Dispatcher.DrainOnceAsync());
        Assert.Equal(1, f.Scalar("SELECT COUNT(*) FROM domain_events"));
        Assert.Equal(3, f.Scalar("SELECT COUNT(*) FROM domain_event_deliveries WHERE state=2 AND length(last_error)>0"));
        Assert.Equal(0, f.Count("counter"));
        Assert.Empty(f.Probe.Live);
    }

    [Fact]
    public async Task Commit_under_database_admission_returns_and_the_worker_consumes_after_admission_is_released()
    {
        await using var f = new Fixture();
        using var stop = new CancellationTokenSource();
        var worker = f.Dispatcher.RunAsync(stop.Token);
        await using (var admission = await f.Services.GetRequiredService<TransactionalExecutionGate>().AcquireAsync(default))
        await using (var tx = f.Scope.Begin())
        {
            await f.Events.EnqueueAsync(new Awarded("a", 3));
            await tx.CommitAsync(default).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, f.Count("counter"));
        }
        await f.Probe.CounterCommitted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await f.Probe.AuditCommitted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await stop.CancelAsync();
        await worker.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(3, f.Count("counter"));
    }

    [Fact]
    public async Task Worker_retries_at_due_time_without_another_enqueue()
    {
        await using var f = new Fixture(options: DomainEventDispatchOptions.Default with
        { RetryDelay = TimeSpan.FromMilliseconds(20), MaximumRetryDelay = TimeSpan.FromMilliseconds(100) });
        f.Probe.FailingAttempts = 1;
        await f.SendAsync(new Awarded("a", 6));
        using var stop = new CancellationTokenSource();
        var worker = f.Dispatcher.RunAsync(stop.Token);
        await f.Probe.CounterCommitted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await f.Probe.AuditCommitted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await stop.CancelAsync();
        await worker.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(6, f.Count("counter"));
        Assert.Equal(2, f.Probe.CounterAttempts);
        Assert.Equal(6, f.Count("audit"));
        Assert.Equal(0, f.Clock.ActiveTimers);
    }

    [Fact]
    public async Task A_failure_to_record_retry_state_backs_off_and_recovers_the_still_pending_fact()
    {
        await using var f = new Fixture(options: DomainEventDispatchOptions.Default with
        { RetryDelay = TimeSpan.FromMilliseconds(30), MaximumRetryDelay = TimeSpan.FromMilliseconds(100) });
        f.Probe.FailingAttempts = 1;
        f.Probe.FailRetryRecordingOnce = 1;
        await f.SendAsync(new Awarded("a", 8));
        using var stop = new CancellationTokenSource();
        var worker = f.Dispatcher.RunAsync(stop.Token);
        await f.Probe.RetryRecordingRejected.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await f.Probe.CounterCommitted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await f.Probe.AuditCommitted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await stop.CancelAsync();
        await worker.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(8, f.Count("counter"));
        Assert.Equal(8, f.Count("audit"));
        Assert.Equal(2, f.Probe.CounterAttempts);
        Assert.Equal(1, f.Scalar("SELECT attempt_count FROM domain_event_deliveries WHERE subscriber_id='counter' AND state=1"));
        Assert.Equal(0, f.Clock.ActiveTimers);
    }

    [Fact]
    public async Task Stop_cancels_inflight_reliable_work_and_leaves_it_pending_for_restart()
    {
        await using var f = new Fixture();
        f.Probe.BlockCounter = true;
        await f.SendAsync(new Awarded("a", 9));
        using var stop = new CancellationTokenSource();
        var worker = f.Dispatcher.RunAsync(stop.Token);
        await f.Probe.CounterEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await stop.CancelAsync();
        await worker.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, f.Count("counter"));
        Assert.Equal(1, f.Scalar("SELECT COUNT(*) FROM domain_event_deliveries WHERE subscriber_id='counter' AND state=0 AND attempt_count=0"));
        await using var restored = new Fixture(path: f.Path);
        await restored.Dispatcher.DrainOnceAsync();
        Assert.Equal(9, restored.Count("counter"));
    }

    /// <summary>
    /// RQ-043: subscriber groups no longer run beside each other, so a slow live hint holds the
    /// executor; the other groups are postponed, not lost, and the hint still runs outside any transaction.
    /// </summary>
    [Fact]
    public async Task A_slow_live_hint_postpones_other_groups_and_still_runs_outside_the_business_transaction()
    {
        await using var f = new Fixture(extraLive: true);
        f.Probe.BlockLive = true;
        await f.SendAsync(new Awarded("a", 7));
        using var stop = new CancellationTokenSource();
        var worker = f.Dispatcher.RunAsync(stop.Token);
        await f.Probe.LiveEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var marker = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var queued = f.Services.GetRequiredService<IGameplayQueue>().RunAsync(new GameplayWork(GameplayShardKey.World,
            GameplayCallChain.NewRoot(GameplayCallSource.Connection), "test:marker", _ =>
            {
                marker.TrySetResult();
                return ValueTask.CompletedTask;
            }), CancellationToken.None).AsTask();
        await Task.Delay(100);
        Assert.False(marker.Task.IsCompleted);
        f.Probe.LiveRelease.TrySetResult();
        await queued.WaitAsync(TimeSpan.FromSeconds(5));
        await f.Probe.FastLive.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await f.Probe.CounterCommitted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await f.Probe.AuditCommitted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(7, f.Count("counter"));
        Assert.Equal(7, f.Count("audit"));
        await f.SendAsync(new Awarded("b", 2)).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        await stop.CancelAsync();
        await worker.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Single(f.Probe.Live, hint => hint.Owner == "a");
        Assert.Equal(0, f.Clock.ActiveTimers);
    }

    [Fact]
    public async Task A_live_failure_is_consumed_once_and_never_retried()
    {
        await using var f = new Fixture();
        f.Probe.FailLive = true;
        await f.SendAsync(new Awarded("a", 1));
        await f.Dispatcher.DrainOnceAsync();
        Assert.Single(f.Probe.Live);
        Assert.Equal(0, await f.Dispatcher.DrainOnceAsync());
        Assert.Equal(1, f.Scalar("SELECT COUNT(*) FROM domain_event_deliveries WHERE subscriber_id='live' AND state=1"));
    }

    [Fact]
    public async Task A_wakeup_disposes_the_losing_retry_timer_before_returning()
    {
        await using var f = new Fixture();
        var channel = System.Threading.Channels.Channel.CreateBounded<byte>(1);
        var waiting = f.Dispatcher.WaitForWakeOrDueAsync(channel.Reader, TimeSpan.FromHours(1), default);
        Assert.Equal(1, f.Clock.ActiveTimers);
        channel.Writer.TryWrite(0);
        await waiting.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, f.Clock.ActiveTimers);
    }

    [Fact]
    public async Task Event_hosted_service_starts_with_no_subscriptions_and_needs_no_database_or_protocol_services()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddEventDispatching();
        await using var provider = services.BuildServiceProvider();
        var hosted = provider.GetServices<IHostedService>().OfType<DomainEventHostedService>().Single();
        await hosted.StartAsync(default);
        await hosted.StopAsync(default);
    }

    internal sealed record Awarded(string Owner, int Amount) : IDomainEvent;
    internal sealed record QuestFinished(int QuestId, int Amount) : IDomainEvent;

    private sealed class CounterSubscriber(Probe probe) : IDomainEventSubscriber<Awarded>, IDomainEventSubscriber<QuestFinished>
    {
        public ValueTask HandleAsync(DomainEventEnvelope<Awarded> e, CancellationToken ct) => ApplyAsync(e.Payload.Amount, ct);
        public ValueTask HandleAsync(DomainEventEnvelope<QuestFinished> e, CancellationToken ct)
        { probe.Quests.Enqueue(e.Payload.QuestId); return ApplyAsync(e.Payload.Amount, ct); }
        private async ValueTask ApplyAsync(int amount, CancellationToken ct)
        {
            probe.CounterAttempts++;
            probe.CounterSeen.Enqueue(Probe.Here());
            await probe.WriteAsync("counter", amount, ct);
            probe.Observe?.Invoke("counter");
            probe.CounterEntered.TrySetResult();
            if (probe.BlockCounter) await Task.Delay(Timeout.Infinite, ct);
            if (probe.FailingAttempts-- > 0) throw new IOException("Injected subscriber failure after writing business data.");
            probe.Transactions.Current!.RegisterAfterCommit("counter-observed", _ =>
            { probe.CounterCommitted.TrySetResult(); return ValueTask.CompletedTask; });
        }
    }

    private sealed class AuditSubscriber(Probe probe) : IDomainEventSubscriber<Awarded>
    {
        public async ValueTask HandleAsync(DomainEventEnvelope<Awarded> e, CancellationToken ct)
        {
            await probe.WriteAsync("audit", e.Payload.Amount, ct);
            probe.Transactions.Current!.RegisterAfterCommit("audit-observed", _ =>
            { probe.AuditCommitted.TrySetResult(); return ValueTask.CompletedTask; });
        }
    }

    private sealed class LiveSubscriber(Probe probe) : IDomainEventSubscriber<Awarded>
    {
        public async ValueTask HandleAsync(DomainEventEnvelope<Awarded> e, CancellationToken ct)
        {
            probe.LiveSeen.Enqueue(Probe.Here() with { InTransaction = probe.Transactions.Current is not null });
            probe.Observe?.Invoke("live");
            Assert.Null(probe.Transactions.Current);
            probe.Live.Enqueue(e.Payload);
            probe.LiveEntered.TrySetResult();
            if (probe.BlockLive) await probe.LiveRelease.Task.WaitAsync(ct);
            if (probe.FailLive) throw new IOException("Injected live failure.");
        }
    }
    private sealed class FastLiveSubscriber(Probe probe) : IDomainEventSubscriber<Awarded>
    {
        public ValueTask HandleAsync(DomainEventEnvelope<Awarded> e, CancellationToken ct)
        { Assert.Null(probe.Transactions.Current); probe.FastLive.TrySetResult(); return ValueTask.CompletedTask; }
    }

    private sealed class DirectSubscriber(Probe probe) : IDomainEventSubscriber<Awarded>
    {
        public ValueTask HandleAsync(DomainEventEnvelope<Awarded> e, CancellationToken ct)
        { probe.DirectSeen.Enqueue(Probe.Here()); return ValueTask.CompletedTask; }
    }

    /// <summary>Where a subscriber or a persistence call ran: on an executor, and as which CMD.</summary>
    private sealed record Seen(string Method, string Subscriber, bool OnExecutor, GameplayCallChain? Current,
        bool InTransaction = false);

    private sealed class Probe(SqliteDatabase database, IDomainTransactionAccessor transactions)
    {
        public static Seen Here(string method = "", string subscriber = "") =>
            new(method, subscriber, GameplayJob.IsOnExecutor, GameplayJob.Current);
        public IDomainTransactionAccessor Transactions => transactions;
        public ConcurrentQueue<Seen> CounterSeen { get; } = [];
        public ConcurrentQueue<Seen> LiveSeen { get; } = [];
        public ConcurrentQueue<Seen> DirectSeen { get; } = [];
        public ConcurrentQueue<Seen> Persisted { get; } = [];
        public Action<string>? Observe { get; set; }
        public TaskCompletionSource LiveRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int FailingAttempts;
        public int CounterAttempts;
        public int FailRetryRecordingOnce;
        public bool BlockCounter, BlockLive, FailLive;
        public ConcurrentQueue<Awarded> Live { get; } = [];
        public ConcurrentQueue<int> Quests { get; } = [];
        public TaskCompletionSource CounterEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CounterCommitted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AuditCommitted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource LiveEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource FastLive { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource RetryRecordingRejected { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ValueTask WriteAsync(string owner, int amount, CancellationToken ct) => database.WriteAsync(async (command, token) =>
        {
            command.CommandText = "INSERT INTO event_test_counter(owner,amount) VALUES(@owner,@amount) ON CONFLICT(owner) DO UPDATE SET amount=amount+@amount";
            command.Parameters.AddWithValue("@owner", owner); command.Parameters.AddWithValue("@amount", amount);
            await command.ExecuteNonQueryAsync(token);
        }, ct);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly bool owns;
        public string Path { get; }
        public ServiceProvider Services { get; }
        public TestClock Clock { get; } = new();
        public DomainTransactionScope Scope => Services.GetRequiredService<DomainTransactionScope>();
        public IEventService Events => Services.GetRequiredService<IEventService>();
        public DomainEventDispatcher Dispatcher => Services.GetRequiredService<DomainEventDispatcher>();
        public Probe Probe => Services.GetRequiredService<Probe>();
        public RecordingGameplayQueue Queue => (RecordingGameplayQueue)Services.GetRequiredService<IGameplayQueue>();
        public LogRecorder Logs { get; } = new();
        public Fixture(string? path = null, DomainEventDispatchOptions? options = null, bool extraLive = false,
            bool record = false)
        {
            owns = path is null;
            Path = path ?? System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dfo-domain-events-" + Guid.NewGuid().ToString("N") + ".db");
            var services = new ServiceCollection().AddLogging();
            services.AddEventDispatching();
            services.AddSingleton<TimeProvider>(Clock);
            if (options is not null) services.AddSingleton(options);
            services.AddSingleton(p => new SqliteDatabase(Path, p.GetRequiredService<IDomainTransactionAccessor>()));
            services.AddSingleton<Probe>();
            services.AddSingleton<IDomainEventPersistence>(p => new FaultingRetryPersistence(
                new SqliteDomainEventPersistence(p.GetRequiredService<SqliteDatabase>()), p.GetRequiredService<Probe>()));
            services.AddDomainEvent<Awarded>("award.granted", 1).AddDomainEvent<QuestFinished>("quest.finished", 1);
            services.AddDomainSubscriber<Awarded, CounterSubscriber>("counter", DomainEventDeliveryPolicy.Reliable);
            services.AddDomainSubscriber<QuestFinished, CounterSubscriber>("counter", DomainEventDeliveryPolicy.Reliable);
            services.AddDomainSubscriber<Awarded, AuditSubscriber>("audit", DomainEventDeliveryPolicy.Reliable);
            services.AddDomainSubscriber<Awarded, LiveSubscriber>("live", DomainEventDeliveryPolicy.LiveOnly, e => e.Owner);
            if (extraLive) services.AddDomainSubscriber<Awarded, FastLiveSubscriber>("fast-live", DomainEventDeliveryPolicy.LiveOnly);
            if (record)
            {
                services.AddSingleton<IGameplayQueue>(p =>
                    new RecordingGameplayQueue(new GameplayQueue(p.GetRequiredService<GameplayShardSelector>())));
                services.AddSingleton<ILoggerProvider>(Logs);
                services.AddRuntimeEventSubscriber<Awarded, DirectSubscriber>();
            }
            Services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            _ = Services.GetRequiredService<SqliteDatabase>();
            Sql("CREATE TABLE IF NOT EXISTS event_test_counter(owner TEXT PRIMARY KEY,amount INTEGER NOT NULL)");
        }
        public async ValueTask SendAsync<TEvent>(TEvent e) where TEvent : class, IDomainEvent
        {
            await TestGameplay.RunAsync(Services, async () =>
            {
                await using var admission = await Services.GetRequiredService<TransactionalExecutionGate>().AcquireAsync(default);
                await using var tx = Scope.Begin();
                await Events.EnqueueAsync(e);
                await tx.CommitAsync(default);
            });
        }
        public long Count(string owner) => Scalar($"SELECT COALESCE((SELECT amount FROM event_test_counter WHERE owner='{owner}'),0)");
        public long Scalar(string sql)
        {
            using var connection = new SqliteConnection("Data Source=" + Path); connection.Open();
            using var command = connection.CreateCommand(); command.CommandText = sql;
            return Convert.ToInt64(command.ExecuteScalar());
        }
        public void Sql(string sql)
        {
            using var connection = new SqliteConnection("Data Source=" + Path); connection.Open();
            using var command = connection.CreateCommand(); command.CommandText = sql; command.ExecuteNonQuery();
        }
        public async ValueTask DisposeAsync()
        {
            await Services.DisposeAsync();
            if (!owns) return;
            using var connection = new SqliteConnection("Data Source=" + Path); SqliteConnection.ClearPool(connection);
            foreach (var suffix in new[] { "", "-wal", "-shm" }) File.Delete(Path + suffix);
        }
    }

    internal sealed class TestClock : TimeProvider
    {
        public DateTimeOffset? Frozen { get; set; }
        private int timers;
        public int ActiveTimers => Volatile.Read(ref timers);
        public override DateTimeOffset GetUtcNow() => Frozen ?? DateTimeOffset.UtcNow;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Interlocked.Increment(ref timers);
            return new TrackedTimer(System.CreateTimer(callback, state, dueTime, period), this);
        }
        private sealed class TrackedTimer(ITimer inner, TestClock owner) : ITimer
        {
            private int disposed;
            public bool Change(TimeSpan dueTime, TimeSpan period) => inner.Change(dueTime, period);
            public void Dispose() { inner.Dispose(); if (Interlocked.Exchange(ref disposed, 1) == 0) Interlocked.Decrement(ref owner.timers); }
            public async ValueTask DisposeAsync() { await inner.DisposeAsync(); if (Interlocked.Exchange(ref disposed, 1) == 0) Interlocked.Decrement(ref owner.timers); }
        }
    }

    private sealed class FaultingRetryPersistence(IDomainEventPersistence inner, Probe probe) : IDomainEventPersistence
    {
        public ValueTask AppendAsync(StoredDomainEvent e, IReadOnlyList<DomainEventDeliveryTarget> s, CancellationToken ct) => inner.AppendAsync(e, s, ct);
        public ValueTask<IReadOnlyList<PendingDomainEventDelivery>> ReadDueAsync(string id, DateTimeOffset now, int limit, CancellationToken ct) => inner.ReadDueAsync(id, now, limit, ct);
        public ValueTask<DateTimeOffset?> NextDueAsync(string id, CancellationToken ct) => inner.NextDueAsync(id, ct);
        public ValueTask<bool> TryBeginAsync(Guid id, string s, DateTimeOffset now, CancellationToken ct)
        { probe.Persisted.Enqueue(Probe.Here("TryBegin", s)); return inner.TryBeginAsync(id, s, now, ct); }
        public ValueTask<bool> HasCompletedCoalescingKeyAsync(Guid batch, string s, string key, CancellationToken ct) => inner.HasCompletedCoalescingKeyAsync(batch, s, key, ct);
        public ValueTask CompleteAsync(Guid id, string s, DateTimeOffset now, string reason, CancellationToken ct)
        { probe.Persisted.Enqueue(Probe.Here("Complete", s)); return inner.CompleteAsync(id, s, now, reason, ct); }
        public ValueTask FailAsync(Guid id, string s, DateTimeOffset next, string error, bool terminal, CancellationToken ct)
        {
            probe.Persisted.Enqueue(Probe.Here("Fail", s));
            if (s == "counter" && Interlocked.Exchange(ref probe.FailRetryRecordingOnce, 0) != 0)
            {
                probe.RetryRecordingRejected.TrySetResult();
                throw new IOException("Injected failure while recording retry state.");
            }
            return inner.FailAsync(id, s, next, error, terminal, ct);
        }
    }
}
