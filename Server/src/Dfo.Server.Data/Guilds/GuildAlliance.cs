using Dfo.Server.Data.Characters;

namespace Dfo.Server.Data.Guilds;

public sealed record GuildAlliancePeer(GuildId Id, string Name, byte Mark, CharacterId Leader);

public sealed record GuildAllianceApplication(GuildAlliancePeer Other, string Message, long AppliedAt);

public enum GuildAllianceAction
{
    Apply,
    Withdraw,
    Approve,
    Reject,
    Dissolve,
}

public enum GuildAllianceStatus
{
    Success,
    Forbidden,
    InvalidTarget,
    InvalidMessage,
    AlreadyAllied,
    PendingOrListFull,
    ApplicationMissing,
    AllianceMissing,
}

public sealed record GuildAllianceResult(GuildAllianceStatus Status, GuildAlliancePeer? Other = null);
