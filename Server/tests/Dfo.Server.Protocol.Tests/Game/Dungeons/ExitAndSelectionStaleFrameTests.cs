using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.CharacterDeath;
using Dfo.Server.Protocol.Game.DungeonClear;
using Dfo.Server.Protocol.Game.DungeonSelection;
using Dfo.Server.Testing;

namespace Dfo.Server.Protocol.Tests.Game.Dungeons;

/// <summary>
/// A stale leave or selection request (EPLP, back to village, enter selection, revival coin) is
/// dropped wherever its requester stands: nothing is written, no tower is asked, no current view
/// is read and nothing is registered.
/// </summary>
/// <remarks>
/// Enter selection in an ordinary run, an illusion tower or town used to open the ordinary
/// selection; a revival coin used to be refused in a Boss tower and handled by the ordinary flow
/// elsewhere; and every one of the four used to reach its ordinary flow without a session.
/// Dropping them there is the change the specification authorizes.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class ExitAndSelectionStaleFrameTests
{
    public static TheoryData<int, RoutingPlace> StaleFrames
    {
        get
        {
            var data = new TheoryData<int, RoutingPlace>();
            foreach (var type in new[] { 0x0048, 0x0084, 0x000F, 0x0029 })
                foreach (var place in Enum.GetValues<RoutingPlace>())
                    data.Add(type, place);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(StaleFrames))]
    public async Task A_stale_leave_or_selection_request_is_dropped_wherever_the_requester_stands(int type, RoutingPlace place)
    {
        await TestGameplay.RunAsync(async () =>
        {
            // A released Boss run would let a request that slipped past the drop open the selection.
            var world = new DungeonOwnerRoutingWorld { Boss = { ReleasesForSelection = true } };
            var connection = world.PlaceConnection(place);
            var before = Snapshot(world, connection);

            await world.SendAsync(connection, StaleRequest(type));

            world.AssertUntouched();
            Assert.Empty(world.Projections.Projected);
            Assert.Equal(before, Snapshot(world, connection));
        });
    }

    private static IClientNetworkMessage StaleRequest(int type)
    {
        var metadata = DungeonOwnerRoutingWorld.Stale((ushort)type);
        return type switch
        {
            0x0048 => new EplpCommandRequest(metadata, EplpCommandHandler.LeaveState, 0),
            0x0084 => new BackToVillageRequest(metadata),
            0x000F => new EnterSelectDungeonRequest(metadata, 3505),
            0x0029 => new UseRevivalCoinRequest(metadata, 2, 1),
            _ => throw new ArgumentOutOfRangeException(nameof(type)),
        };
    }

    /// <summary>What a dropped request must leave as it was: the requester's party and ordinary run, and both towers' runs.</summary>
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
