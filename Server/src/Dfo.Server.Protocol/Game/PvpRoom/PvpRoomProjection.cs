using System.Collections.Immutable;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Pvp;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.DungeonSelection;
using Dfo.Server.Protocol.Game.Party;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Dfo.Server.Protocol.Game.TownProjection;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.PvpRoom;

/// <summary>The same frames, in order, for every recipient of a room change.</summary>
public sealed record PvpRoomBroadcast(IReadOnlyList<IGameServerMessage> Messages) : INotification<PvpRoomBroadcast>;

public sealed class PvpRoomBroadcastHandler : INotificationHandler<PvpRoomBroadcast>
{
    public ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(
        NotificationRecipient recipient, PvpRoomBroadcast notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        return ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>([.. notification.Messages]);
    }
}

/// <summary>What the lobby on a room's listener sees of a committed room change, as frames.</summary>
/// <remarks>
/// Pure: the frames that keep every lobby client's view of the rooms equal to a fresh <c>0x0029</c> snapshot.
/// A new room arrives whole in <c>0x0029</c>; a seat that changes who or which side holds it, or whether it is
/// open, arrives in <c>0x002B</c> (which also carries the mode, so a mode change sends every taken seat);
/// a change of owner, map or waiting/battle state in <c>0x002A</c>; a room that is gone as <c>0x002A</c> state 0.
/// A ready flag is not part of the lobby view and is never sent here.
/// </remarks>
public static class PvpRoomLobbyFrames
{
    private static readonly PvpRoomSeatState Empty = new(PvpRoomSeatOccupancy.Open, 0, 0, false);

    public static IReadOnlyList<IGameServerMessage> For(PvpRoomSnapshot? before, PvpRoomSnapshot? after)
    {
        if (after is null)
        {
            if (before is null) return [];
            // A room that is gone: its last seats shown empty first, so no client keeps anyone seated in it.
            var gone = PvpRoomLobbyView.Of(before);
            var taken = Enumerable.Range(0, PvpRoomSnapshot.SeatCount)
                .Where(seat => gone.Seats[seat].Occupancy == PvpRoomSeatOccupancy.Taken).ToArray();
            List<IGameServerMessage> teardown = [];
            if (taken.Length > 0)
                teardown.Add(new PvpSeatStateMessage(gone.RoomNumber, gone.BattleMode, [.. taken.Select(seat => (seat, Empty))]));
            teardown.Add(PvpRoomStateMessage.Destroyed(gone.RoomNumber));
            return teardown;
        }
        var next = PvpRoomLobbyView.Of(after);
        if (before is null) return [new PvpRoomInfoMessage([next])];
        var previous = PvpRoomLobbyView.Of(before);
        var modeChanged = next.BattleMode != previous.BattleMode;
        var seats = Enumerable.Range(0, PvpRoomSnapshot.SeatCount)
            .Where(seat => !SameSeat(previous.Seats[seat], next.Seats[seat])
                || modeChanged && next.Seats[seat].Occupancy == PvpRoomSeatOccupancy.Taken)
            .ToArray();
        List<IGameServerMessage> frames = [];
        if (seats.Length > 0)
            frames.Add(new PvpSeatStateMessage(next.RoomNumber, next.BattleMode, [.. seats.Select(seat => (seat, next.Seats[seat]))]));
        if (next.OwnerSeat != previous.OwnerSeat || next.MapIndex != previous.MapIndex || next.Waiting != previous.Waiting)
            frames.Add(PvpRoomStateMessage.Of(next));
        return frames;
    }

    /// <summary>Whether a fresh snapshot shows the seat the same, the ready flag aside.</summary>
    private static bool SameSeat(PvpRoomSeatState left, PvpRoomSeatState right) =>
        left.Occupancy == right.Occupancy && left.Team == right.Team && left.UserId == right.UserId;
}

