namespace Dfo.Server.Abstractions.Events;

/// <summary>A fact published by a completed domain operation, independent of network requests.</summary>
public interface IDomainEvent;

public interface IDomainEventHandler<in TEvent> where TEvent : class, IDomainEvent
{
    ValueTask HandleAsync(TEvent domainEvent, CancellationToken cancellationToken);
}

/// <summary>Best-effort dispatch of committed facts. Subscriber failures do not undo the operation.</summary>
public interface IDomainEventBus
{
    ValueTask PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken)
        where TEvent : class, IDomainEvent;
}
