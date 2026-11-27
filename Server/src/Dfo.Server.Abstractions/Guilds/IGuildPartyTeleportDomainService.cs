using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Guilds;

public enum GuildPartyTeleportRefusal
{
    PartyLeaderRequired,
    PartyNotTogether,
    DestinationUnavailable,
    DestinationLocked,
    AlreadyThere,
}

public sealed record GuildPartyTeleportTarget(byte TownId, byte AreaId, short X, short Y, byte Direction);

public sealed record GuildPartyTeleportPlan(
    TownAreaKey Destination, CharacterPositionState Position, IReadOnlyList<TownAreaMember> Members);

public sealed record GuildPartyTeleportPreparation(GuildPartyTeleportPlan? Plan, GuildPartyTeleportRefusal? Refusal = null);

public interface IGuildPartyTeleportDomainService
{
    ValueTask<GuildPartyTeleportPreparation> PrepareAsync(
        SelectedSessionIdentity leader, GuildPartyTeleportTarget target, CancellationToken ct);
}
