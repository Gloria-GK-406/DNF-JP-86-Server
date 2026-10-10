using Dfo.Server.Abstractions.Notifications;

namespace Dfo.Server.Abstractions.DomainEvents;

/// <summary>An immutable business fact; payloads contain data, never live services or connections.</summary>
public interface IDomainEvent;

public sealed record DomainEventEnvelope<TEvent>(
    Guid EventId, Guid BatchId, DateTimeOffset OccurredAt, Guid OriginInstanceId, TEvent Payload)
    where TEvent : class, IDomainEvent;

public interface IDomainEventSubscriber<TEvent> where TEvent : class, IDomainEvent
{
    /// <summary>Reliable handlers join the dispatch transaction and cannot reenter commands or acquire outer account/session locks.</summary>
    ValueTask HandleAsync(DomainEventEnvelope<TEvent> message, CancellationToken cancellationToken);
}

public enum DomainEventDeliveryPolicy { Reliable, LiveOnly }

/// <summary>The entry point for durable facts, immediate runtime facts, and awaited protocol projections.</summary>
public interface IEventService
{
    /// <summary>Records the fact and its subscribers inside the current writable business transaction.</summary>
    ValueTask<Guid> EnqueueAsync<TEvent>(TEvent payload, CancellationToken cancellationToken = default)
        where TEvent : class, IDomainEvent;

    /// <summary>Directly awaits runtime subscribers in registration order, inheriting the caller transaction. Does not open a transaction, queue, persist, replay, or swallow failures.</summary>
    ValueTask DirectAsync<TEvent>(TEvent payload, CancellationToken cancellationToken = default)
        where TEvent : class, IDomainEvent;

    /// <summary>Awaits an immediate protocol projection and preserves its delivery result and audience.</summary>
    ValueTask<NotificationDelivery> ProjectAsync<TNotification>(
        NotificationAudience audience, TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : class, INotification<TNotification>;
}
