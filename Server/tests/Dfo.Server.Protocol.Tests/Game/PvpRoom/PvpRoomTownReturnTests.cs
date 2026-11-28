using Dfo.Server.Abstractions.Pvp;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Sessions;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.DungeonSelection;
using Dfo.Server.Protocol.Game.PvpRoom;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Dfo.Server.Protocol.Game.TownProjection;
using Dfo.Server.Protocol.Tests.Game.TownProjection;
using Dfo.Server.Testing;

namespace Dfo.Server.Protocol.Tests.Game.PvpRoom;

/// <summary>
/// A member sent back to town from a duel room: upstream <c>PublishTownReturnAsync</c> gives it USER_AREA and the
/// area roster without its own USER_STATE, and only then the room's change; the lobby hears its state 0.
/// </summary>
public sealed class PvpRoomTownReturnTests
{
    private static readonly TownAreaKey Area = new(11, 3, 6);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task Leaving_returns_to_town_without_its_own_state_and_then_hears_the_room(bool ownerLeaves) => TestGameplay.RunAsync(async () =>
    {
        var world = new PvpRoomCommandTests.World();
        var areas = new SessionPresenceDomainService();
        var leaver = ownerLeaves ? world.A : world.B;
        world.Views.Publish(leaver, InTown(leaver));
        ((ITownAreaDomainService)areas).Enter(new TownAreaMember(leaver, Area, new CharacterPositionState(3, 6, 0, 200, 1), true));
        ((ITownAreaDomainService)areas).LeaveExact(leaver, TownAreaDepartureReason.PvpRoom);
        var transit = ProtocolAlgorithmFixture.TownTransit(null, areas, world.Sessions, SharedTownMovementDomainService.Instance);
        var admitted = await transit.ReturnToTownAsync(
            [new TownReturnRequest(leaver, Area.ChannelId, new CharacterPositionState(3, 6, 100, 200, 1))], CancellationToken.None);

        var before = PvpRooms.Room(1, PvpBattleMode.Team, 0, seats: [(0, PvpRooms.Taken(world.A)), (1, PvpRooms.Taken(world.B))]);
        var seat = ownerLeaves ? 0 : 1;
        var after = before with { Seats = before.Seats.SetItem(seat, PvpSeat.OpenSeat), OwnerSeat = ownerLeaves ? 1 : 0 };
        world.Rooms.Room = before;
        world.Rooms.Next = new PvpRoomOutcome.Changed(PvpRoomChangeKind.Left, before, after,
            [new PvpSeatRelease(seat, leaver, PvpSeatReleaseReason.Left)], new ReturnedTownMoves(admitted));
        world.Town = areas;
        world.Timeline = new Timeline();

        Assert.True(await world.SendAsync(new GameSetPvpSeatStateCommand(PvpRoomCommandTests.Dispatch(leaver),
            new(GameMessageMetadata.Empty, (byte)seat, 0xFE, true))));

        var frames = world.Timeline.For(leaver);
        Assert.DoesNotContain(frames, frame => frame is UserStateNotification);
        var expected = new List<Action<IGameServerMessage>>
        {
            frame => Assert.Equal(3, Assert.IsType<UserAreaNotification>(frame).Spawn.TownId),
            frame => Assert.IsType<AreaUsersNotification>(frame),
            frame => Assert.Equal(seat, Assert.Single(Assert.IsType<PvpSeatStateMessage>(frame).Seats).Seat),
        };
        if (ownerLeaves) expected.Add(frame => Assert.Equal((byte)1, Assert.IsType<PvpRoomStateMessage>(frame).OwnerSeat));
        Assert.Collection(frames, [.. expected]);
        // The lobby, the leaver excluded, hears it is back in town before the room's change.
        Assert.Equal((leaver.WireUserId, (byte)0),
            (Assert.IsType<UserStateNotification>(world.Timeline.For(world.C)[0]).WireUserId, ((UserStateNotification)world.Timeline.For(world.C)[0]).UserState));
    });

    /// <summary>The dungeon return keeps its own USER_STATE 0 first: the option is the duel room's alone.</summary>
    [Fact]
    public void Only_the_duel_room_return_drops_the_own_state()
    {
        var session = PvpRooms.Session(1);
        var view = InTown(session);
        var position = new CharacterPositionState(3, 6, 100, 200, 1);
        var dungeon = TownReturnProjection.Frames(session, view, position);
        var duel = TownReturnProjection.Frames(session, view, position, ownState: false);
        Assert.IsType<UserStateNotification>(dungeon[0]);
        Assert.Equal(dungeon.Skip(1).Select(x => x.GetType()), duel.Select(x => x.GetType()));
        Assert.IsType<UserAreaNotification>(duel[0]);
    }

    private static SelectCharacterResult InTown(SelectedSessionIdentity session)
    {
        var result = PvpRoomCommandTests.EntryFor(session);
        return result with
        {
            World = result.World with
            {
                Channel = result.World.Channel with
                {
                    ChannelId = Area.ChannelId,
                    Spawn = result.World.Channel.Spawn with
                    {
                        TownId = 3, AreaId = 6, X = 100, Y = 200, Direction = 1, SharesTownPresence = true,
                    },
                },
            },
        };
    }
}
