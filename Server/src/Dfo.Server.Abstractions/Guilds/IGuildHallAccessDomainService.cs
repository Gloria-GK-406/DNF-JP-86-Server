using System.Collections.Immutable;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Guilds;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Guilds;

public sealed record GuildHallVisit(GuildMembership Membership, TownAreaKey Area);
public sealed record GuildDungeonGateOrigin(TownAreaKey Area, long MembershipId);

/// <summary>The eligible gate visits from a single opening of the dungeon selection screen.</summary>
public sealed record GuildDungeonSelection(ImmutableDictionary<SelectedSessionIdentity, GuildDungeonGateOrigin> Origins)
{
    public TownAreaKey? GateFor(SelectedSessionIdentity session, GuildMembership member) =>
        Origins.TryGetValue(session, out var origin)
        && origin.MembershipId > 0 && origin.MembershipId == member.Member.MembershipId
        && origin.Area.Instance == member.Guild.Id.Value
        && origin.Area is { TownId: GuildHallTown.TownId, AreaId: GuildHallTown.DungeonGateAreaId }
            ? origin.Area : null;
}

public interface IGuildHallAccessDomainService
{
    ValueTask<int?> InstanceForAsync(CharacterId character, CancellationToken ct);
    ValueTask<GuildHallVisit?> VisitAsync(SelectedSessionIdentity session, byte? requiredArea, CancellationToken ct);
    ValueTask<GuildDungeonSelection> CaptureGatesAsync(IReadOnlyList<SelectedSessionIdentity> sessions, CancellationToken ct);
}
