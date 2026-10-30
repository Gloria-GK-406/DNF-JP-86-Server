using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.World;

public sealed class SealedTreasureRunTests
{
    private static readonly CharacterId Character = new(1);
    private static readonly MazeCell Center = new(2, 2);

    [Theory]
    [InlineData(1, 2, 110348, 54291)]
    [InlineData(2, 3, 110349, 54292)]
    [InlineData(3, 2, 110350, 54293)]
    [InlineData(2, 1, 110351, 54294)]
    public async Task Only_committed_matching_element_unlocks_its_route_and_new_run_resets(
        byte x, byte y, int monster, int stone)
    {
        await TestGameplay.RunAsync(async () =>
        {
            var scope = TestRequired.TransactionScope();
            var runs = TestParties.Runs(scope);
            var road = new MazeCell(x, y);
            var layout = Layout(3557, road);
            var seed = DungeonCellSeed.CompleteEmpty(road, 10) with {
                Actors = [new(1, new(monster, 73, 0), false, false, [])],
                Objects = [new(new(new(DungeonObjectSection.Passive, 0), stone, 10, 10, 0), [])]
            };
            var run = Start(runs, layout, seed);
            Assert.Null(runs.TryMove(Character, run.RunId, run.RoomVersion, Center,
                DungeonCellSeed.CompleteEmpty(Center, 11)));
            await using (var tx = scope.Begin())
                Assert.Null(runs.ClaimObjectDestruction(Character, run.RunId, run.RoomVersion, stone));
            var death = runs.TryClaimActorDeath(Character, 1)!;
            Assert.True(runs.ReleaseActorDeath(death));
            Assert.Null(runs.TryMove(Character, run.RunId, run.RoomVersion, Center,
                DungeonCellSeed.CompleteEmpty(Center, 11)));
            Assert.NotNull(runs.CommitActorDeath(runs.TryClaimActorDeath(Character, 1)!));
            await using (var tx = scope.Begin())
            {
                Assert.NotNull(runs.ClaimObjectDestruction(Character, run.RunId, run.RoomVersion, stone));
                await tx.CommitAsync(default);
            }
            Assert.NotNull(runs.TryMove(Character, run.RunId, run.RoomVersion, Center,
                DungeonCellSeed.CompleteEmpty(Center, 11)));
            foreach (var other in new[] { new MazeCell(1, 2), new(2, 3), new(3, 2), new(2, 1) }.Where(p => p != road))
            {
                var atCenter = runs.Find(Character)!;
                Assert.Null(runs.TryMove(Character, atCenter.RunId, atCenter.RoomVersion, other,
                    DungeonCellSeed.CompleteEmpty(other, 10)));
            }
            var back = runs.TryMove(Character, road)!;
            Assert.Single(back.Kills);
            Assert.NotNull(runs.TryMove(Character, Center));
            var fresh = Start(runs, layout, seed);
            Assert.Empty(fresh.Kills);
            Assert.Null(runs.TryMove(Character, fresh.RunId, fresh.RoomVersion, Center,
                DungeonCellSeed.CompleteEmpty(Center, 11)));
        });
    }

    [Fact]
    public async Task Chest_ground_reward_requires_committed_destruction_and_remains_single_use()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var scope = TestRequired.TransactionScope();
            var runs = TestParties.Runs(scope);
            var seed = DungeonCellSeed.CompleteEmpty(Center, 10) with {
                Objects = [new(new(new(DungeonObjectSection.SpecialPassive, 0), 15150, 10, 10, 0), [])],
                GroundItems = [new(1, 10003717, 1, 0, false, DungeonGroundItemSource.Declared(0))]
            };
            var run = Start(runs, Layout(3557, Center), seed);
            var number = Assert.Single(run.CurrentCellState.GroundItems).Number;
            Assert.Null(runs.TryClaimGroundDrop(Character, run.RunId, run.RoomVersion, number));
            await using (var tx = scope.Begin())
                Assert.NotNull(runs.ClaimObjectDestruction(Character, run.RunId, run.RoomVersion, 15150));
            Assert.Null(runs.TryClaimGroundDrop(Character, run.RunId, run.RoomVersion, number));
            await using (var tx = scope.Begin())
            {
                Assert.NotNull(runs.ClaimObjectDestruction(Character, run.RunId, run.RoomVersion, 15150));
                await tx.CommitAsync(default);
            }
            var claim = runs.TryClaimGroundDrop(Character, run.RunId, run.RoomVersion, number);
            Assert.NotNull(claim);
            Assert.NotNull(runs.CommitGroundDrop(claim, DungeonPickupKind.Item));
            Assert.Null(runs.TryClaimGroundDrop(Character, run.RunId, run.RoomVersion, number));
            await using var retry = scope.Begin();
            Assert.Null(runs.ClaimObjectDestruction(Character, run.RunId, run.RoomVersion, 15150));
        });
    }

    private static DungeonLayout Layout(int dungeon, MazeCell start) => new(dungeon, 0, 5, 5,
        start, new(4, 4), 10, 123, new Dictionary<MazeCell, DungeonCellDefinition> {
            [new(1, 2)] = DungeonCellDefinition.Fixed(10), [new(2, 3)] = DungeonCellDefinition.Fixed(10),
            [new(3, 2)] = DungeonCellDefinition.Fixed(10), [new(2, 1)] = DungeonCellDefinition.Fixed(10),
            [start] = DungeonCellDefinition.Fixed(10), [Center] = DungeonCellDefinition.Fixed(11)
        }) { ConsumesFatigue = false };
    private static DungeonRunState Start(DungeonRunDomainService runs, DungeonLayout layout, DungeonCellSeed seed) =>
        runs.PreparePartyEntry([new(new(1), Character, 0, true)], layout, DungeonDifficulty.Tier2,
            seed, DungeonRunId.New(), DateTimeOffset.UtcNow).Run;
}
