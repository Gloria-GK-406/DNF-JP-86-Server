using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Tests.Characters;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.World;

public sealed partial class GiantDragonDungeonTests
{
    [SkippableTheory]
    [InlineData(3536, 20720, false, 0, 0)]
    [InlineData(3537, 20722, false, 0, 0)]
    [InlineData(3538, 20728, true, 10002520, 2)]
    [InlineData(3539, 20728, false, 10002519, 2)]
    [InlineData(3540, 20727, true, 0, 0)]
    public async Task Restored_variants_require_their_own_quest_and_ticket_and_materialize_every_declared_room(
        int dungeon, int quest, bool active, int ticket, byte difficulty)
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        await using var f = await Fixture(restored: true);
        Skip.If(await f.Services.GetRequiredService<ITownDefinitionSource>().GetAsync(100, default) is null,
            "Use task-restored PVF for restored-variant tests.");
        TestGameplay.Run(f.Services, () => f.Services.GetRequiredService<IOnlineCharacterRuntimeDomainService>()
            .CrossInto(f.Session, 100, 2, 900, 230));
        var context = new DungeonEntryContext(dungeon, DungeonEntryFacts.None with { Difficulty = (DungeonDifficulty)difficulty },
            0, 0, [new(new(1), new(71), 0, true)]);
        Assert.Equal(DungeonEntryFailure.MissingPrerequisite, (await DungeonEntryRouteDriver.EnterAsync(f.Services, context)).Failure);
        f.Sql(active ? $"INSERT INTO character_active_quests(character_id,slot,quest_id,trigger_value) VALUES(71,0,{quest},0)"
            : $"INSERT INTO character_invisible_flags(character_id,slot_index,flag_value) VALUES(71,{quest},1)");
        context = context with { Facts = context.Facts with { ActiveQuestIds = active ? [quest] : [], ClearedQuestIds = active ? [] : [quest] } };
        if (ticket > 0)
        {
            Assert.Equal(DungeonEntryFailure.MissingRequiredItem, (await DungeonEntryRouteDriver.EnterAsync(f.Services, context)).Failure);
            await TestGameplay.RunAsync(f.Services, async () =>
            {
                await using var tx = f.Scope.Begin();
                await f.Services.GetRequiredService<IItemDeliveryDomainService>().DeliverAsync(new(1), new(71),
                    [new(ticket, 2)], "giant-dragon-test", default, ItemDeliveryMode.InventoryOnly);
                await tx.CommitAsync(default);
            });
        }
        var result = await DungeonEntryRouteDriver.EnterAsync(f.Services, context);
        Assert.True(result.Entry is not null, $"{result.Failure}: {result.Detail}");
        Assert.Null(Assert.IsType<DungeonRunMode.GiantDragon>(result.Entry.Layout.Mode).TimeReward);
        Assert.Equal(dungeon == 3540 ? 0 : 1,
            (await f.Services.GetRequiredService<ICharacterFatigueDomainService>().BalanceOfAsync(new(71), default)).Used);
        if (ticket > 0) Assert.Equal(1, await CountTicket(f, ticket));
        Assert.Equal(DungeonEntryFailure.AlreadyInDungeon, (await DungeonEntryRouteDriver.EnterAsync(f.Services, context)).Failure);
        if (ticket > 0) Assert.Equal(1, await CountTicket(f, ticket));
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            var layout = result.Entry.Layout;
            var factory = f.Services.GetRequiredService<IDungeonCellFactory>();
            foreach (var (coordinate, cell) in layout.Cells)
            foreach (var map in cell.MapCandidates.Concat(cell.BossMapCandidates).Distinct())
            {
                var seed = await factory.CreateAsync(new(DungeonRunId.New(), layout, coordinate, map,
                    (DungeonDifficulty)difficulty, DateTimeOffset.UtcNow, context.Participants), default);
                Assert.True(seed.IsComplete);
                Assert.NotEmpty(seed.Actors);
            }
        });
    }

    private static async Task<int> CountTicket(LearningFixture f, int item) =>
        (await f.Services.GetRequiredService<ICharacterBusinessInventoryDomainService>().GetStore(new(71), BusinessInventoryStoreKind.Item)
            .GetContainer(BusinessInventorySpace.Quest).GetAllAsync(default)).Where(x => x.ItemId == item).Sum(x => x.Count);

    [SkippableTheory]
    [InlineData(3200)]
    [InlineData(3201)]
    [InlineData(3202)]
    public async Task Production_route_uses_real_room_actors_and_refuses_duplicate_entry_without_spending(int dungeon)
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        await using var f = await Fixture();
        var context = new DungeonEntryContext(dungeon, DungeonEntryFacts.None with { Difficulty = DungeonDifficulty.Adventure },
            0, 0, [new(new(1), new(71), 0, true)]);
        var result = await DungeonEntryRouteDriver.EnterAsync(f.Services, context);
        Assert.True(result.Entry is not null, $"{result.Failure}: {result.Detail}");
        Assert.NotNull(Assert.IsType<DungeonRunMode.GiantDragon>(result.Entry.Layout.Mode).TimeReward);
        Assert.NotEmpty(result.Entry.Visits[new(71)].Actors);
        Assert.Equal(8, (await f.Services.GetRequiredService<ICharacterFatigueDomainService>().BalanceOfAsync(new(71), default)).Used);
        var run = f.Services.DungeonRuns().Find(new Dfo.Server.Data.Characters.CharacterId(71))!;
        Assert.Equal(DungeonEntryFailure.AlreadyInDungeon, (await DungeonEntryRouteDriver.EnterAsync(f.Services, context)).Failure);
        Assert.Equal(run.RunId, f.Services.DungeonRuns().Find(new Dfo.Server.Data.Characters.CharacterId(71))!.RunId);
        Assert.Equal(8, (await f.Services.GetRequiredService<ICharacterFatigueDomainService>().BalanceOfAsync(new(71), default)).Used);
    }

    [SkippableFact]
    public async Task Restored_resource_preserves_original_gate_and_links_both_dragon_regions()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var content = ContentArchive.CreateContentProvider();
        var towns = content.GetRequiredService<ITownDefinitionSource>();
        var restored = await towns.GetAsync(100, default);
        Skip.If(restored is null, "Run this native-restoration oracle with the task-generated PVF.");
        Assert.Equal(98, restored!.AreaAt(2)!.Operand1);
        Assert.Equal(TownAreaKind.DungeonGate, restored.AreaAt(2)!.Kind);
        Assert.Equal(30, (await towns.GetAsync(18, default))!.AreaAt(0)!.Operand1);
        var workshop = (await towns.GetAsync(11, default))!.AreaAt(1)!;
        Assert.Contains((await towns.GetMapGeometryAsync(workshop.MapPath, default))!.TransitAreas,
            x => x.LinkedTownId == 100 && x.LinkedAreaId == 3);
        Assert.Contains((await towns.GetMapGeometryAsync(restored.Gate!.MapPath, default))!.TransitAreas,
            x => x.LinkedTownId == 18 && x.LinkedAreaId == 2);
        var quests = await content.GetRequiredService<IQuestDefinitionSource>().GetAllAsync(default);
        Assert.Equal(Enumerable.Range(20720, 16), quests.Where(q => q.QuestId is >= 20720 and <= 20735)
            .Select(q => q.QuestId).Order());
    }

    [SkippableFact]
    public async Task Ordinary_dragon_requires_its_native_gate_and_restoration_is_explicitly_disabled_by_default()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        await using var f = await Fixture();
        TestGameplay.Run(f.Services, () => f.Services.GetRequiredService<IOnlineCharacterRuntimeDomainService>()
            .CrossInto(f.Session, 11, 1, 100, 200));
        var context = new DungeonEntryContext(3200, DungeonEntryFacts.None with { Difficulty = DungeonDifficulty.Adventure },
            0, 0, [new(new(1), new(71), 0, true)]);
        Assert.Equal(DungeonEntryFailure.MissingPrerequisite, (await DungeonEntryRouteDriver.EnterAsync(f.Services, context)).Failure);
        Assert.Equal(DungeonEntryFailure.OutsideOpeningHours,
            (await DungeonEntryRouteDriver.EnterAsync(f.Services, context with { DungeonId = 3539 })).Failure);
        Assert.Equal(0, (await f.Services.GetRequiredService<ICharacterFatigueDomainService>().BalanceOfAsync(new(71), default)).Used);
    }

    [SkippableFact]
    public async Task Each_member_must_have_the_restored_unlock_and_the_whole_party_is_refused_before_spending()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        await using var f = await Fixture(restored: true);
        Skip.If(await f.Services.GetRequiredService<ITownDefinitionSource>().GetAsync(100, default) is null, "Requires restored task PVF.");
        var peer = f.EnterWorld(2, 72, "giant-dragon-peer");
        TestGameplay.Run(f.Services, () =>
        {
            var runtime = f.Services.GetRequiredService<IOnlineCharacterRuntimeDomainService>();
            runtime.CrossInto(f.Session, 100, 2, 900, 230);
            runtime.Begin(peer, new(100, 2, 900, 230, 0), 100);
        });
        f.Sql("INSERT INTO character_invisible_flags(character_id,slot_index,flag_value) VALUES(71,20720,1)");
        var context = new DungeonEntryContext(3536, DungeonEntryFacts.None with { ClearedQuestIds = [20720] }, 0, 0,
            [new(new(1), new(71), 0, true), new(new(2), new(72), 1, false)]);
        var refusal = await DungeonEntryRouteDriver.EnterAsync(f.Services, context);
        Assert.Equal(DungeonEntryFailure.MissingPrerequisite, refusal.Failure);
        Assert.Equal((byte)1, refusal.BlockedSlot);
        Assert.Equal(0, (await f.Services.GetRequiredService<ICharacterFatigueDomainService>().BalanceOfAsync(new(71), default)).Used);
        Assert.Null(f.Services.DungeonRuns().Find(new CharacterId(71)));
    }

    [SkippableFact]
    public async Task Giant_dragon_room_creation_failure_rolls_back_a_real_quest_ticket()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        await using var f = await Fixture(restored: true, configure: s => s.AddSingleton<IDungeonCellFactory>(new BrokenCellFactory()));
        Skip.If(await f.Services.GetRequiredService<ITownDefinitionSource>().GetAsync(100, default) is null, "Requires restored task PVF.");
        TestGameplay.Run(f.Services, () => f.Services.GetRequiredService<IOnlineCharacterRuntimeDomainService>().CrossInto(f.Session, 100, 2, 900, 230));
        f.Sql("INSERT INTO character_invisible_flags(character_id,slot_index,flag_value) VALUES(71,20728,1)");
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            await using var tx = f.Scope.Begin();
            await f.Services.GetRequiredService<IItemDeliveryDomainService>().DeliverAsync(new(1), new(71),
                [new(10002519, 2)], "giant-dragon-test", default, ItemDeliveryMode.InventoryOnly);
            await tx.CommitAsync(default);
        });
        var context = new DungeonEntryContext(3539, DungeonEntryFacts.None with { Difficulty = DungeonDifficulty.Tier2 },
            0, 0, [new(new(1), new(71), 0, true)]);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await DungeonEntryRouteDriver.EnterAsync(f.Services, context));
        Assert.Equal(2, await CountTicket(f, 10002519));
        Assert.Equal(0, (await f.Services.GetRequiredService<ICharacterFatigueDomainService>().BalanceOfAsync(new(71), default)).Used);
        Assert.Null(f.Services.DungeonRuns().Find(new CharacterId(71)));
    }

    private sealed class BrokenCellFactory : IDungeonCellFactory
    {
        public ValueTask<DungeonCellSeed> CreateAsync(DungeonCellCreationContext context, CancellationToken ct) =>
            throw new InvalidOperationException("test room creation failure");
    }

    private static async Task<LearningFixture> Fixture(bool restored = false, Action<IServiceCollection>? configure = null)
    {
        var f = new LearningFixture(useArchive: true, configure: s =>
        {
            s.AddSingleton(new GiantDragonOptions { RestoredVariantsEnabled = restored });
            configure?.Invoke(s);
        });
        await f.SeedAsync();
        TestGameplay.Run(f.Services, () => f.Services.GetRequiredService<ICharacterContainerDomainService>().Register(new(71),
            new PersistentItemContainer(ContainerId.ForCharacter(new(71), InventoryContainerType.Main),
                new(InventoryContainerType.Main, 0), f.Services.GetRequiredService<IItemDomainStore>(), f.Scope,
                f.Services.GetRequiredService<IItemPlacementRuleService>(), f.Services.GetRequiredService<IItemSlotRuleService>())));
        f.Services.GetRequiredService<IIndependentDropRuleService>().Initialize();
        f.Services.GetRequiredService<IExtraDropRuleService>().Initialize();
        f.Services.GetRequiredService<IMonsterDeclaredDropRuleService>().Initialize();
        f.Services.GetRequiredService<IWorldDropRuleService>().Initialize();
        TestGameplay.Run(f.Services, () => f.Services.GetRequiredService<IOnlineCharacterRuntimeDomainService>()
            .Begin(f.Session, new CharacterPositionState(18, 0, 300, 230, 0), 100));
        return f;
    }
}
