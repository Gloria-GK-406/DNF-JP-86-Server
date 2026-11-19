using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Data.Raids;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Raids;

public sealed record AntonRaidPhaseConcluded(AntonRaidPhaseOutcome Outcome) : IDomainEvent;

public sealed record AntonRaidCombatChanged(AntonRaidCombatSnapshot Snapshot,
    IReadOnlyList<SelectedSessionIdentity> Audience, IReadOnlyList<KeyValuePair<int, AntonRaidMapStatus>> Maps,
    IReadOnlyList<KeyValuePair<uint, uint>> Symbols, IReadOnlyList<AntonRaidCombatDeadline> Timers,
    IReadOnlyList<Guid> Ends, bool Participation = false, bool Full = false, ushort LifeMember = 0, byte LifeState = 0) : IDomainEvent
{
    public IReadOnlyList<AntonRaidParticipationGroup> ParticipationGroups { get; init; } = [];
    public bool BuffsChanged { get; init; }
    public bool InfectedMapChanged { get; init; }
}

public sealed record AntonRaidParticipationGroup(int DungeonId, byte Operation, IReadOnlyList<ushort> Members);
