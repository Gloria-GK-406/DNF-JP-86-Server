using Dfo.Server.Data.Characters;

namespace Dfo.Server.Data.Raids;

public enum AntonRaidStage : byte
{
    Recruiting = 0,
    Preparing = 1,
    Attacking = 2,
    Settling = 3,
    Completed = 4,
    Break = 5,
    Failed = 6,
}

public readonly record struct AntonRaidDeadline(Guid Instance, uint Number, long Generation, DateTimeOffset At);

/// <summary>A null character omits the identity of a member awaiting rejoin.</summary>
public sealed record AntonRaidTicket(CharacterId? Character, long Count, bool Ready);

public enum AntonRaidSquadSignal { Settings, Invite, Accept }
