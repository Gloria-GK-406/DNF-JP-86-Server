using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.Party;
using Dfo.Server.Protocol.Game.TownProjection;

namespace Dfo.Server.Protocol.Game;

internal sealed class DungeonTownReturnProjection(ISelectedSessionDomainService sessions,
    IDungeonRunQueryDomainService runs, ISelectedCharacterCurrentViewDomainService views,
    IOnlineCharacterRuntimeDomainService onlineCharacters, ITownTransitDomainService transit,
    ITownAreaDomainService towns, IGameCommandOutbox outbox, IWeaknessDomainService weakness,
    ICommandProjections projections, IPartyService parties, IPartyProjectionStateDomainService partyProjection)
{
    internal async ValueTask<bool> ReturnAsync(SelectedSessionIdentity session, CancellationToken ct)
    {
        if (!sessions.IsInWorld(session) || runs.Find(session.CharacterId) is not null)
            return false;
        var view = await views.TryLoadAsync(session, ct).ConfigureAwait(false);
        var position = onlineCharacters.PositionOf(session);
        if (view is null || position is null)
            throw new InvalidOperationException($"Cannot prepare dungeon return for {session.CharacterId}.");
        TownAreaEntry[] entries = [new(session, view, position)];
        var requests = TownArrivalProjection.TownReturnRequests(entries);
        var admitted = await transit.ReturnToTownAsync(requests, ct).ConfigureAwait(false);
        await TownArrivalProjection.ProjectAsync(sessions, towns, views, outbox, weakness, true, projections,
            new PartyTownProjectionDelivery(parties, towns, sessions, projections, partyProjection),
            TownArrivalProjection.TownAdmissions(entries, requests, admitted), ct).ConfigureAwait(false);
        return true;
    }
}
