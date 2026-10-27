using System.Collections.Concurrent;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Execution;
using Dfo.Server.Application.Tests.Execution;
using Microsoft.Extensions.Logging;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Application.DomainEvents;
using Dfo.Server.Application.Notifications;
using Dfo.Server.Application.Tests.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.Fatigue;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;
namespace Dfo.Server.Application.Tests.DomainEvents;
public sealed class EventConsumptionFailureBoundaryTests
{
    [Theory]
    [InlineData("required-swallowed", DomainEventDeliveryPolicy.Reliable, false)]
    [InlineData("required-thrown", DomainEventDeliveryPolicy.Reliable, false)]
    [InlineData("notification", DomainEventDeliveryPolicy.LiveOnly, true)]
    [InlineData("notification-then-required-same", DomainEventDeliveryPolicy.LiveOnly, false)]
    public async Task Only_the_fixed_recipient_boundary_handles_its_exact_encoding_occurrence(string mode, DomainEventDeliveryPolicy policy, bool success)
    {
        var state = new State(mode);
        using var f = Build(state, policy);
        state.Session = f.Session;
        f.Sender.FailSends(() => state.Encoding);
        await Publish(f);
        var error = await Record.ExceptionAsync(() => f.Services.GetRequiredService<DomainEventDispatcher>().DrainOnceAsync("test.failure-boundary").AsTask());
        if (success || policy == DomainEventDeliveryPolicy.Reliable) Assert.Null(error);
        else Assert.True(Contains(error!, state.Encoding), error?.ToString());
        if (!success) Assert.True(Contains(Assert.Single(state.Failures), state.Encoding));
        Assert.Equal(policy == DomainEventDeliveryPolicy.LiveOnly ? 1 : 0,
            f.Scalar("SELECT state FROM domain_event_deliveries WHERE subscriber_id='test.failure-boundary'"));
        Assert.Equal(1, f.Scalar("SELECT attempt_count FROM domain_event_deliveries WHERE subscriber_id='test.failure-boundary'"));
        if (mode.StartsWith("notification", StringComparison.Ordinal)) Assert.Equal(NotificationOutcome.Failed, state.Notification);
        Assert.Empty(f.Sender.Messages);
    }
    [Theory]
    [InlineData(DomainEventDeliveryPolicy.Reliable, false, 1)]
    [InlineData(DomainEventDeliveryPolicy.LiveOnly, false, 1)]
    [InlineData(DomainEventDeliveryPolicy.Reliable, true, 0)]
    public async Task Subscriber_scope_cleanup_keeps_its_actual_phase_and_never_replays_a_committed_delivery(DomainEventDeliveryPolicy policy, bool bodyFails, int durableAtDispose)
    {
        var state = new State("scope") { BodyFails = bodyFails };
        using var f = Build(state, policy);
        state.Session = f.Session;
        state.ObserveDispose = () => f.Scalar("SELECT state FROM domain_event_deliveries WHERE subscriber_id='test.failure-boundary'");
        await Publish(f);
        var error = await Record.ExceptionAsync(() => f.Services.GetRequiredService<DomainEventDispatcher>().DrainOnceAsync("test.failure-boundary").AsTask());
        if (policy == DomainEventDeliveryPolicy.Reliable) Assert.Null(error); else Assert.NotNull(error);
        var failedJob = Assert.Single(state.Failures);
        Assert.True(DomainEventScopeCleanupException.Contains(failedJob)); Assert.True(Contains(failedJob, state.Cleanup));
        if (bodyFails) Assert.True(Contains(failedJob, state.Body));
        Assert.Equal(durableAtDispose, state.DurableAtDispose);
        Assert.Equal(1, state.Disposals);
        Assert.Equal(1, f.Scalar("SELECT attempt_count FROM domain_event_deliveries WHERE subscriber_id='test.failure-boundary'"));
        Assert.Equal(bodyFails ? 2 : 1, f.Scalar("SELECT state FROM domain_event_deliveries WHERE subscriber_id='test.failure-boundary'"));
        Assert.Equal(0, await f.Services.GetRequiredService<DomainEventDispatcher>().DrainOnceAsync("test.failure-boundary"));
    }
    [Theory]
    [InlineData("required-thrown", false)]
    [InlineData("required-swallowed", false)]
    [InlineData("uncertain", true)]
    public async Task A_failed_job_is_settled_in_another_cmd_and_later_facts_in_its_group_still_run(string mode, bool quarantine)
    {
        var state = new State(mode);
        using var f = Build(state, DomainEventDeliveryPolicy.Reliable);
        state.Session = f.Session;
        f.Sender.FailSends(() => state.Encoding);
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            await using var tx = f.Services.GetRequiredService<Dfo.Server.Application.Transactions.DomainTransactionScope>().Begin();
            var events = f.Services.GetRequiredService<IEventService>();
            await events.EnqueueAsync(new Fact());
            await events.EnqueueAsync(new Fact(false));
            await tx.CommitAsync(default);
        });
        var dispatcher = f.Services.GetRequiredService<DomainEventDispatcher>();
        Assert.Equal(2, await dispatcher.DrainOnceAsync("test.failure-boundary"));
        Assert.Equal(1, state.Healthy);
        Assert.Single(state.Failures);
        Assert.Equal(1, f.Scalar("SELECT COUNT(*) FROM domain_event_deliveries WHERE state=1 AND attempt_count=1"));
        Assert.Equal(1, f.Scalar($"SELECT COUNT(*) FROM domain_event_deliveries WHERE state={(quarantine ? 2 : 0)} AND attempt_count=1"));
        Assert.Equal(0, await dispatcher.DrainOnceAsync("test.failure-boundary"));
        f.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(quarantine ? 0 : 1, await dispatcher.DrainOnceAsync("test.failure-boundary"));
        Assert.Equal(quarantine ? 2 : 3, state.Invoked);
        Assert.Equal(1, f.Scalar($"SELECT COUNT(*) FROM domain_event_deliveries WHERE state=2 AND attempt_count={(quarantine ? 1 : 2)}"));
        Assert.Equal(0, await dispatcher.DrainOnceAsync("test.failure-boundary"));
        Assert.Equal(1, state.Healthy);
    }

    [Fact]
    public async Task Failure_settlement_cas_cannot_increment_a_backed_off_or_completed_delivery_twice()
    {
        var state = new State("scope");
        using var f = Build(state, DomainEventDeliveryPolicy.Reliable);
        await Publish(f);
        await Publish(f);
        var persistence = f.Services.GetRequiredService<IDomainEventPersistence>();
        var due = await persistence.ReadDueAsync("test.failure-boundary", f.Clock.GetUtcNow(), 10, default);
        Assert.Equal(2, due.Count);
        var delivery = due[0];
        var uncertain = due[1];
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            await using var tx = f.Services.GetRequiredService<Dfo.Server.Application.Transactions.DomainTransactionScope>().Begin();
            Assert.True(await persistence.TryFailAsync(delivery.Event.EventId, delivery.SubscriberId, 0, f.Clock.GetUtcNow().AddMinutes(1), "failed job", false, default));
            Assert.False(await persistence.TryFailAsync(delivery.Event.EventId, delivery.SubscriberId, 0, f.Clock.GetUtcNow().AddMinutes(1), "duplicate", true, default));
            await persistence.CompleteAsync(delivery.Event.EventId, delivery.SubscriberId, f.Clock.GetUtcNow(), "completed", default);
            Assert.False(await persistence.TryFailAsync(delivery.Event.EventId, delivery.SubscriberId, 1, f.Clock.GetUtcNow().AddMinutes(1), "late", true, default));
            Assert.True(await persistence.TryFailAsync(uncertain.Event.EventId, uncertain.SubscriberId, 0, f.Clock.GetUtcNow().AddMinutes(1), "older safe attempt", false, default));
            Assert.True(await persistence.QuarantineAsync(uncertain.Event.EventId, uncertain.SubscriberId, 0, "uncertain attempt overrides retry", default));
            Assert.False(await persistence.QuarantineAsync(uncertain.Event.EventId, uncertain.SubscriberId, 0, "duplicate quarantine", default));
            await tx.CommitAsync(default);
        });
        Assert.Equal(1, f.Scalar("SELECT attempt_count FROM domain_event_deliveries WHERE subscriber_id='test.failure-boundary'"));
        Assert.Equal(1, f.Scalar("SELECT state FROM domain_event_deliveries WHERE subscriber_id='test.failure-boundary' ORDER BY rowid"));
        Assert.Equal(1, f.Scalar("SELECT COUNT(*) FROM domain_event_deliveries WHERE state=2 AND attempt_count=1"));
    }

    [Fact]
    public async Task A_failed_quarantine_job_is_retried_before_the_subscriber_can_run_again()
    {
        var state = new State("uncertain") { RejectFirstSettlement = 1 };
        using var f = Build(state, DomainEventDeliveryPolicy.Reliable);
        await Publish(f);
        var dispatcher = f.Services.GetRequiredService<DomainEventDispatcher>();
        Assert.Same(state.Settlement, await Record.ExceptionAsync(() => dispatcher.DrainOnceAsync("test.failure-boundary").AsTask()));
        Assert.Equal(1, state.Invoked);
        Assert.Equal(0, f.Scalar("SELECT attempt_count FROM domain_event_deliveries WHERE subscriber_id='test.failure-boundary'"));
        Assert.Equal(1, await dispatcher.DrainOnceAsync("test.failure-boundary"));
        Assert.Equal(1, state.Invoked);
        Assert.Equal(2, f.Scalar("SELECT state FROM domain_event_deliveries WHERE subscriber_id='test.failure-boundary'"));
        Assert.Equal(0, await dispatcher.DrainOnceAsync("test.failure-boundary"));
    }

    [Fact]
    public async Task Concurrent_drains_cannot_rerun_an_uncertain_subscriber_already_waiting_for_failure_settlement()
    {
        var state = new State("uncertain");
        using var f = Build(state, DomainEventDeliveryPolicy.Reliable);
        await Publish(f);
        var dispatcher = f.Services.GetRequiredService<DomainEventDispatcher>();
        var queue = Assert.IsType<RecordingGameplayQueue>(f.Services.GetRequiredService<IGameplayQueue>());
        var blocker = new ExecutorBlocker();
        await blocker.HoldAsync(queue, TimeSpan.FromSeconds(5));
        var drains = Enumerable.Range(0, 2).Select(_ => dispatcher.DrainOnceAsync("test.failure-boundary").AsTask()).ToArray();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (queue.EnqueuedCount("event:test.failure-boundary") != 2) await Task.Delay(5, timeout.Token);
        }
        finally { await blocker.ReleaseAsync(TimeSpan.FromSeconds(5)); }
        await Task.WhenAll(drains).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, state.Invoked);
        Assert.Equal(2, f.Scalar("SELECT state FROM domain_event_deliveries WHERE subscriber_id='test.failure-boundary'"));
        Assert.Equal(1, f.Scalar("SELECT attempt_count FROM domain_event_deliveries WHERE subscriber_id='test.failure-boundary'"));
    }

    [Theory]
    [InlineData("cancel-uncertain")]
    [InlineData("cancel-scope")]
    public async Task Requested_cancellation_cannot_erase_a_simultaneous_uncertain_or_scope_cleanup_failure(string mode)
    {
        using var cancellation = new CancellationTokenSource();
        var state = new State(mode) { Cancellation = cancellation };
        using var f = Build(state, DomainEventDeliveryPolicy.Reliable);
        await Publish(f);
        var dispatcher = f.Services.GetRequiredService<DomainEventDispatcher>();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => dispatcher.DrainOnceAsync("test.failure-boundary", cancellation.Token).AsTask());
        Assert.Equal(1, state.Invoked);
        Assert.Equal(1, await dispatcher.DrainOnceAsync("test.failure-boundary"));
        Assert.Equal(1, state.Invoked);
        Assert.Equal(2, f.Scalar("SELECT state FROM domain_event_deliveries WHERE subscriber_id='test.failure-boundary'"));
    }

    private static ShopFixture Build(State state, DomainEventDeliveryPolicy policy) => new(services =>
    {
        services.AddSingleton(state);
        services.AddSingleton<ILogger<GameplayExecutor>>(new FailureLog(state));
        services.AddSingleton<IGameplayQueue>(provider =>
        {
            IGameplayQueue queue = new GameplayQueue(provider.GetRequiredService<GameplayShardSelector>());
            if (state.RejectFirstSettlement != 0) queue = new FailedSettlementQueue(queue, state);
            return new RecordingGameplayQueue(queue);
        });
        services.AddSingleton(DomainEventDispatchOptions.Default with { MaximumAttempts = 2 });
        services.AddDomainEvent<Fact>("test.failure-boundary", 1);
        services.AddDomainSubscriber<Fact, Subscriber>("test.failure-boundary", policy);
        services.AddNotificationHandler<Notice, Composer>();
    });
    private static async Task Publish(ShopFixture f) => await TestGameplay.RunAsync(f.Services, async () =>
    {
        await using var tx = f.Services.GetRequiredService<Dfo.Server.Application.Transactions.DomainTransactionScope>().Begin();
        await f.Services.GetRequiredService<IEventService>().EnqueueAsync(new Fact());
        await tx.CommitAsync(default);
    });
    private static bool Contains(Exception error, Exception expected) => ReferenceEquals(error, expected)
        || error is AggregateException aggregate && aggregate.InnerExceptions.Any(x => Contains(x, expected))
        || error.InnerException is { } inner && Contains(inner, expected);
    private sealed record Fact(bool Poison = true) : IDomainEvent;
    private sealed record Notice : INotification<Notice>;
    private sealed class State(string mode)
    {
        internal string Mode => mode;
        internal SelectedSessionIdentity Session = null!;
        internal readonly IOException Encoding = new("one object, separate actual encoding occurrences");
        internal readonly IOException Body = new("subscriber business action");
        internal readonly IOException Cleanup = new("subscriber scope dispose");
        internal bool BodyFails;
        internal Func<int>? ObserveDispose;
        internal int DurableAtDispose = -1;
        internal int Disposals;
        internal NotificationOutcome? Notification;
        internal ConcurrentQueue<Exception> Failures { get; } = new();
        internal int Healthy, Invoked;
        internal int RejectFirstSettlement;
        internal CancellationTokenSource? Cancellation;
        internal readonly IOException Settlement = new("Injected failed quarantine job");
    }
    private sealed class Subscriber(State state, IEventService events, ICommandMessageOutbox outbox) : IDomainEventSubscriber<Fact>, IAsyncDisposable
    {
        public async ValueTask HandleAsync(DomainEventEnvelope<Fact> envelope, CancellationToken token)
        {
            state.Invoked++;
            if (!envelope.Payload.Poison) { state.Healthy++; return; }
            if (state.Mode.StartsWith("cancel-", StringComparison.Ordinal)) state.Cancellation!.Cancel();
            if (state.Mode is "uncertain" or "cancel-uncertain") throw new TransactionOutcomeUncertainException(
                TransactionUncertaintyReason.RollbackFailure, "Injected uncertain subscriber outcome", state.Body);
            if (state.BodyFails) throw state.Body;
            if (state.Mode.StartsWith("notification", StringComparison.Ordinal))
            {
                var result = await events.ProjectAsync(new NotificationAudience.TheseSessions([state.Session]), new Notice(), token);
                state.Notification = Assert.Single(result.Attempts).Outcome;
                if (state.Mode == "notification") return;
            }
            if (state.Mode is "scope" or "cancel-scope") return;
            try { await outbox.RegisterAsync(state.Session.ConnectionId, new FatigueNotification(new(156, 1)), token); }
            catch (IOException) when (state.Mode == "required-swallowed") { }
        }
        public ValueTask DisposeAsync()
        {
            state.Disposals++;
            if (state.Mode is not ("scope" or "cancel-scope")) return ValueTask.CompletedTask;
            state.DurableAtDispose = state.ObserveDispose?.Invoke() ?? -1;
            return ValueTask.FromException(state.Cleanup);
        }
    }
    private sealed class FailedSettlementQueue(IGameplayQueue inner, State state) : IGameplayQueue
    {
        public ValueTask<T> RunAsync<T>(GameplayWork<T> work, CancellationToken token) =>
            work.CommandType.EndsWith("/failure-settlement", StringComparison.Ordinal) && Interlocked.Exchange(ref state.RejectFirstSettlement, 0) != 0
                ? inner.RunAsync(work with { Body = _ => ValueTask.FromException<T>(state.Settlement) }, token)
                : inner.RunAsync(work, token);
        public ValueTask RunAsync(GameplayWork work, CancellationToken token) => inner.RunAsync(work, token);
    }
    private sealed class FailureLog(State state) : ILogger<GameplayExecutor>
    {
        public IDisposable? BeginScope<TState>(TState value) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState value, Exception? error, Func<TState, Exception?, string> formatter)
        { if (level == LogLevel.Error && error is not null) state.Failures.Enqueue(error); }
    }
    private sealed class Composer : INotificationHandler<Notice>
    {
        public ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(NotificationRecipient recipient, Notice notice, CancellationToken token) =>
            ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>([new FatigueNotification(new(156, 1))]);
    }
}
