using Dfo.Server.Abstractions.DomainEvents;

namespace Dfo.Server.Abstractions.Events;

public sealed record ManagedEventActivationState(bool Active, long ConfigurationRevision, DateTimeOffset AppliedAt);

/// <summary>A target state, including reapplication after a configuration revision changes.</summary>
public sealed record ManagedEventActivationChanged(int EventId, bool Active, long ConfigurationRevision,
    DateTimeOffset AppliedAt) : IDomainEvent;

public interface IManagedEventActivationPersistence
{
    ValueTask<ManagedEventActivationState?> ReadAsync(int eventId, CancellationToken ct);
    ValueTask SaveAsync(int eventId, ManagedEventActivationState value, CancellationToken ct);
}

public interface IManagedEventLifecycleDomainService
{
    IReadOnlyList<int> EventIds { get; }
    ValueTask<ManagedEventActivationState?> AppliedAsync(int eventId, CancellationToken ct);
    ValueTask<bool> ReconcileAsync(int eventId, DateTimeOffset now, CancellationToken ct, bool notifyClients = true);
}
