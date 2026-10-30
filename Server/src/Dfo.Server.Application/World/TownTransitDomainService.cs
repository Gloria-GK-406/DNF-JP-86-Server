using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
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
    public async ValueTask<TownCrossingRefusal?> RefuseCrossingAsync(
        SelectedSessionIdentity session,
        CharacterPositionState standing,
        byte townId,
        byte areaId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(standing);
        if (CoupleRoomTown.Is(townId)
            && (areaId != 0
                || !CoupleRoomTown.Is(standing.TownId) && !housing.IsEntrySource(standing.TownId, standing.AreaId)
                || await CoupleRoomInstanceAsync(session, townId, cancellationToken).ConfigureAwait(false) is null))
            return TownCrossingRefusal.CoupleRoomEntry;
        if (CoupleRoomTown.Is(standing.TownId) && !CoupleRoomTown.Is(townId)
            && (housingSessions.ReturnFor(session) is not { } home || home.TownId != townId || home.AreaId != areaId))
            return TownCrossingRefusal.CoupleRoomExit;
        return null;
    }

    public CharacterPositionState StoredPositionAfter(CharacterPositionState standing, CharacterPositionState destination) =>
        CoupleRoomTown.Is(destination.TownId) ? standing : destination;

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
        return await CoupleRoomInstanceAsync(session, townId, cancellationToken).ConfigureAwait(false) is { } instance
            ? Cross(session, new TownAreaKey(channelId, townId, areaId, instance), x, y, sharesTownPresence)
            : null;
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
        // The way back is remembered before the character moves: if remembering fails, it is still
        // standing where it was rather than inside a couple room it has no exit from.
        if (previous is not null && !CoupleRoomTown.Is(previous.Area.TownId) && CoupleRoomTown.Is(destination.TownId))
            housingSessions.RememberReturn(session, previous.Position);
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
            if (await CoupleRoomInstanceAsync(request.Session, request.Position.TownId, cancellationToken)
                    .ConfigureAwait(false) is not { } instance)
                continue;
            candidates.Add((request, sharesTownPresence, instance));
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

    // The area instance a character stands in: its household's couple room, zero for any other
    // town, or null when it has no household to enter.
    private async ValueTask<int?> CoupleRoomInstanceAsync(
        SelectedSessionIdentity session, int townId, CancellationToken cancellationToken)
    {
        if (!CoupleRoomTown.Is(townId)) return 0;
        return await housing.InstanceAsync(session, cancellationToken).ConfigureAwait(false) is > 0 and var instance
            ? instance
            : null;
    }
}
