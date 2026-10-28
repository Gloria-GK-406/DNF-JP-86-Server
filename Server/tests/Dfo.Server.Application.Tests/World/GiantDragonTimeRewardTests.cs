using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.World;
using Dfo.Server.Application.Tests.Characters;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.World;

public sealed partial class GiantDragonDungeonTests
{
    private static readonly GiantDragonTimeRewardPlan RewardPlan = new(
        new(10094602, 1, DropRarity.Uncommon, DropItemKind.Stackable), 9, 2,
        TimeSpan.FromSeconds(145), TimeSpan.FromSeconds(45));

    [Theory]
    [InlineData(-1, 9)] [InlineData(0, 9)] [InlineData(40362, 9)] [InlineData(50284, 9)]
    [InlineData(99999, 9)] [InlineData(100000, 9)] [InlineData(144999, 9)]
    [InlineData(145000, 8)] [InlineData(145001, 8)] [InlineData(189999, 8)]
    [InlineData(190000, 7)] [InlineData(234999, 7)] [InlineData(235000, 6)]
    [InlineData(280000, 5)] [InlineData(325000, 4)] [InlineData(370000, 3)]
    [InlineData(414999, 3)] [InlineData(415000, 2)] [InlineData(460000, 2)]
    [InlineData(long.MaxValue / 10000, 2)]
    public void Approved_time_reward_has_exact_millisecond_boundaries(long elapsed, int expected) =>
        Assert.Equal(expected, GiantDragonTimeRewardRules.CountFor(RewardPlan, TimeSpan.FromMilliseconds(elapsed)));

    [Theory]
    [InlineData(true, true, true, true, 9)]
    [InlineData(false, true, true, true, 4)]
    [InlineData(true, false, true, true, 4)]
    [InlineData(true, true, false, true, 4)]
    [InlineData(true, true, true, false, 4)]
    public void Shared_time_reward_preserves_other_and_personal_loot_and_is_not_multiplied_by_party(
        bool bossRoom, bool monster, bool enabled, bool bossActor, int expected)
    {
        TestGameplay.Run(() =>
        {
            var clock = new DragonRewardClock();
            var rules = new GiantDragonRuleService(null!, null!, new(), null!);
            var runs = new DungeonRunDomainService(null!, null!, clock: clock, giantDragons: rules);
            var cell = new MazeCell(0, 0);
            var boss = bossRoom ? cell : new MazeCell(1, 0);
            var layout = new DungeonLayout(3200, 0, 2, 1, cell, boss, 38119, 1,
                new Dictionary<MazeCell, DungeonCellDefinition> { [cell] = DungeonCellDefinition.Fixed(38119) })
                { GiantDragonTimeReward = enabled ? RewardPlan : null };
            RolledDrop[] loot = [new(10094602, 4, DropRarity.Uncommon, DropItemKind.Stackable),
                new(3326, 4, DropRarity.Rare, DropItemKind.Stackable),
                new(0, 123, 0, DropItemKind.Gold), new(10002515, 2, 0, DropItemKind.Stackable),
                new(10094602, 2, DropRarity.Uncommon, DropItemKind.Stackable, OwnerCharacterId: new(2)),
                new(10094602, 1, DropRarity.Uncommon, DropItemKind.Stackable, OwnerCharacterId: new(1), Delivery: RolledDropDelivery.DirectQuestItem)];
            var run = runs.PreparePartyEntry([new(new(1), new(1), 0, true), new(new(2), new(2), 1, false)],
                layout, DungeonDifficulty.Adventure,
                new(cell, 38119, 1, [new(1, new(69037, 1, bossActor ? (byte)3 : (byte)0), false, false, loot, monster)], []),
                DungeonRunId.New(), clock.Now).Run;
            clock.Now = clock.Now.AddMilliseconds(40362);
            var change = runs.CommitActorDeathChange(runs.TryClaimActorDeath(new(1), 1)!)!;
            Assert.Equal(2, change.Recipients.Count);
            Assert.Equal(expected, change.AddedGroundItems.Where(x => x.ItemId == 10094602 && x.OwnerCharacterId is null).Sum(x => x.Count));
            Assert.Equal(4, change.AddedGroundItems.Where(x => x.ItemId == 3326).Sum(x => x.Count));
            Assert.Equal(123, Assert.Single(change.AddedGroundItems, x => x.ItemId == 0).Count);
            Assert.Equal(2, Assert.Single(change.AddedGroundItems, x => x.ItemId == 10002515).Count);
            Assert.Equal(2, Assert.Single(change.AddedGroundItems, x => x.ItemId == 10094602 && x.OwnerCharacterId == new CharacterId(2)).Count);
            Assert.DoesNotContain(change.AddedGroundItems, x => x.OwnerCharacterId == new CharacterId(1));
            Assert.Null(runs.TryClaimActorDeath(new(2), 1));
        });
    }

