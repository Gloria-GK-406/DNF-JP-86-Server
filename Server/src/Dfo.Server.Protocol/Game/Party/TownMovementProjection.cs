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

internal static class TownMovementProjection
{
    internal static ValueTask TownRegisterNotiAsync(IGameCommandOutbox outbox, NetworkConnectionId connection, IGameNotiMessage message, CancellationToken token) => outbox.RegisterExplicitAsync(connection, new([message], []), token);

    internal static ValueTask TownRefuseTownChangeAsync(IGameCommandOutbox outbox, NetworkConnectionId connectionId, SelectedSessionIdentity session,
        SelectCharacterResult result, Dfo.Server.Data.Characters.CharacterPositionState standing,
        string reason, CancellationToken cancellationToken)
    {
        var spawn = result.World.Channel.Spawn with
        {
            TownId = standing.TownId, AreaId = standing.AreaId,
            X = standing.X, Y = standing.Y, Direction = standing.Direction,
        };
        return outbox.RegisterExplicitAsync(connectionId, new GameExplicitMessageDelivery(
            [new UserAreaNotification(session.WireUserId, spawn, standing.AreaId),
             new GameServerNoticeMessage(reason)], []), cancellationToken);
    }

    internal static async ValueTask<bool> TownTransferAsync(ISelectedSessionDomainService sessions, ITownAreaDomainService townAreas, ICommandProjections projections, IPartyTownProjectionDelivery partyTown, ISelectedCharacterCurrentViewDomainService currentViews, IMarriageHousingQueryDomainService housing, IGameCommandOutbox outbox, IMarriageHousingSessionDomainService housingSessions, 
        NetworkConnectionId connectionId,
        SelectedSessionIdentity session,
        SelectCharacterResult result,
        int channelId,
        Dfo.Server.Data.Characters.CharacterPositionState moved,
        TownAreaMember previousMember,
        bool sharesTownPresence,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(moved);
        ArgumentNullException.ThrowIfNull(previousMember);
        var instance = moved.TownId == 101 ? await housing.InstanceAsync(session, cancellationToken).ConfigureAwait(false) : 0;
        if (moved.TownId == 101 && instance is not > 0) return false;
        var destination = new TownAreaKey(
            channelId,
            moved.TownId,
            moved.AreaId, instance ?? 0);
        var change = townAreas.TransferExact(
            session.CharacterId,
            session.TownAreaLease(),
            destination,
            moved,
            sharesTownPresence);
        if (change is null)
            return false;

        return await TownMovementProjection.TownProjectTransferAsync(sessions, townAreas, projections, partyTown, currentViews, housing, outbox, housingSessions, connectionId, session, result, previousMember, cancellationToken).ConfigureAwait(false);
    }

