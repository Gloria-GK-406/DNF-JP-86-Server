using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.UsageQuotas;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.World.BossTower;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.Party;
using Dfo.Server.Protocol.Game.DungeonEntry;
using Dfo.Server.Protocol.Game.FinishLoading;
using Dfo.Server.Protocol.Game.IllusionTowerStage;
using Dfo.Server.Protocol.Game.BossTowerMap;
using Dfo.Server.Protocol.Game.IllusionTowerMap;
using Dfo.Server.Protocol.Game.Dungeons;
using Microsoft.Extensions.Logging;
namespace Dfo.Server.Protocol.Game;
internal sealed partial class GameMapLoadingCommandHandler
{
    internal async ValueTask<bool> CompleteOrdinaryLoadingAsync(
        NetworkConnectionId connectionId,
        CancellationToken cancellationToken)
    {
        var session = sessions.FindByConnection(connectionId);
        if (session is null)
            return false;

        var expected = runs.Find(session.CharacterId);
        // FINISH_LOADING also closes ordinary town loads. There is no dungeon state to mutate,
        // but the response must still belong to the exact selected-session generation.
        if (expected is null)
        {
            if (!IsCurrentSession(session, connectionId))
                return false;
            await outbox.RegisterExplicitAsync(connectionId, new([new LoadingCompleteNotification()], []), cancellationToken).ConfigureAwait(false);
            return true;
        }

        if (!IsCurrentSession(session, connectionId)
            || registry.Find(expected.RunId).SingleOrDefault(value =>
                value.CharacterId == session.CharacterId) != session)
        {
            return false;
        }

        // Entry USERINFO refreshes the client's combat attributes. Restore the retained
        // weakness after the first map loads, once per participant. Room changes and duplicate
        // loading reports must not reapply a notification that can also reset current HP/MP.
        var restoreEntryWeakness = expected.RoomVersion == 0
            && !expected.ReadyParticipants.Contains(session.CharacterId);

        await LoadingReadyAsync(session, expected, cancellationToken).ConfigureAwait(false);

        var current = runs.Find(session.CharacterId);
        var accepted = current is not null
            && current.RunId == expected.RunId
            && current.RoomVersion == expected.RoomVersion
            && current.ReadyParticipants.Contains(session.CharacterId)
            && IsCurrentSession(session, connectionId)
            && registry.Find(expected.RunId).SingleOrDefault(value =>
                value.CharacterId == session.CharacterId) == session;
        if (!accepted)
            return false;

        // The direct response belongs to this frozen generation too; it follows the validation above.
        await outbox.RegisterExplicitAsync(connectionId, new([new LoadingCompleteNotification()], []), cancellationToken).ConfigureAwait(false);
        // A repeated FINISH_LOADING or a later room must not restart the exploration clock.
        if (restoreEntryWeakness && current!.Layout.Mode is DungeonRunMode.LostLand { Plan: var exploration })
            await outbox.RegisterNotiAsync(connectionId,
                new Dfo.Server.Protocol.Game.StateSynchronization.DungeonClearTimeLimitMessage(
                    exploration.FirstLimitSeconds, exploration.SecondLimitSeconds), cancellationToken).ConfigureAwait(false);
        await LoadingMechanismsAsync(session, current!, cancellationToken).ConfigureAwait(false);
        if (current!.Layout.Mode is DungeonRunMode.LostLand)
        {
            // Sending 100 again restarts the native fever clock. Only restore an inactive charge.
            if (current.ModeState is DungeonRunModeState.LostLand { Fever: { ActiveUntil: null } fever })
                await outbox.RegisterNotiAsync(connectionId,
                    new Dfo.Server.Protocol.Game.StateSynchronization.DungeonFeverGaugeMessage(
                        fever.Percent), cancellationToken).ConfigureAwait(false);
        }
        if (current!.Layout.Mode.Capabilities.QuotaFamily is { } quota)
            _ = await LoadingQuotaAsync(session, quota, cancellationToken).ConfigureAwait(false);
        if (restoreEntryWeakness)
            await LoadingWeaknessAsync(session, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async ValueTask MoveOrdinaryAsync(CommandExecutionContext execution,
        NetworkDispatchContext context,
        MoveMapRequest message,
        SelectedSessionIdentity session,
        CancellationToken cancellationToken)
    {
        var current = runs.Find(session.CharacterId);
        if (current is null)
        {
            logger.LogDebug(
                "Move map {ConnectionId}: no run in progress, ignoring ({X},{Y})",
                context.Connection.ConnectionId, message.NextX, message.NextY);
            return;
        }

        if (logger.IsEnabled(LogLevel.Debug))
            logger.LogDebug("Move map state connection={ConnectionId} character={CharacterId} run={RunId} dungeon={DungeonId} maze={MazeIndex} room={RoomId} version={RoomVersion} from=({FromX},{FromY}) kind={Kind} mayLeave={MayLeave} cleared={Cleared} aliveBlockers={AliveBlockers} mechanismBlocks={MechanismBlocks}",
                context.Connection.ConnectionId, session.CharacterId, current.RunId, current.Layout.DungeonId,
                current.Layout.MazeIndex, current.CurrentRoomMapId, current.RoomVersion, current.CurrentCell.X,
                current.CurrentCell.Y, current.CurrentRoomKind, current.MayLeaveCurrentRoom,
                current.CurrentCellState.IsCleared, current.CurrentCellState.Actors.Count(x => x.BlocksClear && !x.IsDead),
                current.CurrentCellState.MechanismBlocksClear);

        // The room decides, not the run. A boss room has no way back out of it whatever state the
        // run is in; see DungeonRoomKind.
        if (!current.MayLeaveCurrentRoom)
        {
            logger.LogDebug(
                "Move map {ConnectionId}: the {Kind} room at ({FromX},{FromY}) may not be left, "
                    + "refusing ({X},{Y})",
                context.Connection.ConnectionId,
                current.CurrentRoomKind,
                current.CurrentCell.X,
                current.CurrentCell.Y,
                message.NextX,
                message.NextY);
            return;
        }

        var target = new MazeCell(message.NextX, message.NextY);
        // FindByConnection identifies who sent this frame; it does not prove that generation is
        // still the character's published generation. Validate at the mutation boundary so a
        // superseded connection cannot advance the shared aggregate.
        if (!sessions.IsInWorld(session))
        {
            logger.LogDebug("Move map refused connection={ConnectionId} character={CharacterId} generation={Generation} run={RunId} sequence={Sequence} reason=SupersededSession",
                context.Connection.ConnectionId, session.CharacterId, session.ConnectionGeneration, current.RunId, message.Metadata.Sequence);
            return;
        }
        if (!FindMoveRecipients(session, current.RunId).Contains(session))
        {
            logger.LogDebug("Move map refused connection={ConnectionId} character={CharacterId} generation={Generation} run={RunId} sequence={Sequence} reason=NotAdmittedParticipant",
                context.Connection.ConnectionId, session.CharacterId, session.ConnectionGeneration, current.RunId, message.Metadata.Sequence);
            return;
        }

        var attempt = await MoveRoomAsync(execution, session, current, target, cancellationToken).ConfigureAwait(false);
        var visit = attempt.Visit;
        if (visit is null)
        {
            logger.LogDebug(
                "Move map {ConnectionId}: maze {MazeIndex} of dungeon {DungeonId} refused {Failure} "
                    + "at ({X},{Y}), staying in ({FromX},{FromY})",
                context.Connection.ConnectionId,
                current.Layout.MazeIndex,
                current.Layout.DungeonId,
                attempt.Failure,
                message.NextX,
                message.NextY,
                current.CurrentCell.X,
                current.CurrentCell.Y);
            return;
        }

        if (logger.IsEnabled(LogLevel.Debug))
            logger.LogDebug(
                "Move map {ConnectionId} user={WireUserId}: ({FromX},{FromY}) -> ({ToX},{ToY}) "
                    + "room={RoomId} first={FirstEntry} path=({PathX},{PathY}) run={RunId} version={RoomVersion} sequence={Sequence} stage=Committed",
                context.Connection.ConnectionId,
                session.WireUserId,
                current.CurrentCell.X,
                current.CurrentCell.Y,
                visit.Run.CurrentCell.X,
                visit.Run.CurrentCell.Y,
                visit.Run.CurrentRoomMapId,
                visit.FirstEntry,
                message.PathPositionX,
                message.PathPositionY, visit.Run.RunId, visit.Run.RoomVersion, message.Metadata.Sequence);

        // A room walked into a second time is described as empty. The reference server has a
        // separate revisit builder that writes no actors and no drops at all, and it is right to:
        // the client already killed what was in there and still holds those identities. Sending
        // the list again resurrects them under the very same numbers -- which is what a live
        // session saw on 2026-08-26 when it walked back out of the boss room, five monsters and
        // four monsters standing up again with the numbers they had died under.
        var frozen = FindMoveRecipients(session, visit.Run.RunId);
        var currentRecipients = frozen
            .Where(value => sessions.IsInWorld(value))
            .Select(value => (Session: value, Run: runs.Find(value.CharacterId)))
            .Where(value => value.Run?.RunId == visit.Run.RunId)
            .Select(value => (value.Session, Run: value.Run!))
            .ToArray();
        if (currentRecipients.Length == 0)
        {
            logger.LogWarning("Move map committed without recipients connection={ConnectionId} run={RunId} room={RoomId} version={RoomVersion} sequence={Sequence}",
                context.Connection.ConnectionId, visit.Run.RunId, visit.Run.CurrentRoomMapId, visit.Run.RoomVersion, message.Metadata.Sequence);
            return;
        }

        // If the requester was replaced while room projection was awaited, the room has already
        // advanced. Do not send to either requester generation, but do keep the remaining frozen
        // participants in sync with the committed shared room.
        var required = currentRecipients.FirstOrDefault(value => value.Session == session);
        if (required == default)
            required = currentRecipients[0];

        StartMapNotification RoomFor(DungeonRunState run) => new(
            run,
            visit.FirstEntry,
            visit.Actors,
            visit.FirstActorSequence,
            FloorPieceValues.Describe(run, visit.FloorDrops));

        IReadOnlyList<IGameServerMessage> FramesFor(DungeonRunState run) =>
            run.Layout.Mode is DungeonRunMode.Altar
                ? run.IsCleared
                    ? [new Dfo.Server.Protocol.Game.Altar.AltarMapMessage(run), new Dfo.Server.Protocol.Game.Altar.AltarWaveMessage([], 0)]
                    : [new Dfo.Server.Protocol.Game.Altar.AltarMapMessage(run)]
                : visit.FirstEntry && HellPartyProjection.For(run) is { } info
                ? [RoomFor(run), info] : [RoomFor(run)];

        try
        {
            await outbox.RegisterExplicitAsync(
                required.Session.ConnectionId,
                new GameExplicitMessageDelivery(
                    FramesFor(required.Run),
                    [.. currentRecipients
                        .Where(value => value.Session != required.Session)
                        .Select(value => new GameExplicitMessageTarget(
                            value.Session.ConnectionId,
                            FramesFor(value.Run)))]),
                cancellationToken).ConfigureAwait(false);
            foreach (var receiver in currentRecipients)
                await MoveFatigueAsync(receiver.Session, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug("Move map publication cancelled connection={ConnectionId} run={RunId} sequence={Sequence}",
                context.Connection.ConnectionId, visit.Run.RunId, message.Metadata.Sequence);
            throw;
        }
        catch (Exception)
        {
            logger.LogWarning("Move map publication failed after commit connection={ConnectionId} run={RunId} room={RoomId} version={RoomVersion} sequence={Sequence}",
                context.Connection.ConnectionId, visit.Run.RunId, visit.Run.CurrentRoomMapId, visit.Run.RoomVersion, message.Metadata.Sequence);
            throw;
        }
        if (logger.IsEnabled(LogLevel.Debug))
            logger.LogDebug("Move map delivered connection={ConnectionId} run={RunId} room={RoomId} version={RoomVersion} sequence={Sequence} recipients={RecipientCount} actors={ActorCount} floorDrops={FloorDropCount}",
                context.Connection.ConnectionId, visit.Run.RunId, visit.Run.CurrentRoomMapId, visit.Run.RoomVersion,
                message.Metadata.Sequence, currentRecipients.Length, visit.Actors.Count, visit.FloorDrops.Count);
        if (visit.Run.Layout.Mode is DungeonRunMode.LostLand && logger.IsEnabled(LogLevel.Debug))
            foreach (var piece in FloorPieceValues.Describe(visit.Run, visit.FloorDrops))
                logger.LogDebug("LostLand floor registered connection={ConnectionId} run={RunId} room={RoomId} version={RoomVersion} first={FirstEntry} number={Number} item={ItemId} count={ItemCount} wireValue={WireValue}",
                    context.Connection.ConnectionId, visit.Run.RunId, visit.Run.CurrentRoomMapId, visit.Run.RoomVersion,
                    visit.FirstEntry, piece.Number, piece.Drop.ItemId, piece.Drop.Count, piece.WireValue);
    }
}
