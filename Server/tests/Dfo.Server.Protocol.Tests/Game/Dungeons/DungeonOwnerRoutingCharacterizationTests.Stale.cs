using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.CharacterDeath;
using Dfo.Server.Protocol.Game.DungeonClear;
using Dfo.Server.Protocol.Game.DungeonEntry;
using Dfo.Server.Protocol.Game.DungeonSelection;
using Dfo.Server.Protocol.Game.FinishLoading;
using Dfo.Server.Protocol.Game.GroundPickup;
using Dfo.Server.Protocol.Game.MonsterDeath;
using Dfo.Server.Testing;

namespace Dfo.Server.Protocol.Tests.Game.Dungeons;

// A frame whose inbound ordinal did not advance the connection's counter is already dropped in
// these places: nothing is written, no tower is asked and nothing is registered. Places where a
// stale frame is still processed today are not pinned here.
public sealed partial class DungeonOwnerRoutingCharacterizationTests
{
    public static TheoryData<int, RoutingPlace> DroppedStaleFrames
    {
        get
        {
            var data = new TheoryData<int, RoutingPlace>();
            RoutingPlace[] seated = [RoutingPlace.Ordinary, RoutingPlace.Boss, RoutingPlace.Illusion, RoutingPlace.Town];
            foreach (var type in new[] { 0x002B, 0x0028, 0x0027, 0x0025, 0x002D, 0x0048, 0x0084, 0x002A })
                foreach (var place in seated)
                    data.Add(type, place);
            foreach (var type in new[] { 0x002B, 0x0025, 0x002A })
                data.Add(type, RoutingPlace.NoSession);
            data.Add(ObjectDestruction, RoutingPlace.Boss);
            data.Add(0x000F, RoutingPlace.Boss);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(DroppedStaleFrames))]
    public async Task A_stale_frame_is_dropped_where_it_is_dropped_today(int type, RoutingPlace place)
    {
        await TestGameplay.RunAsync(async () =>
        {
            var world = new DungeonOwnerRoutingWorld();
            var connection = world.PlaceConnection(place);
            var before = Snapshot(world, connection);

            await world.SendAsync(connection, StaleRequest(type));

            world.AssertUntouched();
            Assert.Equal(before, Snapshot(world, connection));
        });
    }

    public static TheoryData<int, RoutingPlace> DroppedStalePartyFrames
    {
        get
        {
            var data = new TheoryData<int, RoutingPlace>();
            foreach (var type in new[] { 0x000D, 0x000E, 0x0079 })
                foreach (var place in new[] { RoutingPlace.Ordinary, RoutingPlace.Boss, RoutingPlace.Illusion, RoutingPlace.Town, RoutingPlace.NoSession })
                    data.Add(type, place);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(DroppedStalePartyFrames))]
    public async Task A_stale_party_mutation_is_dropped_wherever_the_party_stands(int type, RoutingPlace place)
    {
        await TestGameplay.RunAsync(async () =>
        {
            var world = new DungeonOwnerRoutingWorld();
            var seat = place == RoutingPlace.NoSession ? RoutingPlace.Town : place;
            var (leader, member) = Party(world, seat, seat);
            var before = (Snapshot(world, leader.ConnectionId), Snapshot(world, member.ConnectionId));

            await world.SendAsync(place == RoutingPlace.NoSession ? DungeonOwnerRoutingWorld.Unpublished : leader.ConnectionId,
                PartyMutation(type, DungeonOwnerRoutingWorld.Stale((ushort)type)));

            world.AssertUntouched();
            Assert.Equal(before, (Snapshot(world, leader.ConnectionId), Snapshot(world, member.ConnectionId)));
            Assert.Empty(world.Projections.Projected);
        });
    }

    private static IClientNetworkMessage StaleRequest(int type)
    {
        var metadata = DungeonOwnerRoutingWorld.Stale((ushort)(type & 0xFFFF));
        return type switch
        {
            0x002B => new GetItemRequest(metadata, 3),
            0x0028 => new CharacterDeathRequest(metadata, 612, 294, 0),
            0x0027 => new MonsterDeathRequest(metadata, 7, 1),
            ObjectDestruction => new MonsterDeathRequest(metadata, 7, 1) { ObjectCode = DungeonOwnerRoutingWorld.ObjectCode },
            0x0025 => new FinishLoadingRequest(metadata, 0, 0),
            0x002D => new MoveMapRequest(metadata, 1, 0, 0, 0),
            0x0048 => new EplpCommandRequest(metadata, EplpCommandHandler.LeaveState, 0),
            0x0084 => new BackToVillageRequest(metadata),
            0x000F => new EnterSelectDungeonRequest(metadata, 3505),
            0x002A => new GiveUpGameRequest(metadata),
            _ => throw new ArgumentOutOfRangeException(nameof(type)),
        };
    }

    /// <summary>What a dropped frame must leave as it was: the requester's party and run, and both towers' runs.</summary>
    private static string Snapshot(DungeonOwnerRoutingWorld world, NetworkConnectionId connection)
    {
        if (world.Sessions.FindByConnection(connection) is not { } session) return "unpublished";
        var party = world.PartyOf(session);
        var run = world.Runs.Find(session.CharacterId);
        return string.Join('|',
            $"party={party.LeaderWireUserId}:{string.Join(',', party.Members.Select(member => member.CharacterId.Value))}",
            run is null ? "run=none" : $"run={run.RunId.Value}:{run.RoomVersion}:{run.DeadParticipants.Count}:{run.ReadyParticipants.Count}",
            $"boss={world.Boss.Find(session)?.RunId}", $"illusion={world.Illusion.Find(session)?.RunId}");
    }
}
