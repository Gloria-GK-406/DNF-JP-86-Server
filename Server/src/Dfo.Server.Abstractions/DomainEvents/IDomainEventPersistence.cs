namespace Dfo.Server.Abstractions.DomainEvents;

public sealed record StoredDomainEvent(
    Guid EventId, Guid BatchId, DateTimeOffset OccurredAt, Guid OriginInstanceId,
    string EventType, int SchemaVersion, string PayloadJson);

public sealed record DomainEventDeliveryTarget(string SubscriberId, string? CoalescingKey);

public enum DomainEventDeliveryState { Pending, Completed, Terminal }

public sealed record PendingDomainEventDelivery(
    StoredDomainEvent Event, string SubscriberId, string? CoalescingKey, int AttemptCount);

/// <summary>Writes join the current domain transaction; reads may run outside one.</summary>
public interface IDomainEventPersistence
{
    ValueTask AppendAsync(StoredDomainEvent item, IReadOnlyList<DomainEventDeliveryTarget> subscribers,
        CancellationToken cancellationToken);
    ValueTask<IReadOnlyList<PendingDomainEventDelivery>> ReadDueAsync(string subscriberId,
        DateTimeOffset now, int limit, CancellationToken cancellationToken);
    ValueTask<DateTimeOffset?> NextDueAsync(string subscriberId, CancellationToken cancellationToken);
    /// <summary>Acquires this delivery by writing before any subscriber reads; its transaction owns the claim.</summary>
    ValueTask<bool> TryBeginAsync(Guid eventId, string subscriberId, DateTimeOffset now,
        CancellationToken cancellationToken);
    ValueTask<bool> HasCompletedCoalescingKeyAsync(Guid batchId, string subscriberId, string key,
        CancellationToken cancellationToken);
    ValueTask CompleteAsync(Guid eventId, string subscriberId, DateTimeOffset now, string reason,
        CancellationToken cancellationToken);
    /// <summary>Settles a completed failed job only if no newer attempt or completion has superseded it.</summary>
    /// <summary>Uncertainty quarantines a still-pending row even when a newer retry record already exists.</summary>
    ValueTask<bool> QuarantineAsync(Guid eventId, string subscriberId, int expectedAttemptCount, string error,
        CancellationToken cancellationToken);
    ValueTask<bool> TryFailAsync(Guid eventId, string subscriberId, int expectedAttemptCount, DateTimeOffset nextAttemptAt,
        string error, bool terminal, CancellationToken cancellationToken);
    ValueTask FailAsync(Guid eventId, string subscriberId, DateTimeOffset nextAttemptAt,
        string error, bool terminal, CancellationToken cancellationToken);
}