    internal static async ValueTask<bool> TownProjectTransferAsync(ISelectedSessionDomainService sessions, ITownAreaDomainService townAreas, ICommandProjections projections, IPartyTownProjectionDelivery partyTown, ISelectedCharacterCurrentViewDomainService currentViews, IMarriageHousingQueryDomainService housing, IGameCommandOutbox outbox, IMarriageHousingSessionDomainService housingSessions, NetworkConnectionId connectionId, SelectedSessionIdentity session,
        SelectCharacterResult result, TownAreaMember previousMember, CancellationToken cancellationToken)
    {
        var member = townAreas.Find(session.CharacterId);
        if (member is null || !session.Owns(member)) return false;
        var changedArea = previousMember.Area != member.Area;
        if (changedArea && previousMember.Area.TownId == 101) housingSessions.CloseExact(session);
        var currentKey = member.Area;
        var current = townAreas.Find(currentKey);
        var own = current?.Members.SingleOrDefault(member => session.Owns(member));
        if (own is null)
        {
            if (changedArea
                && previousMember.SharesTownPresence)
            {
                await TownArrivalProjection.TownPublishCurrentRosterCoreAsync(sessions, townAreas, projections, currentViews, previousMember.Area, cancellationToken)
                    .ConfigureAwait(false);
            }
            return false;
        }

        var visible = await TownArrivalProjection.TownVisibleParticipantsAsync(sessions, currentViews, current!, session, cancellationToken)
            .ConfigureAwait(false);
        var peers = visible.Select(candidate => candidate.Participant).ToArray();
        var peerSessions = visible.Select(candidate => candidate.Session).ToArray();
        var currentPosition = own.Position;
        var spawn = result.World.Channel.Spawn with
        {
            TownId = currentPosition.TownId,
            AreaId = currentPosition.AreaId,
            X = currentPosition.X,
            Y = currentPosition.Y,
            Direction = currentPosition.Direction,
            SharesTownPresence = own.SharesTownPresence,
        };
        var state = peers.Length == 0
            ? null
            : new TownPresenceSynchronizationState(
                currentPosition.TownId,
                currentPosition.AreaId,
                TownReturnProjection.Participant(result, currentPosition),
                peers);
        IReadOnlyList<IGameServerMessage> direct = state is null
            ?
            [
                new UserAreaNotification(session.WireUserId, spawn, currentPosition.AreaId),
                new AreaUsersNotification(
                    currentPosition.TownId,
                    currentPosition.AreaId,
                    [new AreaUserPresence(
                        session.WireUserId,
                        currentPosition.X,
                        currentPosition.Y,
                        currentPosition.Direction,
                        spawn.AreaState)]),
            ]
            :
            [
                new UserAreaNotification(session.WireUserId, spawn, currentPosition.AreaId),
                .. TownPresenceMessages.ForNewcomer(state),
            ];
        await outbox.RegisterExplicitAsync(
            connectionId,
            new GameExplicitMessageDelivery(direct, []),
            cancellationToken).ConfigureAwait(false);

        if (changedArea && previousMember.SharesTownPresence)
            await TownArrivalProjection.TownPublishCurrentRosterCoreAsync(sessions, townAreas, projections, currentViews, previousMember.Area, cancellationToken).ConfigureAwait(false);
        if (changedArea && peerSessions.Length > 0 && state is not null)
            await TownArrivalProjection.TownPublishArrivalCoreAsync(townAreas, projections, partyTown, session, state, peerSessions, cancellationToken)
                .ConfigureAwait(false);
        if (peerSessions.Length > 0)
        {
            await partyTown.RefreshObservedPartiesAsync( 
                [session], cancellationToken).ConfigureAwait(false);
        }
        if (changedArea)
        {
            await partyTown.RefreshMovedPartyAsync( 
                session, TownArrivalProjection.TownScope(previousMember.Area), cancellationToken).ConfigureAwait(false);
        }
        var marriageFrames = await HousingCurrentProjection.ReadAsync(housing, session, cancellationToken).ConfigureAwait(false);
        if (marriageFrames.Count > 0) await outbox.RegisterExplicitAsync(connectionId, new(marriageFrames, []), cancellationToken).ConfigureAwait(false);
        return true;
    }

    internal static async ValueTask<bool> TownMoveAsync(ISelectedSessionDomainService sessions, ITownAreaDomainService townAreas, ICommandProjections projections, IGameCommandOutbox outbox, 
        NetworkConnectionId connectionId,
        SelectedSessionIdentity session,
        Dfo.Server.Data.Characters.CharacterPositionState position,
        ushort trailer,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(position);
        var previous = townAreas.Find(session.CharacterId);
        if (previous is null || !session.Owns(previous))
        {
            await TownMovementProjection.TownRegisterNotiAsync(outbox, 
                connectionId,
                new UserPositionNotification(
                    session.WireUserId,
                    position.X,
                    position.Y,
                    position.Direction,
                    trailer),
                cancellationToken).ConfigureAwait(false);
            return false;
        }
        var movement = townAreas.MoveExact(
            session.CharacterId,
            session.TownAreaLease(),
            position);
        var area = movement?.Area ?? townAreas.Find(previous.Area);
        if (area is null)
        {
            await TownMovementProjection.TownRegisterNotiAsync(outbox, 
                connectionId,
                new UserPositionNotification(
                    session.WireUserId,
                    position.X,
                    position.Y,
                    position.Direction,
                    trailer),
                cancellationToken).ConfigureAwait(false);
            return false;
        }

        var current = townAreas.Find(area.Key);
        var own = current?.Members.SingleOrDefault(member => session.Owns(member));
        if (own is null)
            return false;

        await TownMovementProjection.TownRegisterNotiAsync(outbox, 
            connectionId,
            new UserPositionNotification(
                session.WireUserId,
                own.Position.X,
                own.Position.Y,
                own.Position.Direction,
                trailer),
            cancellationToken).ConfigureAwait(false);
        if (!own.SharesTownPresence)
            return true;

        var audience = current!.Members
            .Where(member => member.SharesTownPresence
                && member.CharacterId != session.CharacterId)
            .Select((member => TownArrivalProjection.TownExactSession(sessions, member)))
            .Where(candidate => candidate is not null)
            .Cast<SelectedSessionIdentity>()
            .ToArray();
        if (audience.Length > 0)
        {
            await projections.ProjectAsync(
                new NotificationAudience.TheseSessions(audience),
                new TownPositionChanged(
                    session.WireUserId,
                    own.Position.X,
                    own.Position.Y,
                    own.Position.Direction,
                    trailer),
                cancellationToken).ConfigureAwait(false);
        }
        return true;
    }
}