    [SkippableFact]
    public async Task Missing_time_reward_material_refuses_entry_before_spending_or_publishing_run()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        await using var f = await Fixture(configure: services => services.AddSingleton<IItemCatalogSource>(new MissingCrystalCatalog()));
        var attempt = await DungeonEntryRouteDriver.EnterAsync(f.Services, new(3200,
            DungeonEntryFacts.None with { Difficulty = DungeonDifficulty.Adventure }, 0, 0,
            [new(new(1), new(71), 0, true)]));
        Assert.Equal(DungeonEntryFailure.ConfigurationInvalid, attempt.Failure);
        Assert.Null(f.Services.GetRequiredService<IDungeonRunDomainService>().Find(new CharacterId(71)));
        Assert.Equal(0, (await f.Services.GetRequiredService<ICharacterFatigueDomainService>().BalanceOfAsync(new(71), default)).Used);
    }

    private sealed class MissingCrystalCatalog : IItemCatalogSource
    {
        public ValueTask<Dfo.Server.Data.Inventory.ItemCatalogEntry?> FindAsync(int id, CancellationToken token) => ValueTask.FromResult<Dfo.Server.Data.Inventory.ItemCatalogEntry?>(null);
        public ValueTask<IReadOnlyList<Dfo.Server.Data.Inventory.ItemCatalogEntry>> SearchAsync(Dfo.Server.Data.Inventory.ItemCatalogFilter filter, CancellationToken token) => throw new NotSupportedException();
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Guaranteed_time_reward_survives_an_empty_original_roll_and_only_replaces_the_last_boss(bool earlierBoss)
    {
        TestGameplay.Run(() =>
        {
            var clock = new DragonRewardClock();
            var runs = new DungeonRunDomainService(null!, null!, clock: clock,
                giantDragons: new GiantDragonRuleService(null!, null!, new(), null!));
            var cell = new MazeCell(0, 0);
            var layout = new DungeonLayout(3200, 0, 1, 1, cell, cell, 38119, 1,
                new Dictionary<MazeCell, DungeonCellDefinition> { [cell] = DungeonCellDefinition.Fixed(38119) })
                { GiantDragonTimeReward = RewardPlan };
            var actors = new List<DungeonCellActorState> { new(1, new(69037, 1, 3), false, false,
                [new(10002515, 2, 0, DropItemKind.Stackable)]) };
            if (earlierBoss) actors.Add(new(2, new(69037, 1, 3), false, false,
                [new(3326, 4, DropRarity.Rare, DropItemKind.Stackable)]));
            runs.PreparePartyEntry([new(new(1), new(1), 0, true)], layout, DungeonDifficulty.Adventure,
                new(cell, 38119, 1, actors, []), DungeonRunId.New(), clock.Now);
            if (earlierBoss)
            {
                var first = runs.CommitActorDeathChange(runs.TryClaimActorDeath(new(1), 2)!)!;
                Assert.Equal(4, first.AddedGroundItems.Sum(x => x.Count));
                Assert.False(first.IsCellCleared);
            }
            var last = runs.CommitActorDeathChange(runs.TryClaimActorDeath(new(1), 1)!)!;
            var material = last.AddedGroundItems.Where(x => x.ItemId == 10094602).ToArray();
            Assert.Equal(9, material.Length);
            Assert.All(material, x => { Assert.Equal(1, x.Count); Assert.Equal(DropRarity.Uncommon, x.Rarity); });
            Assert.Equal(2, Assert.Single(last.AddedGroundItems, x => x.ItemId == 10002515).Count);
            Assert.True(last.IsCellCleared);
        });
    }

    [SkippableFact]
    public async Task Time_reward_and_verified_death_rollback_together_then_retry_and_duplicate_are_exact()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        var clock = new DragonRewardClock();
        await using var f = await Fixture(configure: services => services.AddSingleton<TimeProvider>(clock));
        var run = await ReachRewardBoss(f, 3200);
        var boss = Assert.Single(run.CurrentCellState!.Actors, x => x.Actor.IsBoss && x.IsMonster);
        var beforeLoot = boss.PendingLoot.ToArray();
        var character = f.Services.GetRequiredService<ICharacterDomainService>().Get(new(71));
        var before = await character.ProgressAsync(default);
        var request = new MonsterDeathSettlementRequest(f.Session, [f.Session], run.RunId, run.RoomVersion, boss.Sequence);
        clock.Now = run.StartedAt.AddMilliseconds(160000);
        await Assert.ThrowsAsync<InvalidOperationException>(() => TestGameplay.RunAsync(f.Services, async () =>
        {
            await using var tx = f.Scope.Begin();
            await TestEffectFootprints.Run([f.Session], cap =>
                f.Services.GetRequiredService<IMonsterDeathDomainService>().SettleAsync(request, cap, default));
            throw new InvalidOperationException("Abort the complete death transaction before commit");
        }));
        var runs = f.Services.GetRequiredService<IDungeonRunDomainService>();
        var rolledBack = runs.Find(new CharacterId(71))!;
        Assert.False(rolledBack.CurrentCellState!.FindActor(boss.Sequence)!.IsDead);
        Assert.Equal(beforeLoot, rolledBack.CurrentCellState.FindActor(boss.Sequence)!.PendingLoot);
        Assert.DoesNotContain(rolledBack.CurrentCellState.GroundItems, x => x.ItemId == 10094602);
        Assert.Equal(before, await character.ProgressAsync(default));

        async Task<MonsterDeathSettlementResult> Settle() => await TestGameplay.RunAsync(f.Services, async () =>
        {
            await using var tx = f.Scope.Begin();
            var result = await TestEffectFootprints.Run([f.Session], cap =>
                f.Services.GetRequiredService<IMonsterDeathDomainService>().SettleAsync(request, cap, default));
            await tx.CommitAsync(default);
            return result;
        });
        var result = await Settle();
        Assert.NotNull(result.Death?.Change);
        Assert.Equal(8, result.Death!.Change!.AddedGroundItems.Where(x => x.ItemId == 10094602).Sum(x => x.Count));
        Assert.Equal(beforeLoot.Where(x => x.ItemId == 3326).Sum(x => x.Count),
            result.Death.Change.AddedGroundItems.Where(x => x.ItemId == 3326).Sum(x => x.Count));
        var earned = await character.ProgressAsync(default);
        Assert.NotEqual(before, earned);
        clock.Now = clock.Now.AddHours(1);
        Assert.Null((await Settle()).Death);
        Assert.Equal(earned, await character.ProgressAsync(default));
        Assert.Equal(8, runs.Find(new CharacterId(71))!.CurrentCellState!.GroundItems.Where(x => x.ItemId == 10094602).Sum(x => x.Count));
    }

    [SkippableTheory]
    [InlineData(3200, 50284, 9)]
    [InlineData(3201, 40362, 9)]
    [InlineData(3202, 90000, 9)]
    [InlineData(3200, 160000, 8)]
    [InlineData(3201, 220000, 7)]
    [InlineData(3202, 460000, 2)]
    public async Task Time_reward_replaces_final_boss_shared_crystals_once_and_preserves_breath_using_confirmed_death_time(
        int dungeonId, int elapsedMilliseconds, int expected)
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        var clock = new DragonRewardClock();
        await using var f = await Fixture(configure: services => services.AddSingleton<TimeProvider>(clock));
        var bossRun = await ReachRewardBoss(f, dungeonId);
        await TestGameplay.RunAsync(f.Services, () =>
        {
            var runs = f.Services.GetRequiredService<IDungeonRunDomainService>();
            var boss = Assert.Single(bossRun.CurrentCellState!.Actors, x => x.Actor.IsBoss && x.IsMonster);
            var originalBreath = boss.PendingLoot.Where(x => x.ItemId == 3326).Sum(x => x.Count);
            clock.Now = bossRun.StartedAt.AddMilliseconds(elapsedMilliseconds);
            var claim = runs.TryClaimActorDeath(new(71), boss.Sequence)!;
            // Deferred commit/animation time must not reduce a reward already earned by this death.
            clock.Now = clock.Now.AddHours(1);
            var change = runs.CommitActorDeathChange(claim)!;
            var crystals = change.AddedGroundItems.Where(x => x.ItemId == 10094602).ToArray();
            Assert.Equal(expected, crystals.Sum(x => x.Count));
            Assert.All(crystals, item => Assert.Null(item.OwnerCharacterId));
            Assert.Equal(originalBreath, change.AddedGroundItems.Where(x => x.ItemId == 3326).Sum(x => x.Count));
            Assert.Null(runs.TryClaimActorDeath(new(71), boss.Sequence));
            Assert.Null(runs.CommitActorDeathChange(claim));
            Assert.Equal(expected, runs.Find(new CharacterId(71))!.CurrentCellState!.GroundItems
                .Where(x => x.ItemId == 10094602).Sum(x => x.Count));
            return Task.CompletedTask;
        });
    }

    private static async Task<DungeonRunState> ReachRewardBoss(LearningFixture f, int dungeonId)
    {
        var context = new DungeonEntryContext(dungeonId,
            DungeonEntryFacts.None with { Difficulty = DungeonDifficulty.Adventure }, 0, 0,
            [new(new(1), new(71), 0, true)]);
        var result = await DungeonEntryRouteDriver.EnterAsync(f.Services, context);
        Assert.True(result.Entry is not null, $"{result.Failure}: {result.Detail}");
        return await TestGameplay.RunAsync(f.Services, async () =>
        {
            var runs = f.Services.GetRequiredService<IDungeonRunDomainService>();
            var run = runs.Find(new CharacterId(71))!;
            var queue = new Queue<MazeCell>();
            var previous = new Dictionary<MazeCell, MazeCell?> { [run.CurrentCell] = null };
            queue.Enqueue(run.CurrentCell);
            while (queue.TryDequeue(out var cell) && cell != run.Layout.BossCell)
                foreach (var adjacent in run.Layout.Cells.Keys.Where(x =>
                    Math.Abs(x.X - cell.X) + Math.Abs(x.Y - cell.Y) == 1 && !previous.ContainsKey(x)))
                { previous[adjacent] = cell; queue.Enqueue(adjacent); }
            var route = new List<MazeCell>();
            for (MazeCell? cell = run.Layout.BossCell; cell is not null; cell = previous[cell.Value])
                route.Add(cell.Value);
            route.Reverse();
            foreach (var target in route.Skip(1))
            {
                foreach (var actor in run.CurrentCellState!.Actors.Where(x => !x.IsDead))
                    if (runs.TryClaimActorDeath(new(71), actor.Sequence) is { } claim)
                        runs.CommitActorDeathChange(claim);
                run = runs.Find(new CharacterId(71))!;
                var map = run.Layout.Cells[target].BossMapCandidates.Concat(run.Layout.Cells[target].MapCandidates).First();
                var seed = await f.Services.GetRequiredService<IDungeonCellFactory>().CreateAsync(
                    new(run.RunId, run.Layout, target, map, run.Difficulty, run.StartedAt, context.Participants), default);
                run = runs.TryMove(new(71), run.RunId, run.RoomVersion, target, seed)
                    ?? throw new InvalidOperationException("Reward oracle could not move to the next native cell.");
            }
            return run;
        });
    }

    private sealed class DragonRewardClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
