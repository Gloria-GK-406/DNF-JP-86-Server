using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.TownProjection;
namespace Dfo.Server.Protocol.Game.Party;
internal abstract partial class PartyMutationProtocolCommandHandler
{
    protected virtual async ValueTask<IReadOnlyList<TownAreaMembershipChange>> TownLeaveManyAsync(
        IReadOnlyList<SelectedSessionIdentity> sessions,
        TownAreaDepartureReason reason,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        var departures = new List<TownAreaDeparture>(sessions.Count);
        foreach (var session in sessions.Distinct())
        {
            ArgumentNullException.ThrowIfNull(session);
            var current = townAreas.Find(session.CharacterId);
            if (current is null || !session.Owns(current))
                continue;
            var previousScope = current.SharesTownPresence ? TownScope(current.Area) : null;
            var change = townAreas.LeaveExact(
                session.CharacterId,
                session.TownAreaLease(),
                reason);
            if (change is not null)
            {
                departures.Add(new TownAreaDeparture(
                    session,
                    change,
                    current.Area,
                    current.SharesTownPresence,
                    previousScope));
            }
        }
        if (departures.Count == 0)
            return [];

        foreach (var area in departures
            .Where(departure => departure.SharedTownPresence)
            .Select(departure => departure.PreviousArea)
            .Distinct())
        {
            await TownPublishCurrentRosterCoreAsync(area, cancellationToken)
                .ConfigureAwait(false);
        }
        foreach (var departure in departures)
        {
            await PartyTownRefreshMovedPartyAsync(
                departure.Session, departure.PreviousScope, cancellationToken).ConfigureAwait(false);
        }
        return departures.Select(departure => departure.Change).ToArray();
    }

}
