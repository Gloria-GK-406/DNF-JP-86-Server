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
    IEntryPlacementRuleService placement,
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
        // Household facts are read only for a crossing into or out of a household town.
        var entering = placement.IsHouseholdTown(townId);
        var leaving = !entering && placement.IsHouseholdTown(standing.TownId);
        return placement.RefuseCrossing(new TownCrossingFacts(
            standing,
            townId,
            areaId,
            entering && housing.IsEntrySource(standing.TownId, standing.AreaId),
            entering ? await housing.InstanceAsync(session, cancellationToken).ConfigureAwait(false) : null,
            leaving ? housingSessions.ReturnFor(session) : null));
    }

    public CharacterPositionState StoredPositionAfter(CharacterPositionState standing, CharacterPositionState destination) =>
        placement.StoredPositionAfter(standing, destination);

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
        return await AreaInstanceAsync(session, townId, cancellationToken).ConfigureAwait(false) is { } instance
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
        var housingChange = previous is null
            ? TownHousingChange.None
            : placement.HousingChangeOf(previous.Area.TownId, destination.TownId);
        // The way back is remembered before the character moves: if remembering fails, it is still
        // standing where it was rather than inside a couple room it has no exit from.
        if (housingChange == TownHousingChange.Enters)
            housingSessions.RememberReturn(session, previous!.Position);
        var change = onlineCharacters.CrossInto(session, destination, x, y, sharesTownPresence, direction);
        if (change is not null && housingChange == TownHousingChange.Leaves
            && change.CurrentArea?.Key != previous!.Area)
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
            if (await AreaInstanceAsync(request.Session, request.Position.TownId, cancellationToken)
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

    // The household instance is read only for a household town, whose area instance it decides.
    private async ValueTask<int?> AreaInstanceAsync(
        SelectedSessionIdentity session, int townId, CancellationToken cancellationToken) =>
        placement.AreaInstance(townId, placement.IsHouseholdTown(townId)
            ? await housing.InstanceAsync(session, cancellationToken).ConfigureAwait(false)
            : null);
}
