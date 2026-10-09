using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Notifications;

namespace Dfo.Server.Testing;

/// <summary>Protocol doubles must implement awaited projections; accidentally publishing a business fact fails.</summary>
internal abstract class ProjectionEventService : IEventService
{
    public ValueTask<Guid> EnqueueAsync<TEvent>(TEvent payload, CancellationToken cancellationToken = default)
        where TEvent : class, IDomainEvent => throw new InvalidOperationException("This protocol test does not publish durable facts.");

    public ValueTask PublishAsync<TEvent>(TEvent payload, CancellationToken cancellationToken = default)
        where TEvent : class, IDomainEvent => throw new InvalidOperationException("This protocol test does not publish runtime facts.");

    public abstract ValueTask<NotificationDelivery> ProjectAsync<TNotification>(NotificationAudience audience,
        TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : class, INotification<TNotification>;
}
