using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.DungeonSelection;
using Dfo.Server.Testing;

namespace Dfo.Server.Protocol.Tests.Game.Dungeons;

/// <summary>
/// Enter selection hands a Boss tower request over to the ordinary selection only after the tower
/// released the run; a request the Boss channel refuses before asking the tower ends there.
/// </summary>
[Trait("Category", "Unit")]
public sealed class EnterSelectionBossHandOverTests
{
    [Fact]
    public async Task A_boss_tower_member_who_is_not_the_leader_ends_the_selection_with_the_tower()
    {
        await TestGameplay.RunAsync(async () =>
        {
            // The tower would release on request, so only the leader check keeps the member seated.
            var world = new DungeonOwnerRoutingWorld { Boss = { ReleasesForSelection = true } };
            var leader = world.Join();
            var member = world.Join();
            var run = world.Boss.Find(world.Boss.Enter(leader, member))!.RunId;

            await world.SendAsync(member, new EnterSelectDungeonRequest(GameMessageMetadata.Empty, 3505));

            world.AssertUntouched();
            Assert.Equal(run, world.Boss.Find(member)?.RunId);
        });
    }
}
