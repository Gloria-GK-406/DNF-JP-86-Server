using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Application.World;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.World;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.World;

public sealed class AltarCellGateTests
{
    [Fact]
    public async Task Dynamic_batch_and_clear_gate_publish_only_at_commit_and_reject_stale_or_reused_numbers()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var scope = TestRequired.TransactionScope();
            var runs = TestParties.Runs(scope);
            var cell = new MazeCell(0, 0);
            var layout = new DungeonLayout(11007, 0, 1, 1, cell, cell, 17030, 1,
                new Dictionary<MazeCell, DungeonCellDefinition> { [cell] = DungeonCellDefinition.Fixed(17030) });
            var run = runs.PreparePartyEntry([new(new(1), new(2), 0, true)], layout, DungeonDifficulty.Normal,
                DungeonCellSeed.CompleteEmpty(cell, 17030) with { MechanismBlocksClear = true }, DungeonRunId.New(), DateTimeOffset.UnixEpoch).Run;
            DungeonCellActorState[] actors = [new(7, new(100, 70, 0), false, false, [])];
            await using (var tx = scope.Begin())
            {
                Assert.True(runs.StageDynamicCell(run.RunId, run.RoomVersion, run.CurrentCellState.Revision, actors, true));
                Assert.Empty(runs.Find(run.RunId)!.CurrentCellState.Actors);
                // Rollback leaves neither actors nor a new revision behind.
            }
            Assert.Empty(runs.Find(run.RunId)!.CurrentCellState.Actors);
            await using (var tx = scope.Begin())
            {
                Assert.True(runs.StageDynamicCell(run.RunId, run.RoomVersion, run.CurrentCellState.Revision, actors, true));
                await tx.CommitAsync(default);
            }
            Assert.NotNull(runs.Find(run.RunId)!.CurrentCellState.FindActor(7));
            await using (var tx = scope.Begin())
            {
                var current = runs.Find(run.RunId)!;
                Assert.False(runs.StageDynamicCell(run.RunId, current.RoomVersion - 1, current.CurrentCellState.Revision, [], false));
                Assert.False(runs.StageDynamicCell(run.RunId, current.RoomVersion, current.CurrentCellState.Revision, actors, true));
                Assert.False(runs.StageDynamicCell(run.RunId, current.RoomVersion, current.CurrentCellState.Revision - 1, [], false));
            }
            var death = runs.TryClaimActorDeath(new(2), 7)!;
            runs.CommitActorDeathChange(death);
            Assert.False(runs.Find(run.RunId)!.IsCleared);
            await using (var tx = scope.Begin())
            {
                var current = runs.Find(run.RunId)!;
                Assert.True(runs.StageDynamicCell(run.RunId, current.RoomVersion, current.CurrentCellState.Revision, [], false));
                Assert.False(runs.Find(run.RunId)!.IsCleared);
                await tx.CommitAsync(default);
            }
            Assert.True(runs.Find(run.RunId)!.IsCleared);
        });
    }
    [Fact]
    public void Pending_mechanism_blocks_clear_and_boss_death_never_suppresses_an_altar_wave()
    {
        TestGameplay.Run(() =>
        {
            var runs = TestParties.Runs();
            var cell = new MazeCell(0, 0);
            var layout = new DungeonLayout(11007, 0, 1, 1, cell, cell, 17030, 1,
                new Dictionary<MazeCell, DungeonCellDefinition> { [cell] = DungeonCellDefinition.Fixed(17030) })
                { Mode = new DungeonRunMode.Altar(new(AltarDungeonKind.Ultimate, new(2026, 10, 13))), ConsumesFatigue = false };
            var run = runs.PreparePartyEntry([new(new(1), new(2), 0, true)], layout, DungeonDifficulty.Normal,
                new(cell, 17030, 1, [new(1, new(100, 70, 3), false, false, []), new(2, new(101, 70, 0), false, false, [])], [])
                    { MechanismBlocksClear = true }, DungeonRunId.New(), DateTimeOffset.UnixEpoch).Run;
            var boss = runs.TryClaimActorDeath(new(2), 1)!;
            Assert.False(runs.WillClearMap(boss));
            var change = runs.CommitActorDeathChange(boss)!;
            Assert.Empty(change.AutoKilledActorSequences);
            Assert.False(change.Run.CurrentCellState.FindActor(2)!.IsDead);
            Assert.False(change.Run.IsCleared);
            var last = runs.TryClaimActorDeath(new(2), 2)!;
            Assert.False(runs.WillClearMap(last));
            Assert.False(runs.CommitActorDeathChange(last)!.Run.IsCleared);
            Assert.Equal(run.RunId, runs.Find(new Dfo.Server.Data.Characters.CharacterId(2))!.RunId);
        });
    }
}
