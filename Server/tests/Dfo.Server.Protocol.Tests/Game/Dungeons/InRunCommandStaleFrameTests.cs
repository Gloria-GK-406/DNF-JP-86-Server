using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.CharacterDeath;
using Dfo.Server.Protocol.Game.DungeonEntry;
using Dfo.Server.Protocol.Game.FinishLoading;
using Dfo.Server.Protocol.Game.GroundPickup;
using Dfo.Server.Protocol.Game.MonsterDeath;
using Dfo.Server.Testing;

namespace Dfo.Server.Protocol.Tests.Game.Dungeons;

/// <summary>
/// A stale in-run request (pickup, character death, monster death and object destruction,
/// finish loading, move map) is dropped wherever its requester stands: nothing is written, no
/// tower is asked and nothing is registered.
/// </summary>
/// <remarks>
/// Object destruction in an ordinary run, an illusion tower or town used to be processed or
/// acknowledged empty; dropping it there, and dropping every stale request without a session,
/// is the change the specification authorizes.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class InRunCommandStaleFrameTests
{
    // 0x0027 carrying an object code: an object destruction report rather than a kill.
    private const int ObjectDestruction = 0x10027;

    public static TheoryData<int, RoutingPlace> StaleFrames
    {
        get
        {
            var data = new TheoryData<int, RoutingPlace>();
            foreach (var type in new[] { 0x002B, 0x0028, 0x0027, ObjectDestruction, 0x0025, 0x002D })
                foreach (var place in Enum.GetValues<RoutingPlace>())
                    data.Add(type, place);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(StaleFrames))]
    public async Task A_stale_in_run_request_is_dropped_wherever_the_requester_stands(int type, RoutingPlace place)
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
            _ => throw new ArgumentOutOfRangeException(nameof(type)),
        };
    }

    /// <summary>What a dropped request must leave as it was: the requester's ordinary run and both towers' runs.</summary>
    private static string Snapshot(DungeonOwnerRoutingWorld world, NetworkConnectionId connection)
    {
        if (world.Sessions.FindByConnection(connection) is not { } session) return "unpublished";
        var run = world.Runs.Find(session.CharacterId);
        return string.Join('|',
            run is null ? "run=none" : $"run={run.RunId.Value}:{run.RoomVersion}:{run.DeadParticipants.Count}:{run.ReadyParticipants.Count}",
            $"boss={world.Boss.Find(session)?.RunId}", $"illusion={world.Illusion.Find(session)?.RunId}");
    }
}
