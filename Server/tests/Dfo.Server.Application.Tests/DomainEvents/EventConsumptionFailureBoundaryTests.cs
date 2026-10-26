using Dfo.Server.Abstractions.DomainEvents;
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
        if (success) Assert.Null(error);
        else Assert.True(Contains(error!, state.Encoding), error?.ToString());
        Assert.Equal(policy == DomainEventDeliveryPolicy.LiveOnly ? 1 : 0,
            f.Scalar("SELECT state FROM domain_event_deliveries WHERE subscriber_id='test.failure-boundary'"));
        Assert.Equal(policy == DomainEventDeliveryPolicy.LiveOnly ? 1 : 0, f.Scalar("SELECT attempt_count FROM domain_event_deliveries WHERE subscriber_id='test.failure-boundary'"));
        if (mode.StartsWith("notification", StringComparison.Ordinal)) Assert.Equal(NotificationOutcome.Failed, state.Notification);
        Assert.Empty(f.Sender.Messages);
    }
    [Theory]
    [InlineData(DomainEventDeliveryPolicy.Reliable, false, 1)]
    [InlineData(DomainEventDeliveryPolicy.LiveOnly, false, 1)]
    [InlineData(DomainEventDeliveryPolicy.Reliable, true, 0)]
    public async Task Subscriber_scope_cleanup_keeps_its_actual_phase_and_never_records_an_ordinary_retry(DomainEventDeliveryPolicy policy, bool bodyFails, int durableAtDispose)
    {
        var state = new State("scope") { BodyFails = bodyFails };
        using var f = Build(state, policy);
        state.Session = f.Session;
        state.ObserveDispose = () => f.Scalar("SELECT state FROM domain_event_deliveries WHERE subscriber_id='test.failure-boundary'");
        await Publish(f);
        var error = await Record.ExceptionAsync(() => f.Services.GetRequiredService<DomainEventDispatcher>().DrainOnceAsync("test.failure-boundary").AsTask());
        Assert.NotNull(error); Assert.True(DomainEventScopeCleanupException.Contains(error)); Assert.True(Contains(error, state.Cleanup));
        if (bodyFails) Assert.True(Contains(error, state.Body));
        Assert.Equal(durableAtDispose, state.DurableAtDispose);
        Assert.Equal(1, state.Disposals);
        Assert.Equal(bodyFails ? 0 : 1, f.Scalar("SELECT attempt_count FROM domain_event_deliveries WHERE subscriber_id='test.failure-boundary'"));
    }
    private static ShopFixture Build(State state, DomainEventDeliveryPolicy policy) => new(services =>
    {
        services.AddSingleton(state);
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
    private sealed record Fact : IDomainEvent;
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
    }
    private sealed class Subscriber(State state, IEventService events, ICommandMessageOutbox outbox) : IDomainEventSubscriber<Fact>, IAsyncDisposable
    {
        public async ValueTask HandleAsync(DomainEventEnvelope<Fact> envelope, CancellationToken token)
        {
            if (state.BodyFails) throw state.Body;
            if (state.Mode.StartsWith("notification", StringComparison.Ordinal))
            {
                var result = await events.ProjectAsync(new NotificationAudience.TheseSessions([state.Session]), new Notice(), token);
                state.Notification = Assert.Single(result.Attempts).Outcome;
                if (state.Mode == "notification") return;
            }
            if (state.Mode == "scope") return;
            try { await outbox.RegisterAsync(state.Session.ConnectionId, new FatigueNotification(new(156, 1)), token); }
            catch (IOException) when (state.Mode == "required-swallowed") { }
        }
        public ValueTask DisposeAsync()
        {
            state.Disposals++;
            if (state.Mode != "scope") return ValueTask.CompletedTask;
            state.DurableAtDispose = state.ObserveDispose!();
            return ValueTask.FromException(state.Cleanup);
        }
    }
    private sealed class Composer : INotificationHandler<Notice>
    {
        public ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(NotificationRecipient recipient, Notice notice, CancellationToken token) =>
            ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>([new FatigueNotification(new(156, 1))]);
    }
}
