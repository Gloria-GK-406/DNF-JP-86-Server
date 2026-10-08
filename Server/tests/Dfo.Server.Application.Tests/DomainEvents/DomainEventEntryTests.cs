using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.DomainEvents;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.DomainEvents;

public sealed class DomainEventEntryTests
{
    [Fact]
    public async Task Immediate_projection_waits_and_preserves_the_exact_delivery_result_without_persistence()
    {
        var probe = new ProjectionProbe();
        var services = new ServiceCollection().AddLogging();
        services.AddEventDispatching().AddSingleton<INotificationBus>(probe);
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var audience = new NotificationAudience.TheseCharacters([new CharacterId(7)]);
        var notification = new TestNotification(123);
        var publish = provider.GetRequiredService<IEventService>().ProjectAsync(audience, notification).AsTask();
        Assert.False(publish.IsCompleted);
        Assert.Same(audience, probe.Audience);
        Assert.Same(notification, probe.Payload);
        var result = new NotificationDelivery([new(new(7), NotificationOutcome.Gone)]);
        probe.Result.SetResult(result);
        Assert.Same(result, await publish);
        Assert.Null(provider.GetService<IDomainEventPersistence>());
    }

    [Fact]
    public async Task Uncommitted_runtime_facts_and_projections_are_rejected_but_readonly_projections_are_allowed()
    {
        var trace = new RuntimeTrace();
        var probe = new ProjectionProbe();
        var services = new ServiceCollection().AddLogging();
        services.AddEventDispatching().AddSingleton(trace).AddSingleton<INotificationBus>(probe);
        services.AddRuntimeEventSubscriber<RuntimeAward, RuntimeSubscriber>();
        await using var provider = services.BuildServiceProvider();
        var events = provider.GetRequiredService<IEventService>();
        var scope = provider.GetRequiredService<DomainTransactionScope>();
        var audience = new NotificationAudience.TheseCharacters([]);
        await using (var transaction = scope.Begin())
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => events.PublishAsync(new RuntimeAward(2)).AsTask());
            await Assert.ThrowsAsync<InvalidOperationException>(() => events.ProjectAsync(audience, new TestNotification(1)).AsTask());
        }
        Assert.Empty(trace.Events);
        Assert.Null(probe.Payload);
        probe.Result.SetResult(new([]));
        await using (var query = scope.Begin(readOnly: true))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => events.PublishAsync(new RuntimeAward(2)).AsTask());
            await events.ProjectAsync(audience, new TestNotification(1));
            await query.CommitAsync(default);
        }
        await events.PublishAsync(new RuntimeAward(3));
        Assert.Single(trace.Events);
    }

    [Fact]
    public async Task Runtime_subscribers_receive_one_typed_envelope_and_a_failure_does_not_skip_other_subscribers()
    {
        var trace = new RuntimeTrace { FailFirst = true };
        var services = new ServiceCollection().AddLogging();
        services.AddEventDispatching().AddSingleton(trace);
        services.AddRuntimeEventSubscriber<RuntimeAward, RuntimeSubscriber>();
        services.AddRuntimeEventSubscriber<RuntimeAward, OtherRuntimeSubscriber>();
        services.AddRuntimeEventSubscriber<OtherRuntimeAward, OtherTypedRuntimeSubscriber>();
        await using var provider = services.BuildServiceProvider();
        var events = provider.GetRequiredService<IEventService>();
        await Assert.ThrowsAsync<AggregateException>(() => events.PublishAsync(new RuntimeAward(4)).AsTask());
        Assert.Equal(2, trace.Events.Count);
        Assert.Equal(trace.Events[0].EventId, trace.Events[1].EventId);
        Assert.Equal(trace.Events[0].Payload, trace.Events[1].Payload);
        await events.PublishAsync(new OtherRuntimeAward("done"));
        Assert.Equal("done", trace.OtherPayload);
    }

    [Fact]
    public async Task Runtime_publish_awaits_the_consumer()
    {
        var trace = new RuntimeTrace { Wait = true };
        var services = new ServiceCollection().AddLogging();
        services.AddEventDispatching().AddSingleton(trace);
        services.AddRuntimeEventSubscriber<RuntimeAward, RuntimeSubscriber>();
        await using var provider = services.BuildServiceProvider();
        var pending = provider.GetRequiredService<IEventService>().PublishAsync(new RuntimeAward(3)).AsTask();
        Assert.False(pending.IsCompleted);
        trace.Release.TrySetResult();
        await pending;
        Assert.Single(trace.Events);
    }

    [Theory]
    [InlineData("type")]
    [InlineData("name")]
    [InlineData("versions")]
    [InlineData("subscription")]
    [InlineData("runtime")]
    [InlineData("group-handler")]
    [InlineData("group-policy")]
    [InlineData("missing-event")]
    public void Ambiguous_registration_fails_before_any_subscriber_is_resolved(string duplicate)
    {
        var services = new ServiceCollection().AddLogging();
        services.AddEventDispatching();
        services.AddDomainEvent<RuntimeAward>("award", 1);
        services.AddDomainEvent<OtherRuntimeAward>("other", 1);
        services.AddDomainSubscriber<RuntimeAward, RuntimeSubscriber>("one", DomainEventDeliveryPolicy.Reliable);
        switch (duplicate)
        {
            case "type": services.AddDomainEvent<RuntimeAward>("award-renamed", 1); break;
            case "name": services.AddDomainEvent<TestAward>("award", 1); break;
            case "versions": services.AddDomainEvent<TestAward>("award", 2); break;
            case "subscription": services.AddDomainSubscriber<RuntimeAward, RuntimeSubscriber>("one", DomainEventDeliveryPolicy.Reliable); break;
            case "runtime":
                services.AddRuntimeEventSubscriber<RuntimeAward, RuntimeSubscriber>();
                services.AddRuntimeEventSubscriber<RuntimeAward, RuntimeSubscriber>(); break;
            case "group-handler": services.AddDomainSubscriber<OtherRuntimeAward, OtherTypedRuntimeSubscriber>("one", DomainEventDeliveryPolicy.Reliable); break;
            case "group-policy": services.AddDomainSubscriber<OtherRuntimeAward, RuntimeSubscriber>("one", DomainEventDeliveryPolicy.LiveOnly); break;
            case "missing-event": services.AddDomainSubscriber<TestAward, TestAwardSubscriber>("missing", DomainEventDeliveryPolicy.Reliable); break;
        }
        using var provider = services.BuildServiceProvider();
        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IEventService>());
    }

    [Theory]
    [InlineData("session")]
    [InlineData("nested-session")]
    [InlineData("network")]
    [InlineData("service")]
    [InlineData("delegate")]
    [InlineData("provider")]
    [InlineData("notification")]
    public void Domain_payloads_cannot_hide_live_sessions_protocol_messages_or_capabilities(string kind)
    {
        var services = new ServiceCollection().AddLogging();
        services.AddEventDispatching();
        switch (kind)
        {
            case "session": services.AddDomainEvent<SessionEvent>("invalid", 1); break;
            case "nested-session": services.AddDomainEvent<NestedEvent>("invalid", 1); break;
            case "network": services.AddDomainEvent<NetworkEvent>("invalid", 1); break;
            case "service": services.AddDomainEvent<ServiceEvent>("invalid", 1); break;
            case "delegate": services.AddDomainEvent<DelegateEvent>("invalid", 1); break;
            case "provider": services.AddDomainEvent<ProviderEvent>("invalid", 1); break;
            case "notification": services.AddDomainEvent<NotificationEvent>("invalid", 1); break;
        }
        using var provider = services.BuildServiceProvider();
        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IEventService>());
    }

    [Fact]
    public void Reliable_subscribers_cannot_reenter_the_command_pipeline()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddEventDispatching().AddDomainEvent<RuntimeAward>("award", 1);
        services.AddDomainSubscriber<RuntimeAward, ReentrantSubscriber>("bad", DomainEventDeliveryPolicy.Reliable);
        using var provider = services.BuildServiceProvider();
        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IEventService>());
    }

    [Fact]
    public void Reliable_subscriptions_cannot_coalesce_and_discard_business_facts()
    {
        var services = new ServiceCollection();
        Assert.Throws<ArgumentException>(() => services.AddDomainSubscriber<RuntimeAward, RuntimeSubscriber>(
            "bad", DomainEventDeliveryPolicy.Reliable, e => e.Amount.ToString()));
    }

    private sealed record RuntimeAward(int Amount) : IDomainEvent;
    private sealed record OtherRuntimeAward(string Text) : IDomainEvent;
    private sealed record TestAward(int Amount) : IDomainEvent;
    private sealed record TestNotification(int Value) : INotification<TestNotification>;
    private sealed record SessionEvent(SelectedSessionIdentity Session) : IDomainEvent;
    private sealed record NestedEvent(SessionList Nested) : IDomainEvent;
    private sealed record SessionList(IReadOnlyList<SelectedSessionIdentity> Sessions);
    private sealed record NetworkEvent(IServerNetworkMessage Reply) : IDomainEvent;
    private sealed record ServiceEvent(IEventService Service) : IDomainEvent;
    private sealed record DelegateEvent(Action Action) : IDomainEvent;
    private sealed record ProviderEvent(IServiceProvider Provider) : IDomainEvent;
    private sealed record NotificationEvent(TestNotification Notification) : IDomainEvent;

    private sealed class RuntimeTrace
    {
        public bool FailFirst, Wait;
        public List<DomainEventEnvelope<RuntimeAward>> Events { get; } = [];
        public string? OtherPayload;
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    private sealed class RuntimeSubscriber(RuntimeTrace trace) : IDomainEventSubscriber<RuntimeAward>, IDomainEventSubscriber<OtherRuntimeAward>
    {
        public async ValueTask HandleAsync(DomainEventEnvelope<RuntimeAward> e, CancellationToken ct)
        {
            if (trace.Wait) await trace.Release.Task.WaitAsync(ct);
            trace.Events.Add(e);
            if (trace.FailFirst) throw new IOException("First runtime subscriber failed.");
        }
        public ValueTask HandleAsync(DomainEventEnvelope<OtherRuntimeAward> e, CancellationToken ct)
        { trace.OtherPayload = e.Payload.Text; return ValueTask.CompletedTask; }
    }
    private sealed class OtherRuntimeSubscriber(RuntimeTrace trace) : IDomainEventSubscriber<RuntimeAward>
    { public ValueTask HandleAsync(DomainEventEnvelope<RuntimeAward> e, CancellationToken ct) { trace.Events.Add(e); return ValueTask.CompletedTask; } }
    private sealed class OtherTypedRuntimeSubscriber(RuntimeTrace trace) : IDomainEventSubscriber<OtherRuntimeAward>
    { public ValueTask HandleAsync(DomainEventEnvelope<OtherRuntimeAward> e, CancellationToken ct) { trace.OtherPayload = e.Payload.Text; return ValueTask.CompletedTask; } }
    private sealed class TestAwardSubscriber : IDomainEventSubscriber<TestAward>
    { public ValueTask HandleAsync(DomainEventEnvelope<TestAward> e, CancellationToken ct) => ValueTask.CompletedTask; }
    private sealed class ReentrantSubscriber(ICommandBus bus) : IDomainEventSubscriber<RuntimeAward>
    {
        private readonly ICommandBus commands = bus;
        public ValueTask HandleAsync(DomainEventEnvelope<RuntimeAward> e, CancellationToken ct)
        { GC.KeepAlive(commands); return ValueTask.CompletedTask; }
    }
    private sealed class ProjectionProbe : INotificationBus
    {
        public NotificationAudience? Audience;
        public object? Payload;
        public TaskCompletionSource<NotificationDelivery> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ValueTask<NotificationDelivery> PublishAsync<T>(NotificationAudience audience, T payload, CancellationToken ct)
            where T : class, INotification<T>
        { Audience = audience; Payload = payload; return new(Result.Task.WaitAsync(ct)); }
    }
}
