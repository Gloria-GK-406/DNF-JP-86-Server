using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.World;

public sealed class DungeonObjectQueueTests
{
    private static readonly CharacterId Character = new(1);
    private static readonly DungeonLayout Layout = new(84, 0, 3, 1, new(0, 0), new(2, 0), 10, 123,
        new Dictionary<MazeCell, DungeonCellDefinition> { [new(0, 0)] = DungeonCellDefinition.Fixed(10), [new(1, 0)] = DungeonCellDefinition.Fixed(11), [new(2, 0)] = DungeonCellDefinition.Fixed(12)});

    [Fact]
    public async Task Empty_entries_count_and_a_type_cannot_skip_its_reserved_head()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var scope = TestRequired.TransactionScope();
            var service = TestParties.Runs(scope);
            var run = Start(service);
            await using (var tx = scope.Begin())
            {
                var first = service.ClaimObjectDestruction(Character, run.RunId, run.RoomVersion, 8961)!;
                Assert.Equal(0, first.QueueIndex);
                Assert.Empty(first.PendingLoot);
                Assert.Null(service.ClaimObjectDestruction(Character, run.RunId, run.RoomVersion, 8961));
                Assert.Null(service.ClaimObjectDestruction(Character, run.RunId, run.RoomVersion, 9999));
                Assert.False(service.Find(Character)!.CurrentCellState.Objects[0].IsConsumed);
                await tx.CommitAsync(default);
            }
            await using (var tx = scope.Begin())
            {
                var second = service.ClaimObjectDestruction(Character, run.RunId, run.RoomVersion, 8961)!;
                Assert.Equal(1, second.QueueIndex);
                Assert.Equal(10149153, Assert.Single(second.PendingLoot).ItemId);
                await tx.CommitAsync(default);
            }
            await using (var tx = scope.Begin())
            {
                Assert.Null(service.ClaimObjectDestruction(Character, run.RunId, run.RoomVersion, 8961));
                Assert.NotNull(service.ClaimObjectDestruction(Character, run.RunId, run.RoomVersion, 777));
                await tx.CommitAsync(default);
            }
            Assert.All(service.Find(Character)!.CurrentCellState.Objects, x => Assert.True(x.IsConsumed));
            Assert.Empty(service.Find(Character)!.Kills);
            Assert.Empty(service.Find(Character)!.CurrentCellState.GroundItems);
        });
    }

    [Fact]
    public async Task Durable_failure_releases_the_same_head_and_revisit_does_not_refill_it()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var scope = TestRequired.TransactionScope();
            var service = TestParties.Runs(scope);
            var run = Start(service);
            await using (var tx = scope.Begin())
            {
                Assert.NotNull(service.ClaimObjectDestruction(Character, run.RunId, run.RoomVersion, 8961));
                scope.Current!.Enlist("failure", () => new Failure());
                await Assert.ThrowsAsync<InvalidOperationException>(async () => await tx.CommitAsync(default));
            }
            await using (var tx = scope.Begin())
            {
                Assert.Equal(0, service.ClaimObjectDestruction(Character, run.RunId, run.RoomVersion, 8961)!.QueueIndex);
                await tx.CommitAsync(default);
            }
            var next = service.TryMove(Character, run.RunId, run.RoomVersion, new(1, 0),
                DungeonCellSeed.CompleteEmpty(new(1, 0), 11))!;
            var back = service.TryMove(Character, new(0, 0))!;
            Assert.NotEqual(run.RoomVersion, back.RoomVersion);
            await using var again = scope.Begin();
            Assert.Null(service.ClaimObjectDestruction(Character, run.RunId, run.RoomVersion, 8961));
            Assert.Equal(1, service.ClaimObjectDestruction(Character, back.RunId, back.RoomVersion, 8961)!.QueueIndex);
            Assert.NotNull(next);
        });
    }

    private static DungeonRunState Start(IDungeonRunDomainService service)
    {
        DungeonCellObjectState[] objects = [
            new(new(new(DungeonObjectSection.SpecialPassive, 0), 8961, 10, 10, 0), []),
            new(new(new(DungeonObjectSection.SpecialPassive, 1), 8961, 20, 10, 0),
                [new(10149153, 1, DropRarity.Common, DropItemKind.Stackable)]),
            new(new(new(DungeonObjectSection.Passive, 0), 777, 30, 10, 0), [])];
        var seed = DungeonCellSeed.CompleteEmpty(new(0, 0), 10) with { Objects = objects };
        return service.PreparePartyEntry([new(new(1), Character, 0, true)], Layout, DungeonDifficulty.Normal,
            seed, DungeonRunId.New(), DateTimeOffset.UtcNow).Run;
    }

    private sealed class Failure : ITransactionParticipant
    {
        public bool CommitCanFail => true;
        public ValueTask PrepareAsync(CancellationToken ct) => ValueTask.CompletedTask;
        public ValueTask CommitAsync(CancellationToken ct) => throw new InvalidOperationException("durable failure");
        public ValueTask RollbackAsync(CancellationToken ct) => ValueTask.CompletedTask;
    }
}
