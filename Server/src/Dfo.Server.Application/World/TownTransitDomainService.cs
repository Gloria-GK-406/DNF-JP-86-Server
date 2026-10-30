using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.World;

/// <summary>Moves characters being played between town areas, out of town and back into it.</summary>
internal sealed class TownTransitDomainService(
    ISelectedSessionDomainService sessions,
    ITownAreaDomainService townAreas,
    IOnlineCharacterRuntimeDomainService onlineCharacters,
    ITownMovementDomainService towns,
    IMarriageHousingQueryDomainService housing,
    IMarriageHousingSessionDomainService housingSessions) : ITownTransitDomainService
{
    public async ValueTask<TownAreaMembershipChange?> CrossAsync(
        SelectedSessionIdentity session,
        int channelId,
        byte townId,
        byte areaId,
        short x,
        short y,
        bool sharesTownPresence,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        var instance = CoupleRoomTown.Is(townId)
            ? await housing.InstanceAsync(session, cancellationToken).ConfigureAwait(false)
            : 0;
        if (CoupleRoomTown.Is(townId) && instance is not > 0) return null;
        return Cross(session, new TownAreaKey(channelId, townId, areaId, instance ?? 0), x, y, sharesTownPresence);
    }

    public TownAreaMembershipChange? Cross(
        SelectedSessionIdentity session,
        TownAreaKey destination,
        short x,
        short y,
        bool sharesTownPresence,
        byte? direction = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        var previous = onlineCharacters.StandingOf(session);
        var change = onlineCharacters.CrossInto(session, destination, x, y, sharesTownPresence, direction);
        if (change is not null && previous is not null && CoupleRoomTown.Is(previous.Area.TownId)
            && change.CurrentArea?.Key != previous.Area)
            housingSessions.CloseExact(session);
        return change;
    }

    public async ValueTask<IReadOnlyList<TownReturnAdmission>> ReturnToTownAsync(
        IReadOnlyList<TownReturnRequest> requests, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requests);
        // Every read that can wait finishes before the first runtime write, so a refused read never
        // leaves half of a party standing in town.
        var candidates = new List<(TownReturnRequest Request, bool SharesTownPresence, int Instance)>(requests.Count);
        foreach (var request in requests)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (!sessions.IsInWorld(request.Session))
                continue;
            var sharesTownPresence = await towns.SharesTownPresenceAsync(
                request.Position.TownId, request.Position.AreaId, cancellationToken).ConfigureAwait(false);
            var instance = CoupleRoomTown.Is(request.Position.TownId)
                ? await housing.InstanceAsync(request.Session, cancellationToken).ConfigureAwait(false)
                : 0;
            if (CoupleRoomTown.Is(request.Position.TownId) && instance is not > 0) continue;
            candidates.Add((request, CoupleRoomTown.Is(request.Position.TownId) || sharesTownPresence, instance ?? 0));
        }

        var admissions = new List<TownReturnAdmission>(candidates.Count);
        foreach (var (request, sharesTownPresence, instance) in candidates)
        {
            if (!sessions.IsInWorld(request.Session))
                continue;
            var previous = townAreas.Find(request.Session.CharacterId);
            var change = onlineCharacters.ReturnToTown(
                request.Session,
                new TownAreaKey(request.ChannelId, request.Position.TownId, request.Position.AreaId, instance),
                request.Position,
                sharesTownPresence);
            if (change is not null)
                admissions.Add(new TownReturnAdmission(request, change, previous));
        }
        return admissions;
    }

    public IReadOnlyList<TownDepartureResult> LeaveTown(
        IReadOnlyList<SelectedSessionIdentity> leaving, TownAreaDepartureReason reason)
    {
        ArgumentNullException.ThrowIfNull(leaving);
        var departures = new List<TownDepartureResult>(leaving.Count);
        foreach (var session in leaving.Distinct())
        {
            ArgumentNullException.ThrowIfNull(session);
            if (onlineCharacters.StandingOf(session) is not { } current)
                continue;
            var change = onlineCharacters.LeaveTown(session, reason);
            if (change is not null)
                departures.Add(new TownDepartureResult(session, change, current.Area, current.SharesTownPresence));
        }
        return departures;
    }
}
