using System.Collections.Immutable;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Pvp;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Sessions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.DungeonSelection;
using Dfo.Server.Protocol.Game.Party;
using Dfo.Server.Protocol.Game.PvpRoom;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Dfo.Server.Protocol.Tests.Game.Party;
using Dfo.Server.Protocol.Tests.Game.SelectCharacter;
using Dfo.Server.Testing;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dfo.Server.Protocol.Tests.Game.PvpRoom;

/// <summary>
/// The room commands against a scripted room owner: what each answers, in which order, and to whom
/// (design-r1 §4 a; Spec AC-010–AC-013 projection side, AC-003 lobby part, AC-075 for 0x0036).
/// </summary>
public sealed class PvpRoomCommandTests
{
    [Fact]
    public Task Making_a_room_sends_the_owner_its_frames_and_the_lobby_the_room() => Run(async world =>
    {
        var room = PvpRooms.Room(1, PvpBattleMode.Team, 0, seats: [(0, PvpRooms.Taken(world.A))]);
        world.Rooms.Next = PvpRooms.Changed(PvpRoomChangeKind.Created, null, room);

        Assert.True(await world.SendAsync(new GameMakePvpRoomCommand(Dispatch(world.A), new(GameMessageMetadata.Empty,
            new MakePvpRoomInput(0, [0x41], 2, null, 0)))));

        var own = world.Direct(world.A);
        Assert.Collection(own,
            frame => Assert.Null(Assert.IsType<ExtendedUserStateMessage>(frame).RoutingWireUserId),
            frame => Assert.Equal((ushort)1, Assert.Single(Assert.IsType<PvpRoomInfoMessage>(frame).Rooms).RoomNumber),
            frame => Assert.Equal(PvpRoomProjector.RoomAreaId, Assert.IsType<UserAreaNotification>(frame).AreaId));
        Assert.DoesNotContain(own, frame => frame is IGameCmdMessage);
        Assert.Empty(world.Projections.FramesFor(world.A));
        foreach (var lobby in new[] { world.B, world.C })
            Assert.Collection(world.Projections.FramesFor(lobby),
                frame => Assert.IsType<PvpRoomInfoMessage>(frame),
                frame => Assert.Equal((world.A.WireUserId, PvpRoomProjector.InRoomUserState),
                    (Assert.IsType<UserStateNotification>(frame).WireUserId, ((UserStateNotification)frame).UserState)));
    });

