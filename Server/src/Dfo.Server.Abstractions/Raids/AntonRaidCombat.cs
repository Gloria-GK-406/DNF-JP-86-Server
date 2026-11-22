using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Raids;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Raids;

public sealed record AntonRaidMemberDeparture(DungeonRunId Run, SelectedSessionIdentity Session, bool EnableClear = false);

public sealed record AntonRaidPhaseConcluded(AntonRaidPhaseOutcome Outcome) : IDomainEvent;

public sealed record AntonRaidCombatChanged(AntonRaidCombatSnapshot Snapshot,
    IReadOnlyList<CharacterId> Audience, IReadOnlyList<KeyValuePair<int, AntonRaidMapStatus>> Maps,
    IReadOnlyList<KeyValuePair<uint, uint>> Symbols, IReadOnlyList<AntonRaidCombatDeadline> Timers,
    IReadOnlyList<Guid> Ends, bool Participation = false, bool Full = false) : IDomainEvent
{
    public IReadOnlyList<AntonRaidParticipationChange> ParticipationChanges { get; init; } = [];
    public AntonRaidLifeChange? LifeChange { get; init; }
    public bool BuffsChanged { get; init; }
    public bool InfectedMapChanged { get; init; }
}

public enum AntonRaidLifeState { Dead, Revived }

public sealed record AntonRaidLifeChange(CharacterId Character, AntonRaidLifeState State);

public enum AntonRaidShieldOperation { Set, Charge, Drain }

public enum AntonRaidParticipationKind { Departed, Ready, Cleared }

public sealed record AntonRaidParticipationChange(int DungeonId, AntonRaidParticipationKind Kind,
    IReadOnlyList<CharacterId> Members);
