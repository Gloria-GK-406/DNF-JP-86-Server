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

public sealed record AntonRaidTicket(ushort User, ushort Count, bool Ready);
