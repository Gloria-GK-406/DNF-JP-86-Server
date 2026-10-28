using System.Buffers.Binary;
using Dfo.Server.Abstractions.Characters;
using System.Collections.Immutable;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Parties;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Protocol.Game.TownProjection;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Dfo.Server.Protocol.Game.DungeonEntry;
using Dfo.Server.Protocol.Game.Dungeons;
using Microsoft.Extensions.Logging;

using Dfo.Server.Protocol.Game.Party;
namespace Dfo.Server.Protocol.Game.DungeonSelection;
internal sealed partial class GameDungeonSelectionCommandHandler
{
    private async ValueTask<bool> BackToVillageAsync(CommandExecutionContext execution,
        NetworkDispatchContext context,
        BackToVillageRequest message,
        CancellationToken cancellationToken)
    {
        if (GameRequestFreshness.IsStale(message.Metadata)) return true;
        var session = sessions.FindByConnection(context.Connection.ConnectionId);
        switch (session is null ? null : DungeonLocator.Locate(session, participation, bossTowers, illusionTowers))
        {
            case DungeonPlace.InBossTower boss:
                await SelectionLeaveBossAsync(execution, boss, new BackToVillageResponse(), cancellationToken).ConfigureAwait(false);
                return true;
            case DungeonPlace.InIllusionTower illusion:
                await SelectionLeaveIllusionAsync(execution, illusion, new BackToVillageResponse(), cancellationToken).ConfigureAwait(false);
                return true;
            default:
                return await BackToVillageOrdinaryAsync(context, message, session, cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask<bool> BackToVillageOrdinaryAsync(
        NetworkDispatchContext context,
        BackToVillageRequest message,
        SelectedSessionIdentity? session,
        CancellationToken cancellationToken)
    {
        // Where they are standing is asked of the service that owns it. It is recorded and dropped
        // with the published session, so this conjunct admits exactly the connections the two
        // before it do.
        var result = session is null ? null
            : await currentViews.TryLoadAsync(session, cancellationToken).ConfigureAwait(false);
        if (session is null || result is null
            || onlineCharacters.PositionOf(session) is not { } position)
        {
            logger.LogWarning(
                "Back to village {ConnectionId}: no published session, ignoring",
                context.Connection.ConnectionId);
            return false;
        }

        // The leader's fan-out consumes every attached member's one-shot selection intent. A
        // follower can still have its own button request queued behind that push; acknowledging it
        // is required, but replaying a self-only town projection would erase the party roster that
        // the leader has just restored.
        if (!entryIntents.ClearExact(session))
        {
            await outbox.RegisterExplicitAsync(
                context.Connection.ConnectionId,
                new GameExplicitMessageDelivery([new BackToVillageResponse()], []),
                cancellationToken).ConfigureAwait(false);
            return false;
        }

        var returning = (await ResolveReturningMembersAsync(session, result, position, cancellationToken)
                .ConfigureAwait(false))
            .Where(value => sessions.IsInWorld(value.Session))
            .ToArray();
        var requester = returning.SingleOrDefault(value => value.Session == session);
        if (requester is null)
            return false;

        List<TownAreaEntry> areaEntries = [];
        var endedRuns = 0;
        returning = returning
            .Where(value => sessions.IsInWorld(value.Session))
            .ToArray();
        requester = returning.SingleOrDefault(value => value.Session == session);
        if (requester is null)
            return false;

        // Only the run's leader takes the whole party back: the run ends for all its members at
        // once, and a member's request leaves it running. The run is reached through the
        // requester's party, so that party's leader is the run's. A selection-intent holder
        // normally has no run at all, so this is defensive.
        var currentRun = dungeonRuns.Find(session.CharacterId);
        IReadOnlyList<CharacterId> ended = currentRun is not null
            && parties.FindParty(new PartyCharacterSnapshot(
                session.AccountId, session.CharacterId, session.WireUserId, null)).LeaderWireUserId == session.WireUserId
            ? await dungeonRuns.EndRunAsync(currentRun.RunId).ConfigureAwait(false)
            : [];
        foreach (var character in ended)
            departureSink.ParticipantDeparted(currentRun!.RunId, character);
        foreach (var member in returning)
        {
            if (ended.Contains(member.Session.CharacterId))
                endedRuns++;

            areaEntries.Add(new TownAreaEntry(
                member.Session,
                member.Runtime,
                member.Position));
        }

        logger.LogInformation(
            "Back to village {ConnectionId} user={WireUserId}: {TownId}:{AreaId} pos=({X},{Y}) "
                + "members={MemberCount} runsEnded={RunsEnded}",
            context.Connection.ConnectionId,
            session.WireUserId,
            position.TownId,
            position.AreaId,
            position.X,
            position.Y,
            returning.Length,
            endedRuns);

        await outbox.RegisterExplicitAsync(
            context.Connection.ConnectionId,
            new GameExplicitMessageDelivery([new BackToVillageResponse()], []),
            cancellationToken).ConfigureAwait(false);
        await ReturnTownAsync(areaEntries, cancellationToken)
            .ConfigureAwait(false);
        return true;
    }

    private async ValueTask<IReadOnlyList<ReturningMember>> ResolveReturningMembersAsync(
        SelectedSessionIdentity requester,
        SelectCharacterResult requesterRuntime,
        CharacterPositionState requesterPosition,
        CancellationToken cancellationToken)
    {
        var returning = new List<ReturningMember>
        {
            new(requester, requesterRuntime, requesterPosition),
        };
        var requesterCharacter = new PartyCharacterSnapshot(
            requester.AccountId, requester.CharacterId, requester.WireUserId, null);
        var party = parties.FindParty(requesterCharacter);
        if (entryIntents is null
            || party.IsVirtual
            || party.LeaderWireUserId != requester.WireUserId)
        {
            return returning;
        }

        foreach (var member in party.Members.OrderBy(value => value.Slot))
        {
            var current = sessions.TryGetInWorld(member.CharacterId, out var inWorld) ? inWorld : null;
            var runtime = current is null ? null
                : await currentViews.TryLoadAsync(current, cancellationToken).ConfigureAwait(false);
            if (current is null
                || current == requester
                || current.AccountId != member.Character.AccountId
                || current.WireUserId != member.WireUserId
                || runtime is null
                || onlineCharacters.PositionOf(current) is not { } position
                || !entryIntents.ClearExact(current))
            {
                continue;
            }

            returning.Add(new ReturningMember(current, runtime, position));
        }

        return returning;
    }

    private sealed record ReturningMember(
        SelectedSessionIdentity Session,
        SelectCharacterResult Runtime,
        CharacterPositionState Position);
    private async ValueTask<bool> EnterSelectDungeonAsync(CommandExecutionContext execution,
        NetworkDispatchContext context,
        EnterSelectDungeonRequest message,
        CancellationToken cancellationToken)
    {
        if (GameRequestFreshness.IsStale(message.Metadata)) return true;
        var session = sessions.FindByConnection(context.Connection.ConnectionId);
        // The one hand-over between channels: a released Boss tower run leaves the leader in town,
        // so the same request opens the ordinary selection without locating it again. A run the
        // tower kept ends the request with its owner.
        if (session is not null
            && DungeonLocator.Locate(session, participation, bossTowers, illusionTowers) is DungeonPlace.InBossTower boss
            && !await SelectionPrepareBossAsync(execution, boss, message.Selection, cancellationToken).ConfigureAwait(false))
            return true;
        return await EnterOrdinarySelectionAsync(context, message, session, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<bool> EnterOrdinarySelectionAsync(
        NetworkDispatchContext context,
        EnterSelectDungeonRequest message,
        SelectedSessionIdentity? session,
        CancellationToken cancellationToken)
    {
        var result = session is null ? null
            : await currentViews.TryLoadAsync(session, cancellationToken).ConfigureAwait(false);
        if (session is null || result is null)
        {
            // Reached before a character is published. Nothing can be answered from an unpublished
            // session, and inventing a spawn would put the client somewhere the server does not
            // believe it is.
            logger.LogWarning(
                "Enter select dungeon {ConnectionId}: no published session, ignoring selection {Selection}",
                context.Connection.ConnectionId,
                message.Selection);
            return false;
        }

        var spawn = result.World.Channel.Spawn;
        var requesterCharacter = new PartyCharacterSnapshot(
            session.AccountId, session.CharacterId, session.WireUserId, null);
        var roster = parties.FindParty(requesterCharacter);
        if (roster.LeaderWireUserId != session.WireUserId)
        {
            logger.LogWarning(
                "Enter select dungeon {ConnectionId}: non-leader ignored",
                context.Connection.ConnectionId);
            return false;
        }
        var attached = await ResolveSelectionMembersAsync(
            session, result, roster, cancellationToken).ConfigureAwait(false);
        if (attached.Count != roster.Members.Length)
            return false;
        if (attached.Any(x => !sessions.IsInWorld(x.Session)))
            return false;
        var intent = message.Selection == 0
            ? DungeonEntryIntent.Interactive
            : entryIntents!.Find(session) is { Kind: DungeonEntryIntentKind.OrdinaryRechallenge } retry
                && retry.DirectedDungeonId == message.Selection
                ? retry
                : DungeonEntryIntent.Directed(message.Selection);
        var wireUserIds = attached
            .Select(value => value.Session.WireUserId)
            .ToImmutableArray();
        foreach (var member in attached)
        {
            entryIntents!.Record(member.Session, intent);
        }
        await TownDepartureProjection.TownLeaveManyAsync(sessions, townAreas, projections, projectionState, currentViews, parties,
            attached.Select(member => member.Session).ToArray(),
            TownAreaDepartureReason.DungeonSelection,
            cancellationToken).ConfigureAwait(false);
        logger.LogInformation(
            "Enter select dungeon {ConnectionId} user={WireUserId}: selection={Selection} intent={Intent} "
                + "town={TownId} area={AreaId}->0xFF pos=({X},{Y})",
            context.Connection.ConnectionId,
            session.WireUserId,
            message.Selection,
            intent,
            spawn.TownId,
            spawn.AreaId,
            spawn.X,
            spawn.Y);

        // Addressed to the connection the request arrived on. The session records the connection
        // it was published against, and while the two are the same today, a reply belongs to the
        // asker by construction. Both are logged so a divergence shows up as data rather than as
        // silence.
        if (session.ConnectionId != context.Connection.ConnectionId)
        {
            logger.LogWarning(
                "Enter select dungeon {ConnectionId}: session is bound to {SessionConnectionId}",
                context.Connection.ConnectionId,
                session.ConnectionId);
        }

        // A21's answer, in its order: the stat block, the acceptance, the user state, the UDP
        // offer. Two frames this used to send are gone from here:
        //
        //   NOTI 0x0017 USER_AREA -- the A21 reference sends it exactly twice in a whole session,
        //   both on the way back to town at the end, and never when the selection screen opens.
        //
        //   NOTI 0x001B -- it belongs to the SELECT_DUNGEON answer, where A21 sends it directly
        //   after the second acceptance byte.
        // The selection state closes the burst for everyone but the tutorial, whose first entry
        // defers it -- and every frame this server had been checked against came from a tutorial,
        // so it was only ever sent at the deferred moment. A player at a real dungeon gate got the
        // screen's other three frames and no state to draw it from (2026-08-26).
        var deliveries = new List<(
            SelectedSessionIdentity Session,
            IReadOnlyList<IGameServerMessage> Messages)>();
        foreach (var member in attached)
        {
            var refreshed = await currentState.RefreshAsync(member.Runtime, cancellationToken)
                .ConfigureAwait(false);
            var equipment = refreshed.CharacterInventory.Containers
                .SingleOrDefault(value => value.ContainerType == InventoryContainerType.Equipment)
                ?.Items ?? [];
            List<IGameServerMessage> notifications =
            [
                new ExtendedUserStateMessage(
                    refreshed.Character,
                    refreshed.CharacterEntry.Public,
                    refreshed.CharacterEntry.Skills,
                    equipment,
                    refreshed.World.PreparedAt),
                new EnterSelectDungeonAcknowledgement(),
                new UserStateNotification(member.Session.WireUserId, 0x01),
                new UdpHostNotification(roster.LeaderMember.Slot),
            ];
            notifications.AddRange(await HousingCurrentProjection.ReadAsync(housing, member.Session, cancellationToken).ConfigureAwait(false));
            if (!visits.DefersSelectionState(refreshed.Character.Progression.Level, refreshed.Character.TutorialCompleted))
            {
                notifications.Add(new DungeonPermissionStateMessage(refreshed.CharacterEntry.Progression.Dungeons));
                var tower = despair is null ? null
                    : await despair.ReadStatusAsync(member.Session.CharacterId, cancellationToken).ConfigureAwait(false);
                if (tower is not null)
                    notifications.AddRange(tower.DungeonIds.Select(id =>
                        new DungeonRemainingEntriesMessage(id, tower.FreeEntriesRemaining)));
                if (await sao.ReadStatusAsync(cancellationToken).ConfigureAwait(false) is { } saoProgress)
                    notifications.Add(new SaoCurrentFloorNotification(saoProgress));
                notifications.Add(new EnterSelectDungeonNotification(wireUserIds, checked((ushort)(tower?.NextFloor ?? 1))));
            }
            deliveries.Add((member.Session, notifications));
        }

        if (!sessions.IsInWorld(session))
            return false;
        deliveries = deliveries
            .Where(value => sessions.IsInWorld(value.Session))
            .ToList();
        var direct = deliveries.SingleOrDefault(value => value.Session == session);
        if (direct == default)
            return false;

        // Logged here rather than left to the dispatch layer. The first live run produced this
        // handler's own log line and no frames at all, with nothing reported anywhere -- the
        // dispatch-layer catch is known not to fire, so an exception raised past this point has no
        // reporting path unless the handler provides one.
        try
        {
            await outbox.RegisterExplicitAsync(
                context.Connection.ConnectionId,
                new GameExplicitMessageDelivery(
                    direct.Messages,
                    [.. deliveries
                        .Where(value => value.Session != session)
                        .Select(value => new GameExplicitMessageTarget(
                            value.Session.ConnectionId,
                            value.Messages))]),
                cancellationToken).ConfigureAwait(false);
            foreach (var member in deliveries)
                await SelectionQuotaAsync(member.Session, cancellationToken).ConfigureAwait(false);
            logger.LogInformation(
                "Enter select dungeon {ConnectionId}: {Count} notifications handed to the sender",
                context.Connection.ConnectionId,
                deliveries.Sum(value => value.Messages.Count));
        }
        catch (Exception)
        {
            logger.LogDebug(
                "Enter select dungeon {ConnectionId}: sending the notification burst failed",
                context.Connection.ConnectionId);
            throw;
        }
        return true;
    }

    private async ValueTask<IReadOnlyList<(
        SelectedSessionIdentity Session,
        SelectCharacterResult Runtime)>> ResolveSelectionMembersAsync(
        SelectedSessionIdentity requester,
        SelectCharacterResult requesterRuntime,
        PartySnapshot roster,
        CancellationToken cancellationToken)
    {
        var members = new List<(SelectedSessionIdentity, SelectCharacterResult)>();
        foreach (var member in roster.Members.OrderBy(value => value.Slot))
        {
            var current = sessions.TryGetInWorld(member.CharacterId, out var inWorld) ? inWorld : null;
            var runtime = current is null ? null
                : await currentViews.TryLoadAsync(current, cancellationToken).ConfigureAwait(false);
            if (current is null
                || current.AccountId != member.Character.AccountId
                || current.WireUserId != member.WireUserId
                || runtime is null)
            {
                return [];
            }
            members.Add((current, runtime));
        }

        return members.Any(value => value.Item1 == requester)
            ? members
            : [];
    }
}