/// <summary>
/// Projects committed room changes: the acting member's own frames straight to its connection, the room's
/// members and the listener's lobby through exact-session notifications, and the town moves the change made.
/// </summary>
/// <remarks>
/// Called only after the write unit that made the change has returned, so <see cref="IPvpTownMoves"/> is
/// filled in and every snapshot it reads is the committed one; it never asks the room owner again. Frame
/// orders follow design-r1 §4 a and upstream <c>PvpRoomHandler</c> (make, enter, seat, ready, start).
/// The battle projection reuses <see cref="ProjectAsync"/> for the rooms its deadline, departure and settlement steps
/// change: a battle going back to waiting and a room ended by its last member's departure project as here.
/// <para>
/// Seats emptied while a battle runs (Spec RQ-035, upstream <c>PvpRoomHandler.Departures</c>): a member's client keeps
/// its battle actors until it leaves the result screen, so the room's members are not shown them then. The lobby is
/// told at once; each member is noted in <c>deferrals</c> as owed the room's seats, and is shown them,
/// as the room then stands, when its own <c>0x0039</c> is accepted, when the next battle starts, or when it leaves
/// the room. A member whose session ended, or whose room is gone, is owed nothing any more. Without
/// <c>deferrals</c> (the party commands' room entry) nothing is owed.
/// </para>
/// </remarks>
internal sealed class PvpRoomProjector(
    ISelectedSessionDomainService sessions,
    IPvpLobbyDomainService lobby,
    ISelectedCharacterCurrentViewDomainService currentViews,
    IOnlineCharacterRuntimeDomainService online,
    IOnlineUserQueryDomainService onlineUsers,
    IGameSessionEndpointDomainService endpoints,
    ITownAreaDomainService townAreas,
    IPartyService parties,
    IPartyProjectionStateDomainService projectionState,
    IWeaknessDomainService weakness,
    ICommandProjections projections,
    IGameCommandOutbox outbox,
    ILogger logger,
    IPvpResultScreenDeferralDomainService? deferrals = null)
{
    /// <summary>USER_STATE of a character in a duel room (upstream <c>PvpUserState</c>).</summary>
    public const byte InRoomUserState = 0x02;

    /// <summary>USER_STATE of a character standing in town.</summary>
    public const byte InTownUserState = 0x00;

    /// <summary>The area a character in a duel room is shown in (upstream <c>PvpAreaId</c>).</summary>
    public const byte RoomAreaId = 0xFE;

    public ValueTask ProjectAsync(PvpRoomOutcome.Changed change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);
        return change.Kind switch
        {
            PvpRoomChangeKind.Created => CreatedAsync(change, cancellationToken),
            PvpRoomChangeKind.Entered => EnteredAsync(change, invited: false, cancellationToken),
            PvpRoomChangeKind.InviteAccepted => EnteredAsync(change, invited: true, cancellationToken),
            PvpRoomChangeKind.MatchStarted => StartedAsync(change, cancellationToken),
            _ => ChangedAsync(change, cancellationToken),
        };
    }

    /// <summary>The owner: own USERINFO1, the room, out of town; the rest of the listener: the room and the owner's state.</summary>
    private async ValueTask CreatedAsync(PvpRoomOutcome.Changed change, CancellationToken cancellationToken)
    {
        var room = change.After ?? throw new InvalidOperationException("A created duel room has a snapshot.");
        var owner = room.Owner.Session;
        var state = PvpRoomLobbyView.Of(room);
        if (await currentViews.TryLoadAsync(owner, cancellationToken).ConfigureAwait(false) is { } view)
            await outbox.RegisterExplicitAsync(owner.ConnectionId, new GameExplicitMessageDelivery(
                [Full(view, null), new PvpRoomInfoMessage([state]), RoomArea(owner, view)], []), cancellationToken).ConfigureAwait(false);
        else
            logger.LogWarning("Duel room {Room} created by character {Character} whose current view is gone; owner frames skipped",
                room.Number, owner.CharacterId.Value);
        await BroadcastAsync(Listener(room.ListenerPort, except: [owner]),
            [.. PvpRoomLobbyFrames.For(null, room), new UserStateNotification(owner.WireUserId, InRoomUserState)],
            cancellationToken).ConfigureAwait(false);
        await DepartTownAsync(change, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The newcomer: a USERINFO0 roster of everyone seated, the members' USERINFO1 and endpoints, its own USERINFO1 and endpoint, the command
    /// reply unless invited, its seat and out of town. The members: the newcomer's identity, USERINFO1 and endpoint.
    /// The listener: the seat and the newcomer's state.
    /// </summary>
    private async ValueTask EnteredAsync(PvpRoomOutcome.Changed change, bool invited, CancellationToken cancellationToken)
    {
        var before = change.Before ?? throw new InvalidOperationException("An entered duel room existed before.");
        var room = change.After ?? throw new InvalidOperationException("An entered duel room still stands.");
        var seat = Enumerable.Range(0, PvpRoomSnapshot.SeatCount)
            .Single(index => before.Seats[index] is not PvpSeat.Taken && room.Seats[index] is PvpSeat.Taken);
        var newcomer = ((PvpSeat.Taken)room.Seats[seat]).Session;
        var members = room.Members.Where(x => x.Seat != seat).Select(x => x.Member.Session).ToArray();
        var newcomerView = await currentViews.TryLoadAsync(newcomer, cancellationToken).ConfigureAwait(false);
        var memberViews = new List<(SelectedSessionIdentity Session, SelectCharacterResult View)>();
        foreach (var member in members)
            if (await currentViews.TryLoadAsync(member, cancellationToken).ConfigureAwait(false) is { } view)
                memberViews.Add((member, view));
        var state = PvpRoomLobbyView.Of(room);
        var seatFrame = new PvpSeatStateMessage(room.Number, state.BattleMode, [(seat, state.Seats[seat])]);

        if (newcomerView is not null)
        {
            List<IGameServerMessage> own = [];
            // Upstream PvpRoomHandler (enter): one USERINFO0 roster of every member, the newcomer included, by user id;
            // USERINFO1 and the endpoint notice need the user records it establishes.
            own.Add(new PvpParticipantRosterMessage([.. memberViews.Append((newcomer, newcomerView))
                .OrderBy(x => x.Item1.WireUserId).Select(x => Identity(x.Item2))]));
            own.AddRange(memberViews.Select(x => Full(x.View, x.Session.WireUserId)));
            if (memberViews.Count > 0) own.Add(Endpoints([.. memberViews.Select(x => x.Session)]));
            own.Add(Full(newcomerView, null));
            own.Add(Endpoints([newcomer]));
            if (!invited) own.Add(new EnterPvpRoomAccepted([.. room.Seats.Select(x => x is PvpSeat.Taken { Ready: true })]));
            own.Add(seatFrame);
            own.Add(RoomArea(newcomer, newcomerView));
            await outbox.RegisterExplicitAsync(newcomer.ConnectionId, new GameExplicitMessageDelivery(own, []), cancellationToken)
                .ConfigureAwait(false);
            if (members.Length > 0)
                await BroadcastAsync(members,
                    [new PvpParticipantPublicationMessage(Identity(newcomerView)), Full(newcomerView, newcomer.WireUserId),
                        Endpoints([newcomer])], cancellationToken).ConfigureAwait(false);
        }
        else
            logger.LogWarning("Duel room {Room} entered by character {Character} whose current view is gone; entry frames skipped",
                room.Number, newcomer.CharacterId.Value);
        await BroadcastAsync(Listener(room.ListenerPort, except: [newcomer]),
            [.. PvpRoomLobbyFrames.For(before, room), new UserStateNotification(newcomer.WireUserId, InRoomUserState)],
            cancellationToken).ConfigureAwait(false);
        await DepartTownAsync(change, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The fighters: the owner's ready flag, the start and the room's battle state; the rest of the listener: the
    /// battle state (upstream <c>HandleSetReadyStateWithinTransition</c>, design-r1 §4 a).
    /// </summary>
    private async ValueTask StartedAsync(PvpRoomOutcome.Changed change, CancellationToken cancellationToken)
    {
        var room = change.After ?? throw new InvalidOperationException("A started duel room still stands.");
        var match = room.Match ?? throw new InvalidOperationException("A started duel room has a battle.");
        var state = PvpRoomStateMessage.Of(PvpRoomLobbyView.Of(room));
        var members = room.Members.Select(x => x.Member.Session).ToArray();
        // A new battle settles what the last one still owed its members, the room as it waited.
        foreach (var member in members)
            await ReleaseAsync(member, change.Before, cancellationToken).ConfigureAwait(false);
        await BroadcastAsync(members,
            [new PvpReadyStateMessage(room.OwnerSeat, true), new StartPvpMessage(checked((byte)match.Map), (byte)room.Mode), state],
            cancellationToken).ConfigureAwait(false);
        await BroadcastAsync(Listener(room.ListenerPort, except: members), [state], cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Any other change: ready flags to the members of a room still waiting, then what the lobby sees (members taken
    /// off their seats first shown as back in town), then the town return of those sent back, and only after it the
    /// room's change to them.
    /// </summary>
    private async ValueTask ChangedAsync(PvpRoomOutcome.Changed change, CancellationToken cancellationToken)
    {
        var anchor = change.After ?? change.Before ?? throw new InvalidOperationException("A duel room change names a room.");
        if (change is { Before: { Phase: PvpRoomPhase.Waiting } before, After: { Phase: PvpRoomPhase.Waiting } after })
        {
            var flags = Enumerable.Range(0, PvpRoomSnapshot.SeatCount)
                .Where(seat => before.Seats[seat] is PvpSeat.Taken was && after.Seats[seat] is PvpSeat.Taken now
                    && was.Session == now.Session && was.Ready != now.Ready)
                .Select(seat => (IGameServerMessage)new PvpReadyStateMessage(seat, ((PvpSeat.Taken)after.Seats[seat]).Ready))
                .ToArray();
            if (flags.Length > 0)
                await BroadcastAsync([.. after.Members.Select(x => x.Member.Session)], flags, cancellationToken).ConfigureAwait(false);
        }

        // Upstream (PvpRoomHandler leave, CompleteKickedMemberExitAsync, CompleteOwnerExitAsync): the lobby, the one
        // sent back excluded, hears that member is back in town and the room's change; the member itself first returns
        // to town, and only then is told the room's change.
        var returning = change.Released.Where(x => x.Reason != PvpSeatReleaseReason.Departed).Select(x => x.Session).ToArray();
        var roomFrames = PvpRoomLobbyFrames.For(change.Before, change.After);
        // A seat emptied while the room still holds a battle is owed to its members, not shown to them now.
        SelectedSessionIdentity[] owed = deferrals is not null && change.Released.Count > 0
            && change.After is { Phase: not PvpRoomPhase.Waiting } fighting
            // A member who already closed its result screen has left the battle's actors behind: it is shown the seat now.
            ? [.. fighting.Members.Select(x => x.Member.Session).Where(member => fighting.Match?.FighterOf(member) is not { } index
                || !fighting.Match.Fighters[index].Reports.HasFlag(PvpFighterReports.ResultAcknowledged))]
            : [];
        foreach (var member in owed) deferrals!.Defer(member, change.After!.Id);
        List<IGameServerMessage> lobbyFrames = [];
        lobbyFrames.AddRange(returning.Select(x => (IGameServerMessage)new UserStateNotification(x.WireUserId, InTownUserState)));
        lobbyFrames.AddRange(roomFrames);
        if (lobbyFrames.Count > 0)
            await BroadcastAsync(Listener(anchor.ListenerPort, except: [.. returning, .. owed]), lobbyFrames, cancellationToken)
                .ConfigureAwait(false);
        await ReturnToTownAsync(change, cancellationToken).ConfigureAwait(false);
        if (returning.Length > 0)
            await BroadcastAsync(returning, roomFrames, cancellationToken).ConfigureAwait(false);
        if (deferrals is null) return;
        // Leaving the room settles what it owed: one sent back to the lobby is shown the room as it now stands there,
        // one whose session ended is owed nothing.
        foreach (var released in change.Released)
            await ReleaseAsync(released.Session, released.Reason == PvpSeatReleaseReason.Departed ? null : change.After,
                cancellationToken).ConfigureAwait(false);
        if (change is { After: null, Before: { } gone }) deferrals.DropRoom(gone.Id);
    }

    /// <summary>
    /// Settles what <paramref name="member"/> is owed of the room's seats: every seat and the room's state, as
    /// <paramref name="room"/> stands, when that is the room it is owed (upstream <c>FlushDeferredPvpDeparturesAsync</c>).
    /// A debt of another room, or with no room to show, is dropped.
    /// </summary>
    public async ValueTask ReleaseAsync(SelectedSessionIdentity member, PvpRoomSnapshot? room, CancellationToken cancellationToken)
    {
        if (deferrals?.Release(member) is not { } owing || room is null || room.Id != owing) return;
        await BroadcastAsync([member], ReleaseFrames(room), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>All eight seats in one <c>0x002B</c>, then the room's <c>0x002A</c>.</summary>
    public static IReadOnlyList<IGameServerMessage> ReleaseFrames(PvpRoomSnapshot room)
    {
        var view = PvpRoomLobbyView.Of(room);
        return
        [
            new PvpSeatStateMessage(view.RoomNumber, view.BattleMode,
                [.. Enumerable.Range(0, PvpRoomSnapshot.SeatCount).Select(seat => (seat, view.Seats[seat]))]),
            PvpRoomStateMessage.Of(view),
        ];
    }

    private async ValueTask DepartTownAsync(PvpRoomOutcome.Changed change, CancellationToken cancellationToken)
    {
        if (!change.Town.Applied)
        {
            logger.LogWarning("Duel room change {Kind} projected before its town moves were made", change.Kind);
            return;
        }
        if (change.Town.Departures.Count > 0)
            _ = await TownDepartureProjection.TownProjectDeparturesAsync(sessions, townAreas, projections, projectionState,
                currentViews, parties, change.Town.Departures, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask ReturnToTownAsync(PvpRoomOutcome.Changed change, CancellationToken cancellationToken)
    {
        if (change.Town.Returns.Count == 0) return;
        if (!change.Town.Applied)
        {
            logger.LogWarning("Duel room change {Kind} projected before its town moves were made", change.Kind);
            return;
        }
        var admissions = new List<TownAreaAdmission>();
        foreach (var admitted in change.Town.Returns)
        {
            var session = admitted.Request.Session;
            if (await currentViews.TryLoadAsync(session, cancellationToken).ConfigureAwait(false) is not { } view)
                continue;
            admissions.Add(new TownAreaAdmission(new TownAreaEntry(session, view, admitted.Request.Position), admitted.Change,
                admitted.Previous is { SharesTownPresence: true } previous ? TownArrivalProjection.TownScope(previous.Area) : null));
        }
        if (admissions.Count > 0)
            _ = await TownArrivalProjection.ProjectAsync(sessions, townAreas, currentViews, outbox, weakness, false, projections,
                new PartyTownProjectionDelivery(parties, townAreas, sessions, projections, projectionState), admissions,
                cancellationToken, ownState: false).ConfigureAwait(false);
    }

    /// <summary>Everyone ready in the duel lobby on this listener, room members included, as exact sessions.</summary>
    /// <remarks>
    /// The existing lobby publication's audience (<see cref="IPvpLobbyDomainService.ReadyOn"/>); upstream sends
    /// room changes to every lobby-ready session on the listener alike, the room's own members among them.
    /// </remarks>
    public SelectedSessionIdentity[] Listener(int listenerPort, IReadOnlyCollection<SelectedSessionIdentity> except)
    {
        var audience = new List<SelectedSessionIdentity>();
        foreach (var participant in lobby.ReadyOn(listenerPort, default))
        {
            var session = participant.Session is { } exact ? exact
                : sessions.TryGetInWorld(participant.CharacterId, out var found) ? found : null;
            if (session is not null && !except.Contains(session)) audience.Add(session);
        }
        return [.. audience];
    }

    public async ValueTask BroadcastAsync(
        IReadOnlyList<SelectedSessionIdentity> audience, IReadOnlyList<IGameServerMessage> frames, CancellationToken cancellationToken)
    {
        if (audience.Count == 0 || frames.Count == 0) return;
        _ = await projections.ProjectAsync(new NotificationAudience.TheseSessions(audience), new PvpRoomBroadcast(frames),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>USERINFO subtype 1; <paramref name="routing"/> is another member's user id, null for the receiver's own.</summary>
    public static ExtendedUserStateMessage Full(SelectCharacterResult view, ushort? routing) => new(
        view.Character, view.CharacterEntry.Public, view.CharacterEntry.Skills, Equipment(view), view.World.PreparedAt, routing);

    /// <summary>USERINFO subtype 0, the identity the lobby roster carries.</summary>
    public static PvpParticipantEntryState Identity(SelectCharacterResult view) => new(
        view.Character, view.CharacterEntry.Public, Equipment(view), view.CharacterEntry.Features.Pets);

    private static IReadOnlyList<InventoryItemState> Equipment(SelectCharacterResult view) =>
        view.CharacterInventory.Containers.SingleOrDefault(value => value.ContainerType == InventoryContainerType.Equipment)?.Items ?? [];

    /// <summary>USER_AREA moving the character into the room area, where it stands (upstream <c>PvpAreaId</c>).</summary>
    private UserAreaNotification RoomArea(SelectedSessionIdentity session, SelectCharacterResult view)
    {
        var spawn = view.World.Channel.Spawn;
        if (online.PositionOf(session) is { } position)
            spawn = spawn with { TownId = position.TownId, X = position.X, Y = position.Y, Direction = position.Direction };
        return new UserAreaNotification(session.WireUserId, spawn, RoomAreaId);
    }

    /// <summary>NOTI <c>0x000B</c> of these sessions' P2P endpoints, through the party endpoint encoder.</summary>
    private PartyEndpointsMessage Endpoints(IReadOnlyList<SelectedSessionIdentity> members) =>
        new(new PartySnapshot(0, 0, 0, members[0].WireUserId, new PartySettings(new byte[PartySettings.WireLength]), PartyTitle.Empty,
            [.. members.Select((member, index) => new PartyMemberSnapshot(
                new PartyCharacterSnapshot(member.AccountId, member.CharacterId, member.WireUserId, null),
                checked((byte)index), EndpointOf(member), 100))],
            IsVirtual: true));

    private SessionEndpoint? EndpointOf(SelectedSessionIdentity session) =>
        onlineUsers.TryGetByConnection(session.ConnectionId, out var user) && user is not null
            ? endpoints.Find(user.SessionIdentity)
            : null;
}
