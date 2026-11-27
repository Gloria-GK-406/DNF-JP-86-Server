using System.Collections.Immutable;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Guilds;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Guilds;

internal sealed class GuildHallAccessDomainService(IGuildMembershipQueryDomainService guilds, IOnlineCharacterRuntimeDomainService online)
    : IGuildHallAccessDomainService
{
    private static int? InstanceOf(GuildMembership? membership) =>
        membership?.Guild.Id.Value is > 0 and <= int.MaxValue and var guild ? (int)guild : null;

    public async ValueTask<int?> InstanceForAsync(CharacterId character, CancellationToken ct) =>
        InstanceOf(await guilds.FindMembershipAsync(character, ct).ConfigureAwait(false));

    public async ValueTask<GuildHallVisit?> VisitAsync(SelectedSessionIdentity session, byte? requiredArea, CancellationToken ct)
    {
        if (online.StandingOf(session) is not { Area.TownId: GuildHallTown.TownId } standing
            || requiredArea is { } area && standing.Area.AreaId != area)
            return null;
        var membership = await guilds.FindMembershipAsync(session.CharacterId, ct).ConfigureAwait(false);
        return membership is not null && InstanceOf(membership) == standing.Area.Instance
            ? new(membership, standing.Area) : null;
    }

    public async ValueTask<GuildDungeonSelection> CaptureGatesAsync(IReadOnlyList<SelectedSessionIdentity> sessions,
        CancellationToken ct)
    {
        var origins = ImmutableDictionary.CreateBuilder<SelectedSessionIdentity, GuildDungeonGateOrigin>();
        foreach (var session in sessions)
            if (await VisitAsync(session, GuildHallTown.DungeonGateAreaId, ct).ConfigureAwait(false) is { } visit)
                origins.Add(session, new(visit.Area, visit.Membership.Member.MembershipId));
        return new(origins.ToImmutable());
    }
}
