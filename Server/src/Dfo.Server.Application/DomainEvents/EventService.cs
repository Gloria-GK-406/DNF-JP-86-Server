using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Transactions;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.DomainEvents;

internal sealed class DomainEventInstance
{
    public Guid Id { get; } = Guid.NewGuid();
}

internal sealed class EventService(DomainEventRegistry registry, IServiceProvider services,
    IDomainTransactionAccessor transactions, DomainEventWakeups wakeups, DomainEventInstance instance,
    TimeProvider clock, IServiceScopeFactory scopes, DomainEventCallChains chains) : IEventService, ICommandProjections
{
    private static readonly object BatchIdentity = new();
    private static readonly object WakeupIdentity = new();

    public async ValueTask<Guid> EnqueueAsync<TEvent>(TEvent payload, CancellationToken cancellationToken = default)
        where TEvent : class, IDomainEvent
    {
        RequireConcrete(payload);
        var transaction = transactions.Current;
        if (transaction is null || transaction.IsReadOnly)
            throw new InvalidOperationException("Enqueue requires the current writable business transaction.");
        var type = registry.For(typeof(TEvent));
        var batch = transaction.Enlist(BatchIdentity, () => new EventBatch(chains));
        var item = new StoredDomainEvent(Guid.NewGuid(), batch.Id, clock.GetUtcNow(), instance.Id,
            type.Name, type.Version, type.Serialize(payload));
        var targets = registry.SubscribersFor(typeof(TEvent))
            .Select(r => new DomainEventDeliveryTarget(r.SubscriberId, r.CoalescingKey(payload))).ToArray();
        await services.GetRequiredService<IDomainEventPersistence>()
            .AppendAsync(item, targets, cancellationToken).ConfigureAwait(false);
        // Each delivery's event CMD is a child of this CMD (RQ-050). Off the executor (only tests publish
        // there) nothing is recorded and the delivery is consumed as a new root.
        if (GameplayJob.Current is { } publisher)
        {
            foreach (var target in targets) batch.Record(item.EventId, target.SubscriberId, publisher);
        }
        transaction.RegisterAfterCommit(WakeupIdentity, _ => { wakeups.WakeAll(); return ValueTask.CompletedTask; });
        return item.EventId;
    }

    public async ValueTask DirectAsync<TEvent>(TEvent payload, CancellationToken cancellationToken = default)
        where TEvent : class, IDomainEvent
    {
        RequireConcrete(payload);
        cancellationToken.ThrowIfCancellationRequested();
        var subscriptions = registry.RuntimeFor(typeof(TEvent));
        var envelope = new DomainEventEnvelope<TEvent>(Guid.NewGuid(), Guid.NewGuid(), clock.GetUtcNow(), instance.Id, payload);
        foreach (var subscriber in subscriptions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // The ambient transaction is inherited by the awaited subscriber. No queue,
            // new transaction, exception aggregation, or background work is introduced.
            await using var scope = scopes.CreateAsyncScope();
            await subscriber.Invoke(scope.ServiceProvider, envelope, cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask<NotificationDelivery> ProjectAsync<TNotification>(NotificationAudience audience,
        TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : class, INotification<TNotification>
    {
        ArgumentNullException.ThrowIfNull(audience);
        ArgumentNullException.ThrowIfNull(notification);
        if (transactions.Current is { IsReadOnly: false })
            throw new InvalidOperationException("Immediate projections cannot observe an uncommitted writable transaction.");
        await using var scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<NotificationProjectionSubscriber<TNotification>>()
            .HandleAsync(audience, notification, cancellationToken).ConfigureAwait(false);
    }

    private static void RequireConcrete<TEvent>(TEvent payload) where TEvent : class, IDomainEvent
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (payload.GetType() != typeof(TEvent))
            throw new ArgumentException("Publish the concrete payload type rather than a widened event interface or base type.", nameof(payload));
    }

    private sealed class EventBatch(DomainEventCallChains chains) : ITransactionParticipant
    {
        private readonly List<(Guid EventId, string SubscriberId)> recorded = [];
        public Guid Id { get; } = Guid.NewGuid();
        public bool CommitCanFail => false;

        public void Record(Guid eventId, string subscriberId, GameplayCallChain publisher)
        {
            chains.Record(eventId, subscriberId, publisher);
            recorded.Add((eventId, subscriberId));
        }

        public ValueTask PrepareAsync(CancellationToken ct) => ValueTask.CompletedTask;
        public ValueTask CommitAsync(CancellationToken ct) => ValueTask.CompletedTask;

        public ValueTask RollbackAsync(CancellationToken ct)
        {
            foreach (var (eventId, subscriberId) in recorded) chains.Forget(eventId, subscriberId);
            return ValueTask.CompletedTask;
        }
    }
}

/// <summary>The sole business adapter from immediate event projections to the existing notification mechanism.</summary>
internal sealed class NotificationProjectionSubscriber<TNotification>(INotificationBus notifications)
    where TNotification : class, INotification<TNotification>
{
    public ValueTask<NotificationDelivery> HandleAsync(NotificationAudience audience,
        TNotification notification, CancellationToken cancellationToken) =>
        notifications.PublishAsync(audience, notification, cancellationToken);
}
