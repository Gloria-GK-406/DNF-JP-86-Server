using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Raids;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Raids;

public sealed record AntonRaidStart(
    bool Started, AntonRaidRecruitmentChange? Change = null, DateTimeOffset? Deadline = null,
    IReadOnlyList<AntonRaidTicket>? Tickets = null)
{
    public static AntonRaidStart Refused { get; } = new(false);
}

public sealed record AntonRaidPreparationOutcome(
    AntonRaidRecruitmentChange? Change = null,
    DateTimeOffset? AttackDeadline = null,
    IReadOnlyList<AntonRaidTicket>? Tickets = null,
    IReadOnlyList<DungeonEntryInventoryChange>? Debits = null);
