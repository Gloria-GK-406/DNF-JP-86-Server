using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World.BossTower;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Execution;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.CharacterDeath;
using Dfo.Server.Protocol.Game.DeleteItem;
using Dfo.Server.Protocol.Game.DungeonClear;
using Dfo.Server.Protocol.Game.DungeonEntry;
using Dfo.Server.Protocol.Game.DungeonSelection;
using Dfo.Server.Protocol.Game.FinishLoading;
using Dfo.Server.Protocol.Game.GroundPickup;
using Dfo.Server.Protocol.Game.IllusionTowerContinue;
using Dfo.Server.Protocol.Game.IllusionTowerInfo;
using Dfo.Server.Protocol.Game.IllusionTowerMap;
using Dfo.Server.Protocol.Game.IllusionTowerRanking;
using Dfo.Server.Protocol.Game.IllusionTowerReward;
using Dfo.Server.Protocol.Game.MonsterDeath;
using Dfo.Server.Protocol.Game.Party;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Dfo.Server.Protocol.Game.TownProjection;
using Dfo.Server.Protocol.Game.Weakness;
using Dfo.Server.Protocol.Game;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Collections.Immutable;

namespace Dfo.Server.Protocol.Game.Party;

internal static class TownDepartureProjection
{
    /// <summary>Projects committed departures to the areas they left and to their parties.</summary>
    internal static async ValueTask<IReadOnlyList<TownAreaMembershipChange>> TownProjectDeparturesAsync(ISelectedSessionDomainService sessions, ITownAreaDomainService townAreas, ICommandProjections projections, IPartyProjectionStateDomainService projectionState, ISelectedCharacterCurrentViewDomainService currentViews, IPartyService parties, 
        IReadOnlyList<TownDepartureResult> departed,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(departed);
        var departures = departed.Select(departure => new TownAreaDeparture(
            departure.Session,
            departure.Change,
            departure.PreviousArea,
            departure.SharedTownPresence,
            departure.SharedTownPresence ? TownArrivalProjection.TownScope(departure.PreviousArea) : null)).ToArray();
        if (departures.Length == 0)
            return [];

        foreach (var area in departures
            .Where(departure => departure.SharedTownPresence)
            .Select(departure => departure.PreviousArea)
            .Distinct())
        {
            await TownArrivalProjection.TownPublishCurrentRosterCoreAsync(sessions, townAreas, projections, currentViews, area, cancellationToken)
                .ConfigureAwait(false);
        }
        foreach (var departure in departures)
        {
            await PartyTownProjection.PartyTownRefreshMovedPartyAsync(sessions, townAreas, projections, projectionState, parties, 
                departure.Session, departure.PreviousScope, cancellationToken).ConfigureAwait(false);
        }
        return departures.Select(departure => departure.Change).ToArray();
    }
}
