using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Events;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Tests.Characters;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;
using Dfo.Server.Infrastructure.Content;
using Microsoft.Extensions.DependencyInjection;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.World;

public sealed class SealedTreasureTests
{
    [Fact]
    public async Task Activity_switch_projects_3557_independently_of_existing_activity_gates()
    {
        await TestGameplay.RunAsync(async () =>
        {
            await using var f = new LearningFixture(); await f.SeedAsync();
            var activities = f.Services.GetRequiredService<IEventManagementDomainService>();
            var projections = f.Services.GetRequiredService<IEventPlayerProjectionDomainService>();
            var open = await projections.ReadNowAsync(f.Session, default);
            Assert.True(Assert.Single(open.ScheduledDungeons, x => x.DungeonId == 3557).Open);
            await using (var tx = f.Scope.Begin())
            {
                await activities.ChangeAsync(new(2331, 0, "disable"), "test", DateTimeOffset.UtcNow, default);
                await tx.CommitAsync(default);
            }
            var closed = await projections.ReadNowAsync(f.Session, default);
            Assert.False(Assert.Single(closed.ScheduledDungeons, x => x.DungeonId == 3557).Open);
            Assert.True(Assert.Single(closed.ScheduledDungeons, x => x.DungeonId == 100).Open);
            Assert.True(Assert.Single(closed.ScheduledDungeons, x => x.DungeonId == 3700).Open);
            Assert.Single(closed.Information, x => x.EventId == 69);
        });
    }

