using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.World;

/// <summary>
/// Maze <c>[clear condition]</c> entries open the clear on their own (#356; 70
/// <c>IsClearDungeon</c> @ 0x0822D08C), and <c>[ignore default dungeon clear]</c> turns the boss
/// room default off.
/// </summary>
[Trait("Category", "Unit")]
public sealed class DungeonClearConditionTests
{
    private static readonly AccountId Account = new(1);
    private static readonly CharacterId Character = new(1);
    private static readonly MazeCell Start = new(0, 0);
    private static readonly MazeCell Boss = new(1, 0);

    [Fact]
    public async Task A_destroyed_object_condition_opens_the_clear_once_and_then_holds_the_party_in_place()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var scope = TestRequired.TransactionScope();
            var runs = TestParties.Runs(scope);
            var layout = Layout([new(DungeonClearConditionKind.DestroyObject, [10601], 1)]);
            var run = Enter(runs, layout, Start, objects: [Object(10601)]);
            var service = new DungeonClearDomainService(runs, new RoomCleared(true));

            // The room is empty and cleared, but with conditions declared that is not a clear.
            Assert.Null(await service.TryOpenClearAsync(Account, Character, run.RunId, run.RoomVersion, default));
            await using (var tx = scope.Begin())
            {
                Assert.NotNull(runs.ClaimObjectDestruction(Character, run.RunId, run.RoomVersion, 10601));
                await tx.CommitAsync(default);
            }
            Assert.Equal(10601, Assert.Single(runs.Find(Character)!.DestroyedObjectCodes));

            Assert.NotNull(await service.TryOpenClearAsync(Account, Character, run.RunId, run.RoomVersion, default));
            Assert.Null(await service.TryOpenClearAsync(Account, Character, run.RunId, run.RoomVersion, default));
            Assert.Null(runs.TryMove(Character, run.RunId, run.RoomVersion, Boss,
                DungeonCellSeed.CompleteEmpty(Boss, 11)));
        });
    }

    [Fact]
    public async Task A_cleared_boss_room_does_not_clear_a_maze_whose_conditions_are_unmet()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var runs = TestParties.Runs();
            var layout = Layout([new(DungeonClearConditionKind.DestroyObject, [10601], 1)]);
            var run = Enter(runs, layout, Boss);
            var service = new DungeonClearDomainService(runs, new RoomCleared(true));
            Assert.True(runs.Find(Character)!.IsBossRoom);
            Assert.Null(await service.TryOpenClearAsync(Account, Character, run.RunId, run.RoomVersion, default));
        });
    }

    [Fact]
    public async Task A_hunt_boss_condition_counts_kills_of_its_monster_code()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var runs = TestParties.Runs();
            var layout = Layout([new(DungeonClearConditionKind.HuntBoss, [69257], 1)]);
            var run = Enter(runs, layout, Start, actors: [Monster(100, 61000, 0), Monster(101, 69257, 3)]);
            var service = new DungeonClearDomainService(runs, new RoomCleared(false));

            Assert.NotNull(runs.CommitActorDeathChange(runs.TryClaimActorDeath(Character, 100)!));
            Assert.Null(await service.TryOpenClearAsync(Account, Character, run.RunId, run.RoomVersion, default));
            Assert.NotNull(runs.CommitActorDeathChange(runs.TryClaimActorDeath(Character, 101)!));
            Assert.NotNull(await service.TryOpenClearAsync(Account, Character, run.RunId, run.RoomVersion, default));
        });
    }

    [Fact]
    public async Task A_clear_map_list_is_met_by_clearing_any_one_of_its_maps()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var runs = TestParties.Runs();
            var layout = Layout([new(DungeonClearConditionKind.ClearMap, [12, 11], 1)]);
            var run = Enter(runs, layout, Start, actors: [Monster(100, 61000, 0)]);
            var service = new DungeonClearDomainService(runs, new RoomCleared(false));
            Assert.Null(await service.TryOpenClearAsync(Account, Character, run.RunId, run.RoomVersion, default));

            var moved = runs.TryMove(Character, run.RunId, run.RoomVersion, Boss,
                DungeonCellSeed.CompleteEmpty(Boss, 11))!;
            Assert.Contains(11, moved.ClearedRoomMapIds);
            Assert.NotNull(await service.TryOpenClearAsync(Account, Character, moved.RunId, moved.RoomVersion, default));
        });
    }

    [Fact]
    public async Task Ignoring_the_default_clear_keeps_a_cleared_boss_room_shut()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var runs = TestParties.Runs();
            var run = Enter(runs, Layout([]) with { IgnoresDefaultClear = true }, Boss);
            var service = new DungeonClearDomainService(runs, new RoomCleared(true));
            Assert.Null(await service.TryOpenClearAsync(Account, Character, run.RunId, run.RoomVersion, default));
        });
    }

    [Fact]
    public async Task A_maze_with_only_a_limit_time_entry_keeps_the_boss_room_default()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var runs = TestParties.Runs();
            var layout = Layout([new(DungeonClearConditionKind.LimitTime, [600], 1200)]);
            Assert.False(layout.ClearsOnConditions);
            var run = Enter(runs, layout, Boss);
            var service = new DungeonClearDomainService(runs, new RoomCleared(true));
            Assert.NotNull(await service.TryOpenClearAsync(Account, Character, run.RunId, run.RoomVersion, default));
        });
    }

    private static DungeonLayout Layout(IReadOnlyList<DungeonClearCondition> conditions) =>
        new(160, 2, 2, 1, Start, Boss, 10, 7,
            new Dictionary<MazeCell, DungeonCellDefinition>
            {
                [Start] = DungeonCellDefinition.Fixed(10),
                [Boss] = DungeonCellDefinition.Fixed(11),
            })
        { ClearConditions = conditions };

    private static DungeonRunState Enter(IDungeonRunDomainService runs, DungeonLayout layout, MazeCell cell,
        DungeonCellObjectState[]? objects = null, DungeonCellActorState[]? actors = null)
    {
        var mapId = cell == Start ? 10 : 11;
        var seed = new DungeonCellSeed(cell, mapId, 100, actors ?? [], []) { Objects = objects ?? [] };
        return runs.PreparePartyEntry([new(Account, Character, 0, true)], layout with { StartCell = cell, StartRoomMapId = mapId },
            DungeonDifficulty.Normal, seed, DungeonRunId.New(), DateTimeOffset.UnixEpoch).Run;
    }

    private static DungeonCellObjectState Object(int code) =>
        new(new(new(DungeonObjectSection.Passive, 0), code, 1048, 312, 0), []);

    private static DungeonCellActorState Monster(ushort sequence, int code, byte type) =>
        new(sequence, new DungeonRoomActor(code, 30, type), IsAiCharacter: false, IsDead: false,
            PendingLoot: [], IsMonster: true);

    private sealed class RoomCleared(bool cleared) : IMonsterKillDomainService
    {
        public ValueTask<KillIdentification> IdentifyAsync(
            DungeonRunState run, ushort actorSequence, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask<bool> IsCurrentRoomClearedAsync(DungeonRunState run, CancellationToken cancellationToken) =>
            ValueTask.FromResult(cleared);
    }
}
