using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Data.Events;

namespace Dfo.Server.Abstractions.Events;

public sealed record ManagedEventConfigurationChanged(int EventId, long Revision) : IDomainEvent;

public enum JoustLifecycleTransition { RoundStarted, PhaseChanged, RoundEnded }

/// <summary>Committed lifecycle outcome. Queries never produce this event.</summary>
public sealed record JoustLifecycleChanged(JoustState State, JoustLifecycleTransition Transition) : IDomainEvent;