    [SkippableFact]
    public async Task Production_factory_materializes_all_25_maps_and_both_platinum_packages()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        await using var f = await Fixture();
        var definition = await f.Services.GetRequiredService<ISealedTreasureRuleService>().DefinitionAsync(default);
        Assert.Equal(70, definition.MinimumRequiredLevel);
        Assert.Equal(10003196, Assert.Single(definition.RequiredItems).ItemId);
        Assert.Equal(10003235, Assert.Single(definition.EventRequiredItems).ItemId);
        Assert.False(definition.ConsumesFatigue);
        var definitions = f.Services.GetRequiredService<IDungeonDefinitionSource>();
        Assert.True((await definitions.GetAsync(70, default))!.ConsumesFatigue);
        var layout = (await f.Services.GetRequiredService<IDungeonStructureRuleService>()
            .ResolveAsync(3557, Entry().Facts, 17, default))!;
        Assert.Equal(25, layout.Cells.Count);
        var actors = new List<DungeonCellActorState>();
        var objects = new List<DungeonCellObjectState>();
        foreach (var (cell, room) in layout.Cells)
        {
            var map = Assert.Single(room.MapCandidates);
            var seed = await f.Services.GetRequiredService<IDungeonCellFactory>().CreateAsync(
                new(DungeonRunId.New(), layout, cell, map, DungeonDifficulty.Tier2,
                    DateTimeOffset.UtcNow, Entry().Participants), default);
            Assert.True(seed.IsComplete); actors.AddRange(seed.Actors); objects.AddRange(seed.Objects);
        }
        Assert.Equal(257, actors.Count);
        Assert.Equal(47, actors.Select(x => x.Actor.Code).Distinct().Count());
        foreach (var monster in new[] { 110348, 110349, 110350, 110351, 56051 })
            Assert.Contains(actors, x => x.Actor.Code == monster);
        foreach (var code in new[] { 54291, 54292, 54293, 54294, 15150 })
            Assert.Contains(objects, x => x.Object.Code == code);
        var packages = f.Services.GetRequiredService<IPackageOpeningDefinitionSource>();
        foreach (var item in new[] { 10003717, 10003195 })
        {
            var package = await packages.GetAsync(item, default);
            Assert.NotNull(package);
            var pool = Assert.Single(package.Pools);
            Assert.Equal(1, pool.Draws);
            Assert.Equal(147, pool.Choices.Length);
            Assert.Equal(100000, pool.Choices.Sum(x => x.Weight));
        }
    }

    [SkippableFact]
    public async Task Admission_uses_free_ticket_once_rolls_back_and_never_spends_exhausted_fatigue()
    {
        await TestGameplay.RunAsync(async () =>
        {
            Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
            await using var f = await Fixture();
            var service = f.Services.GetRequiredService<ISealedTreasureDomainService>();
            var fatigue = f.Services.GetRequiredService<ICharacterFatigueDomainService>();
            await using (var tx = f.Scope.Begin())
            {
                var grant = f.Services.GetRequiredService<IItemGrantDomainService>();
                await grant.GrantAllPersistentAsync(new(71), 10003196, 2, default);
                await grant.GrantAllPersistentAsync(new(71), 10003235, 1, default);
                await fatigue.SpendAsync(new(71), (await fatigue.BalanceOfAsync(new(71), default)).Remaining, default);
                await tx.CommitAsync(default);
            }
            var used = (await fatigue.BalanceOfAsync(new(71), default)).Used;
            await using (var tx = f.Scope.Begin())
                Assert.NotNull((await service.EnterAsync(Entry(), default)).Entry);
            Assert.Equal(1, await Count(f, 10003235));
            Assert.Null(f.Services.DungeonRuns().Find(new CharacterId(71)));
            await using (var tx = f.Scope.Begin())
            {
                Assert.Equal(DungeonEntryFailure.MissingPrerequisite, (await service.EnterAsync(Entry() with { PracticeMode = 1 }, default)).Failure);
                Assert.Equal(DungeonEntryFailure.MissingPrerequisite, (await service.EnterAsync(Entry() with { RequestMode = 1 }, default)).Failure);
                Assert.Equal(DungeonEntryFailure.DifficultyNotUnlocked, (await service.EnterAsync(Entry() with { Facts = DungeonEntryFacts.None }, default)).Failure);
                Assert.Equal(DungeonEntryFailure.MissingPrerequisite, (await service.EnterAsync(Entry() with {
                    Participants = [.. Entry().Participants, new(new(2), new(72), 1, false)] }, default)).Failure);
                await f.Services.GetRequiredService<ICharacterDomainService>().Get(new(71)).SetLevelAsync(69, default);
                Assert.Equal(DungeonEntryFailure.LevelTooLow, (await service.EnterAsync(Entry(), default)).Failure);
                await f.Services.GetRequiredService<ICharacterDomainService>().Get(new(71)).SetLevelAsync(70, default);
                Assert.NotNull((await service.EnterAsync(Entry(), default)).Entry);
                await tx.CommitAsync(default);
            }
            Assert.Equal(0, await Count(f, 10003235)); Assert.Equal(2, await Count(f, 10003196));
            var runs = f.Services.DungeonRuns();
            var run = runs.Find(new CharacterId(71))!;
            var target = run.Layout.Cells.Keys.First(x => run.Layout.CanTraverse(run.CurrentCell, x));
            await using (var tx = f.Scope.Begin())
            {
                Assert.Equal(DungeonEntryFailure.AlreadyInDungeon, (await service.EnterAsync(Entry(), default)).Failure);
                Assert.NotNull(await f.Services.GetRequiredService<IDungeonVisitDomainService>().MoveAsync(new(71), run, target, default));
                await tx.CommitAsync(default);
            }
            await using (var tx = f.Scope.Begin())
            {
                Assert.NotNull(await f.Services.GetRequiredService<IDungeonVisitDomainService>().MoveAsync(new(71), runs.Find(new CharacterId(71))!, run.CurrentCell, default));
                await tx.CommitAsync(default);
            }
            Assert.Equal(used, (await fatigue.BalanceOfAsync(new(71), default)).Used);
            await using (var tx = f.Scope.Begin())
            {
                await f.Services.GetRequiredService<IEventManagementDomainService>().ChangeAsync(new(2331, 0, "disable"), "test", DateTimeOffset.UtcNow, default);
                Assert.Equal(DungeonEntryFailure.OutsideOpeningHours, (await service.EnterAsync(Entry(), default)).Failure);
                await tx.CommitAsync(default);
            }
            Assert.Equal(2, await Count(f, 10003196));
        });
    }

    private static DungeonEntryContext Entry() => new(3557,
        DungeonEntryFacts.None with { Difficulty = DungeonDifficulty.Tier2 }, 0, 0, [new(new(1), new(71), 0, true)]);
    private static async Task<int> Count(LearningFixture f, int item) =>
        (await f.Services.GetRequiredService<ICharacterContainerDomainService>().GetContainer(new(71), InventoryContainerType.Main)
            .GetAllAsync(default)).Where(x => x.ItemId == item).Sum(x => x.Count);
    private static async Task<LearningFixture> Fixture()
    {
        var f = new LearningFixture(useArchive: true, configure: s =>
            s.AddSingleton(new PvfContentOptions { ArchivePath = ContentArchive.Path }));
        await f.SeedAsync();
        TestGameplay.Run(f.Services, () => f.Services.GetRequiredService<ICharacterContainerDomainService>().Register(new(71), new PersistentItemContainer(
            ContainerId.ForCharacter(new(71), InventoryContainerType.Main), new(InventoryContainerType.Main, 0),
            f.Services.GetRequiredService<IItemDomainStore>(), f.Scope,
            f.Services.GetRequiredService<IItemPlacementRuleService>(), f.Services.GetRequiredService<IItemSlotRuleService>())));
        f.Services.GetRequiredService<IIndependentDropRuleService>().Initialize();
        f.Services.GetRequiredService<IExtraDropRuleService>().Initialize();
        f.Services.GetRequiredService<IMonsterDeclaredDropRuleService>().Initialize();
        f.Services.GetRequiredService<IWorldDropRuleService>().Initialize();
        return f;
    }
}