    [Theory]
    [InlineData(PvpRoomRefusal.InParty, 19)]
    [InlineData(PvpRoomRefusal.AlreadySeated, 19)]
    [InlineData(PvpRoomRefusal.InvalidMap, 19)]
    public Task A_refused_room_is_answered_on_its_own_command(PvpRoomRefusal refusal, byte reason) => Run(async world =>
    {
        world.Rooms.Next = new PvpRoomOutcome.Refused(refusal);
        Assert.False(await world.SendAsync(new GameMakePvpRoomCommand(Dispatch(world.A), new(GameMessageMetadata.Empty,
            new MakePvpRoomInput(1, [], 0, null, 0)))));
        AssertRefused(world, world.A, ServerMessageType.GameMakePvpRoom, reason);
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task A_practice_room_or_a_malformed_request_never_reaches_the_room_owner(bool practice) => Run(async world =>
    {
        Assert.False(await world.SendAsync(new GameMakePvpRoomCommand(Dispatch(world.A), new(GameMessageMetadata.Empty,
            practice ? new MakePvpRoomInput(1, [], 0, null, 1) : null))));
        Assert.Empty(world.Rooms.Calls);
        AssertRefused(world, world.A, ServerMessageType.GameMakePvpRoom, 19);
    });

    [Fact]
    public Task Entering_sends_the_r1_order_to_the_newcomer_the_members_and_the_lobby() => Run(async world =>
    {
        var before = PvpRooms.Room(1, PvpBattleMode.Team, 0, seats: [(0, PvpRooms.Taken(world.A, ready: true))]);
        var after = before with { Seats = before.Seats.SetItem(1, PvpRooms.Taken(world.B, PvpTeam.Second)) };
        world.Rooms.Next = PvpRooms.Changed(PvpRoomChangeKind.Entered, before, after);

        Assert.True(await world.SendAsync(new GameEnterPvpRoomCommand(Dispatch(world.B), new(GameMessageMetadata.Empty, 1, null, true))));

        Assert.Collection(world.Direct(world.B),
            frame => Assert.Equal([world.A.CharacterId, world.B.CharacterId],
                Assert.IsType<PvpParticipantRosterMessage>(frame).Participants.Select(x => x.Character.Identity.CharacterId)),
            frame => Assert.Equal(world.A.WireUserId, Assert.IsType<ExtendedUserStateMessage>(frame).RoutingWireUserId),
            frame => Assert.Equal([world.A.WireUserId], Assert.IsType<PartyEndpointsMessage>(frame).Snapshot.Members.Select(x => x.WireUserId)),
            frame => Assert.Null(Assert.IsType<ExtendedUserStateMessage>(frame).RoutingWireUserId),
            frame => Assert.Equal([world.B.WireUserId], Assert.IsType<PartyEndpointsMessage>(frame).Snapshot.Members.Select(x => x.WireUserId)),
            frame => Assert.Equal(new[] { true, false, false, false, false, false, false, false }, Assert.IsType<EnterPvpRoomAccepted>(frame).Ready.ToArray()),
            frame => Assert.Equal(1, Assert.Single(Assert.IsType<PvpSeatStateMessage>(frame).Seats).Seat),
            frame => Assert.Equal(PvpRoomProjector.RoomAreaId, Assert.IsType<UserAreaNotification>(frame).AreaId));
        Assert.Empty(world.Projections.FramesFor(world.B));
        Assert.Collection(world.Projections.FramesFor(world.A),
            frame => Assert.IsType<PvpParticipantPublicationMessage>(frame),
            frame => Assert.Equal(world.B.WireUserId, Assert.IsType<ExtendedUserStateMessage>(frame).RoutingWireUserId),
            frame => Assert.IsType<PartyEndpointsMessage>(frame),
            frame => Assert.IsType<PvpSeatStateMessage>(frame),
            frame => Assert.Equal(PvpRoomProjector.InRoomUserState, Assert.IsType<UserStateNotification>(frame).UserState));
        Assert.Collection(world.Projections.FramesFor(world.C),
            frame => Assert.IsType<PvpSeatStateMessage>(frame),
            frame => Assert.Equal(world.B.WireUserId, Assert.IsType<UserStateNotification>(frame).WireUserId));
    });

    [Fact]
    public Task The_entry_roster_is_ordered_by_user_id_whoever_entered() => Run(async world =>
    {
        var before = PvpRooms.Room(1, PvpBattleMode.Team, 0,
            seats: [(0, PvpRooms.Taken(world.C)), (1, PvpRooms.Taken(world.B, PvpTeam.Second))]);
        var after = before with { Seats = before.Seats.SetItem(2, PvpRooms.Taken(world.A)) };
        world.Rooms.Next = PvpRooms.Changed(PvpRoomChangeKind.Entered, before, after);

        Assert.True(await world.SendAsync(new GameEnterPvpRoomCommand(Dispatch(world.A), new(GameMessageMetadata.Empty, 1, null, true))));

        var roster = Assert.IsType<PvpParticipantRosterMessage>(world.Direct(world.A)[0]);
        Assert.Equal([world.A.CharacterId, world.B.CharacterId, world.C.CharacterId],
            roster.Participants.Select(x => x.Character.Identity.CharacterId));
    });

    [Theory]
    [InlineData(PvpRoomRefusal.WrongPassword, 6)]
    [InlineData(PvpRoomRefusal.Full, 4)]
    [InlineData(PvpRoomRefusal.NotWaiting, 19)]
    [InlineData(PvpRoomRefusal.AlreadySeated, 19)]
    [InlineData(PvpRoomRefusal.InParty, 19)]
    [InlineData(PvpRoomRefusal.RoomMissing, 22)]
    public Task A_refused_entry_answers_its_upstream_code(PvpRoomRefusal refusal, byte reason) => Run(async world =>
    {
        world.Rooms.Next = new PvpRoomOutcome.Refused(refusal);
        Assert.False(await world.SendAsync(new GameEnterPvpRoomCommand(Dispatch(world.B), new(GameMessageMetadata.Empty, 1, null, true))));
        AssertRefused(world, world.B, ServerMessageType.GameEnterPvpRoom, reason);
    });

    [Theory]
    [InlineData(0, 0xFE, PvpSeatAction.Leave, PvpTeam.Individual)]
    [InlineData(1, 0xFE, PvpSeatAction.Kick, PvpTeam.Individual)]
    [InlineData(2, 0xFE, PvpSeatAction.Close, PvpTeam.Individual)]
    [InlineData(2, 0xFF, PvpSeatAction.Open, PvpTeam.Individual)]
    [InlineData(0, 2, PvpSeatAction.ChangeTeam, PvpTeam.Second)]
    public Task A_seat_state_byte_asks_for_one_seat_action(int seat, int state, PvpSeatAction action, PvpTeam team) => Run(async world =>
    {
        world.Rooms.Room = PvpRooms.Room(1, PvpBattleMode.Team, 0, seats: [(0, PvpRooms.Taken(world.A)), (1, PvpRooms.Taken(world.B))]);
        world.Rooms.Next = new PvpRoomOutcome.Refused(PvpRoomRefusal.NotOwner);
        _ = await world.SendAsync(new GameSetPvpSeatStateCommand(Dispatch(world.A),
            new(GameMessageMetadata.Empty, (byte)seat, (byte)state, true)));
        Assert.Equal((action, seat, team), world.Rooms.LastSeat);
        AssertRefused(world, world.A, ServerMessageType.GameSetPvpSeatState, 8);
    });

    [Theory]
    [InlineData(1, 3, true)]
    [InlineData(1, 4, true)]
    [InlineData(8, 0xFE, true)]
    [InlineData(0, 0, false)]
    public Task An_observer_seat_or_a_malformed_seat_request_is_refused_8_without_the_room_owner(int seat, int state, bool wellFormed) =>
        Run(async world =>
        {
            Assert.False(await world.SendAsync(new GameSetPvpSeatStateCommand(Dispatch(world.A),
                new(GameMessageMetadata.Empty, (byte)seat, (byte)state, wellFormed))));
            Assert.Empty(world.Rooms.Calls);
            AssertRefused(world, world.A, ServerMessageType.GameSetPvpSeatState, 8);
        });

    [Fact]
    public Task A_two_byte_team_mode_is_refused_without_touching_the_room() => Run(async world =>
    {
        var request = PvpRoomPayloadCodec.DecodeTeamMode(GameMessageMetadata.Empty, [1, 0]);
        Assert.False(await world.SendAsync(new GameSetPvpTeamModeCommand(Dispatch(world.A), request)));
        Assert.Empty(world.Rooms.Calls);
        AssertRefused(world, world.A, ServerMessageType.GameSetPvpTeamMode, 8);
    });

    [Fact]
    public Task The_owner_leaving_passes_the_room_on_and_tells_the_lobby() => Run(async world =>
    {
        var before = PvpRooms.Room(1, PvpBattleMode.Team, 0, seats: [(0, PvpRooms.Taken(world.A)), (1, PvpRooms.Taken(world.B))]);
        var after = before with { Seats = before.Seats.SetItem(0, PvpSeat.OpenSeat), OwnerSeat = 1 };
        world.Rooms.Room = before;
        world.Rooms.Next = PvpRooms.Changed(PvpRoomChangeKind.Left, before, after, new PvpSeatRelease(0, world.A, PvpSeatReleaseReason.Left));

        Assert.True(await world.SendAsync(new GameSetPvpSeatStateCommand(Dispatch(world.A), new(GameMessageMetadata.Empty, 0, 0xFE, true))));

        foreach (var watcher in new[] { world.B, world.C })
            Assert.Collection(world.Projections.FramesFor(watcher),
                frame => Assert.Equal((world.A.WireUserId, PvpRoomProjector.InTownUserState),
                    (Assert.IsType<UserStateNotification>(frame).WireUserId, ((UserStateNotification)frame).UserState)),
                frame => Assert.Equal(PvpRoomSeatOccupancy.Open, Assert.Single(Assert.IsType<PvpSeatStateMessage>(frame).Seats).State.Occupancy),
                frame => Assert.Equal((byte)1, Assert.IsType<PvpRoomStateMessage>(frame).OwnerSeat));
        // The one who left hears the room's change only after its town return, and not its own state.
        var leaver = world.Projections.Projected
            .Where(x => x.Audience is NotificationAudience.TheseSessions these && these.Sessions.Contains(world.A)).ToArray();
        Assert.Equal([world.A], Assert.IsType<NotificationAudience.TheseSessions>(Assert.Single(leaver).Audience).Sessions);
        Assert.Same(leaver[0].Notification, world.Projections.Projected[^1].Notification);
        Assert.Collection(world.Projections.FramesFor(world.A),
            frame => Assert.Equal(0, Assert.Single(Assert.IsType<PvpSeatStateMessage>(frame).Seats).Seat),
            frame => Assert.Equal((byte)1, Assert.IsType<PvpRoomStateMessage>(frame).OwnerSeat));
        Assert.Empty(world.Outbox.Sent);
    });

    [Fact]
    public Task The_last_member_leaving_ends_the_room_for_the_lobby() => Run(async world =>
    {
        var before = PvpRooms.Room(1, PvpBattleMode.Team, 0, seats: [(0, PvpRooms.Taken(world.A))]);
        world.Rooms.Room = before;
        world.Rooms.Next = PvpRooms.Changed(PvpRoomChangeKind.Left, before, null, new PvpSeatRelease(0, world.A, PvpSeatReleaseReason.Left));

        Assert.True(await world.SendAsync(new GameSetPvpSeatStateCommand(Dispatch(world.A), new(GameMessageMetadata.Empty, 0, 0xFE, true))));

        Assert.Collection(world.Projections.FramesFor(world.C),
            frame => Assert.IsType<UserStateNotification>(frame),
            frame => Assert.Equal(0, Assert.Single(Assert.IsType<PvpSeatStateMessage>(frame).Seats).Seat),
            frame => Assert.Equal(PvpRoomStateMessage.Destroyed(1), frame));
        Assert.Collection(world.Projections.FramesFor(world.A),
            frame => Assert.Equal(0, Assert.Single(Assert.IsType<PvpSeatStateMessage>(frame).Seats).Seat),
            frame => Assert.Equal(PvpRoomStateMessage.Destroyed(1), frame));
    });

    [Fact]
    public Task A_member_getting_ready_tells_only_the_room() => Run(async world =>
    {
        var before = PvpRooms.Room(1, PvpBattleMode.Team, 0, seats: [(0, PvpRooms.Taken(world.A)), (1, PvpRooms.Taken(world.B))]);
        var after = before with { Seats = before.Seats.SetItem(1, PvpRooms.Taken(world.B, ready: true)) };
        world.Rooms.Next = PvpRooms.Changed(PvpRoomChangeKind.ReadyChanged, before, after);

        Assert.True(await world.SendAsync(new GameSetPvpReadyStateCommand(Dispatch(world.B), new(GameMessageMetadata.Empty, true, true))));

        foreach (var member in new[] { world.A, world.B })
            Assert.Equal(new PvpReadyStateMessage(1, true), Assert.Single(world.Projections.FramesFor(member)));
        Assert.Empty(world.Projections.FramesFor(world.C));
        Assert.Empty(world.Outbox.Sent);
    });

    [Fact]
    public Task The_owner_starting_sends_ready_start_and_state_to_the_fighters_and_the_state_to_the_lobby() => Run(async world =>
    {
        var before = PvpRooms.Room(1, PvpBattleMode.Individual, 0, map: 0,
            seats: [(0, PvpRooms.Taken(world.A, PvpTeam.Individual)), (1, PvpRooms.Taken(world.B, PvpTeam.Individual, true))]);
        var match = new PvpMatch(PvpMatchId.New(), [], 3, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        var after = before with
        {
            Seats = before.Seats.SetItem(0, PvpRooms.Taken(world.A, PvpTeam.Individual, true)),
            Phase = PvpRoomPhase.Loading,
            Match = match,
        };
        world.Rooms.Next = PvpRooms.Changed(PvpRoomChangeKind.MatchStarted, before, after);

        Assert.True(await world.SendAsync(new GameSetPvpReadyStateCommand(Dispatch(world.A), new(GameMessageMetadata.Empty, true, true))));

        foreach (var member in new[] { world.A, world.B })
            Assert.Equal<IGameServerMessage>(
                [new PvpReadyStateMessage(0, true), new StartPvpMessage(3, 1), new PvpRoomStateMessage(1, 2, 0, 0, 1)],
                world.Projections.FramesFor(member));
        Assert.Equal<IGameServerMessage>([new PvpRoomStateMessage(1, 2, 0, 0, 1)], world.Projections.FramesFor(world.C));
    });

    [Theory]
    [InlineData(PvpRoomRefusal.NotAllReady, 22)]
    [InlineData(PvpRoomRefusal.UnbalancedTeams, 14)]
    [InlineData(PvpRoomRefusal.TooFewMembers, 22)]
    [InlineData(PvpRoomRefusal.ShuttingDown, 19)]
    public Task A_refused_start_answers_on_the_ready_command(PvpRoomRefusal refusal, byte reason) => Run(async world =>
    {
        world.Rooms.Next = new PvpRoomOutcome.Refused(refusal);
        Assert.False(await world.SendAsync(new GameSetPvpReadyStateCommand(Dispatch(world.A), new(GameMessageMetadata.Empty, true, true))));
        AssertRefused(world, world.A, ServerMessageType.GameSetPvpReadyState, reason);
        Assert.Empty(world.Projections.Projected);
    });

    [Fact]
    public Task A_mode_change_sends_every_taken_seat_with_the_new_mode() => Run(async world =>
    {
        var before = PvpRooms.Room(1, PvpBattleMode.Team, 0,
            seats: [(0, PvpRooms.Taken(world.A, PvpTeam.First)), (1, PvpRooms.Taken(world.B, PvpTeam.Second))]);
        var after = before with
        {
            Mode = PvpBattleMode.Individual,
            Seats = before.Seats.SetItem(0, PvpRooms.Taken(world.A, PvpTeam.Individual)).SetItem(1, PvpRooms.Taken(world.B, PvpTeam.Individual)),
        };
        world.Rooms.Next = PvpRooms.Changed(PvpRoomChangeKind.ModeChanged, before, after);

        Assert.True(await world.SendAsync(new GameSetPvpTeamModeCommand(Dispatch(world.A), new(GameMessageMetadata.Empty, 1, true))));

        var seats = Assert.IsType<PvpSeatStateMessage>(Assert.Single(world.Projections.FramesFor(world.C)));
        Assert.Equal((byte)1, seats.Mode);
        Assert.Equal([0, 1], seats.Seats.Select(x => x.Seat));
        Assert.All(seats.Seats, x => Assert.Equal(0, x.State.Team));
    });

    [Fact]
    public Task A_map_change_has_no_reply_and_reaches_the_lobby_as_room_state() => Run(async world =>
    {
        var before = PvpRooms.Room(1, PvpBattleMode.Team, 0, seats: [(0, PvpRooms.Taken(world.A))]);
        world.Rooms.Next = PvpRooms.Changed(PvpRoomChangeKind.MapChanged, before, before with { MapIndex = 4 });

        Assert.True(await world.SendAsync(new GameSetPvpMapIndexCommand(Dispatch(world.A), new(GameMessageMetadata.Empty, 4, true))));

        Assert.Empty(world.Outbox.Sent);
        Assert.Equal((short)4, Assert.IsType<PvpRoomStateMessage>(Assert.Single(world.Projections.FramesFor(world.C))).MapIndex);
    });

    [Fact]
    public Task A_requester_outside_the_world_is_refused_without_the_room_owner() => Run(async world =>
    {
        var stranger = SelectedSessionIdentity.Create(new AccountId(99), new CharacterId(99), new NetworkConnectionId("stranger"), 1, PvpRooms.Port);
        Assert.False(await world.SendAsync(new GameSetPvpMapIndexCommand(Dispatch(stranger), new(GameMessageMetadata.Empty, 1, true))));
        Assert.Empty(world.Rooms.Calls);
        AssertRefused(world, stranger, ServerMessageType.GameSetPvpMapIndex, 19);
    });

    /// <summary>
    /// AC-003, projection side: a client that applies only the increments the lobby is sent holds, after every step,
    /// exactly the rooms a fresh 0x0029 would show.
    /// </summary>
    [Fact]
    public void Lobby_increments_add_up_to_the_snapshot_at_every_step()
    {
        var a = PvpRooms.Session(1);
        var b = PvpRooms.Session(2);
        var created = PvpRooms.Room(1, PvpBattleMode.Team, 0, seats: [(0, PvpRooms.Taken(a, PvpTeam.First))]);
        var entered = created with { Seats = created.Seats.SetItem(1, PvpRooms.Taken(b, PvpTeam.Second)) };
        var switched = entered with { Seats = entered.Seats.SetItem(1, PvpRooms.Taken(b, PvpTeam.First)) };
        var closed = switched with { Seats = switched.Seats.SetItem(5, PvpSeat.ClosedSeat) };
        var mapped = closed with { MapIndex = 2 };
        var individual = mapped with
        {
            Mode = PvpBattleMode.Individual,
            Seats = mapped.Seats.SetItem(0, PvpRooms.Taken(a, PvpTeam.Individual)).SetItem(1, PvpRooms.Taken(b, PvpTeam.Individual)),
        };
        var started = individual with { Phase = PvpRoomPhase.Loading };
        var waiting = started with { Phase = PvpRoomPhase.Waiting };
        var ownerLeft = waiting with { Seats = waiting.Seats.SetItem(0, PvpSeat.OpenSeat), OwnerSeat = 1 };
        PvpRoomSnapshot?[] steps = [null, created, entered, switched, closed, mapped, individual, started, waiting, ownerLeft, null];

        var model = new LobbyModel();
        for (var step = 1; step < steps.Length; step++)
        {
            foreach (var frame in PvpRoomLobbyFrames.For(steps[step - 1], steps[step]))
                model.Apply(frame);
            PvpRoomSnapshotState[] expected = steps[step] is { } now ? [PvpRoomLobbyView.Of(now)] : [];
            Assert.Equal(SupplementalSynchronizationCodec.EncodePvpRoomInfoBody(expected),
                SupplementalSynchronizationCodec.EncodePvpRoomInfoBody(model.Rooms));
        }
    }

    /// <summary>What a lobby client keeps of the rooms, built only from the frames it is sent.</summary>
    private sealed class LobbyModel
    {
        private readonly SortedDictionary<ushort, PvpRoomSnapshotState> rooms = [];

        public IReadOnlyList<PvpRoomSnapshotState> Rooms => [.. rooms.Values];

        public void Apply(IGameServerMessage frame)
        {
            switch (frame)
            {
                case PvpRoomInfoMessage info:
                    foreach (var room in info.Rooms) rooms[room.RoomNumber] = room;
                    break;
                case PvpSeatStateMessage seats:
                    var seated = rooms[seats.Number];
                    var list = seated.Seats.ToArray();
                    foreach (var (seat, state) in seats.Seats) list[seat] = state;
                    rooms[seats.Number] = seated with { Seats = list, BattleMode = seats.Mode };
                    break;
                case PvpRoomStateMessage { State: 0 } gone:
                    rooms.Remove(gone.Number);
                    break;
                case PvpRoomStateMessage state:
                    rooms[state.Number] = rooms[state.Number] with
                    {
                        Waiting = state.State == 1, OwnerSeat = state.OwnerSeat, MapIndex = state.MapIndex, BattleMode = state.Mode,
                    };
                    break;
                default:
                    throw new InvalidOperationException($"The lobby is sent no {frame.GetType().Name}.");
            }
        }
    }

    private static void AssertRefused(World world, SelectedSessionIdentity actor, ServerMessageType type, byte reason)
    {
        var (connection, message) = Assert.Single(world.Outbox.Sent);
        Assert.Equal(actor.ConnectionId, connection);
        var refusal = Assert.IsType<PvpRoomCommandRefusal>(message);
        Assert.Equal((type, reason), (refusal.MessageType, refusal.Reason));
    }

    internal static NetworkDispatchContext Dispatch(SelectedSessionIdentity session) =>
        new(new NetworkConnectionContext(session.ConnectionId, NetworkEndpointKind.Game, session.ListenerPort));

    private static Task Run(Func<World, Task> body) => TestGameplay.RunAsync(() => body(new World()));

    internal sealed class World
    {
        public World()
        {
            Sessions = TestSessions.Directory(out var state);
            A = Sessions.InWorld(state, new AccountId(1), new CharacterId(1), "a", PvpRooms.Port);
            B = Sessions.InWorld(state, new AccountId(2), new CharacterId(2), "b", PvpRooms.Port);
            C = Sessions.InWorld(state, new AccountId(3), new CharacterId(3), "c", PvpRooms.Port);
            foreach (var session in new[] { A, B, C }) Views.Publish(session, EntryFor(session));
        }

        public SelectedSessionDomainService Sessions { get; }
        public SelectedSessionIdentity A { get; }
        public SelectedSessionIdentity B { get; }
        public SelectedSessionIdentity C { get; }
        public ScriptedPvpRooms Rooms { get; } = new();
        public RecordingCommandOutbox Outbox { get; } = new();
        public RecordingProjections Projections { get; } = new();
        public FixedCurrentViews Views { get; } = new();

        public IPvpLobbyDomainService Lobby => new FixedPvpLobby(A, B, C);

        public IReadOnlyList<IGameServerMessage> Direct(SelectedSessionIdentity session) =>
            [.. Outbox.Sent.Where(x => x.Connection == session.ConnectionId).Select(x => x.Message)];

        /// <summary>When set, receives both the direct frames and the projections, in the order they are registered.</summary>
        public Timeline? Timeline { get; set; }

        public ITownAreaDomainService? Town { get; set; }

        public Dfo.Server.Protocol.Game.PvpBattle.PvpResultScreenDeferrals Deferrals { get; } = new();

        internal GamePvpRoomCommandHandler Handler() => new(Sessions, Rooms, Lobby, Views,
            DefaultAnswers.Create<IOnlineCharacterRuntimeDomainService>(), ProtocolAlgorithmFixture.NoOnline(),
            DefaultAnswers.Create<IGameSessionEndpointDomainService>(), Town ?? ProtocolAlgorithmFixture.Default<ITownAreaDomainService>(),
            ProtocolAlgorithmFixture.Default<IPartyService>(), ProtocolAlgorithmFixture.Default<IPartyProjectionStateDomainService>(),
            ProtocolAlgorithmFixture.Default<IWeaknessDomainService>(), ProtocolAlgorithmFixture.Default<ICommandWorkUnits>(),
            (Dfo.Server.Abstractions.DomainEvents.ICommandProjections?)Timeline ?? Projections, (IGameCommandOutbox?)Timeline ?? Outbox,
            Deferrals, NullLogger<GamePvpRoomCommandHandler>.Instance);

        public async Task<bool> SendAsync<TCommand>(TCommand command) where TCommand : class, IProtocolCommand<bool>
        {
            var handler = (ICommandHandler<TCommand, bool>)(object)Handler();
            var request = new CommandRequestContext.ProtocolNonEffect(command.Dispatch, command.Message, command.Frame, () => true);
            return await handler.HandleAsync(new(Guid.NewGuid(), typeof(TCommand), typeof(GamePvpRoomCommandHandler)) { Request = request },
                command, default);
        }
    }

    internal static SelectCharacterResult EntryFor(SelectedSessionIdentity session)
    {
        var entry = SelectCharacterPayloadTestData.CreateComplete();
        return entry with
        {
            Character = entry.Character with
            {
                Identity = entry.Character.Identity with { CharacterId = session.CharacterId, Name = [(byte)session.WireUserId] },
            },
        };
    }
}
