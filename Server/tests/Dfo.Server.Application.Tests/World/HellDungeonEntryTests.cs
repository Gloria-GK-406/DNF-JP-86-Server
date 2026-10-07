using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Tests.Characters;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.World;

public sealed class HellDungeonEntryTests
{
    [Fact]
    public async Task Registered_CMD_commits_manual_entry_and_ticket_debit_without_an_outer_transaction()
    {
        await using var f = new LearningFixture(configure: services =>
        {
            new Dfo.Server.Protocol.ProtocolModule().ConfigureServices(services,
                new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
            services.AddSingleton<IItemPlacementDefinitionSource>(new Placements());
            services.AddSingleton<IDungeonStructureRuleService>(new Structures());
            services.AddSingleton<IHellDungeonDefinitionSource>(new Content());
            services.AddSingleton<IDungeonRoomActorSource>(new RoomActors());
            services.AddSingleton<IDungeonCellFactory>(new Cells());
        });
        await f.SeedAsync();
        await SeedTicket(f, new(71), 3330, 30, 0);
        var result = await f.Services.GetRequiredService<Dfo.Server.Application.Commands.ICommandBus>().SendAsync(
            new Dfo.Server.Protocol.Game.DungeonEntry.EnterDungeonCommand(Context() with { Participants = [Party[0]] }), default);
        Assert.NotNull(result.Entry);
        var run = f.Services.GetRequiredService<IDungeonRunDomainService>().Find(new CharacterId(71));
        Assert.NotNull(run);
        Assert.Equal(DungeonEntryKind.ManualHell, run.EntryKind);
        Assert.Equal(10, await Count(f, new(71), 3330));
        Assert.Equal(1, (await Fatigue(f).BalanceOfAsync(new(71), default)).Used);
        Assert.Null(f.Scope.Current);
    }
    [Theory]
    [InlineData(0, 0, DungeonEntryKind.Normal)]
    [InlineData(1, 0, DungeonEntryKind.ManualHell)]
    [InlineData(0, 1, DungeonEntryKind.Normal)]
    [InlineData(3, 0, DungeonEntryKind.Normal)]
    [InlineData(4, 0, DungeonEntryKind.Normal)]
    public void Only_manual_mode_routes_to_hell(byte mode, byte practice, DungeonEntryKind expected) =>
        Assert.Equal(expected, new DungeonEntryDecisionDomainService().ResolveEntryKind(
            new(150, DungeonEntryFacts.None, mode, practice, Party)));

    [Theory]
    [InlineData(45, 10)]
    [InlineData(70, 20)]
    [InlineData(85, 26)]
    public void Ticket_cost_scales_by_minimum_level_and_passes_have_priority(byte level, int count)
    {
        var rules = RuleService(new Content());
        Assert.Equal([new(4444, 1), new HellTicketAlternative(3330, count)], rules.TicketAlternatives(Rules, level));
        Assert.Empty(rules.TicketAlternatives(Rules, 44));
    }

    [Fact]
    public async Task Frozen_plan_uses_one_weighted_group_per_order_and_refuses_oversized_wire_rooms()
    {
        var content = new Content();
        var rules = RuleService(content);
        var plan = await rules.PrepareAsync(Rules, Layout, 42, default);
        Assert.NotNull(plan);
        Assert.Equal((byte)1, plan.Difficulty);
        Assert.Equal(new MazeCell(1, 0), plan.Cell);
        Assert.Equal(900, plan.MapId);
        Assert.Equal((byte)80, Assert.Single(plan.Actors).Actor.Level);
        Assert.Equal(plan.Actors, (await rules.PrepareAsync(Rules, Layout, 42, default))!.Actors);
        content.Actors = Enumerable.Repeat(new DungeonRoomActor(1000, 0, 0), 256).ToArray();
        Assert.Null(await rules.PrepareAsync(Rules, Layout, 42, default));
    }

    [SkippableFact]
    public async Task Real_PVF_preparation_selects_a_declared_timegate_room_without_request_difficulty_override()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var content = ContentArchive.CreateContentProvider();
        var structures = new DungeonStructureRuleService(content.GetRequiredService<IDungeonDefinitionSource>(),
            content.GetRequiredService<IDungeonMapDefinitionSource>());
        var rules = new HellDungeonEntryRuleService(content.GetRequiredService<IHellDungeonDefinitionSource>(),
            content.GetRequiredService<IDungeonRoomActorSource>());
        var declarations = (await rules.GetRulesAsync(77, default))!;
        for (var seed = 0; seed < 20; seed++)
        {
            var layout = (await structures.ResolveAsync(77, DungeonEntryFacts.None, seed, default))!;
            var plan = await rules.PrepareAsync(declarations, layout, seed, default);
            Assert.NotNull(plan);
            Assert.Contains(plan.Cell, layout.Cells.Keys);
            Assert.Equal(declarations.Rooms[layout.MazeIndex].MapId, plan.MapId);
            Assert.NotEmpty(plan.Actors);
            Assert.All(plan.Actors, x => Assert.InRange(x.Actor.Level, (byte)1, byte.MaxValue));
            Assert.Equal(plan.Actors, (await rules.PrepareAsync(declarations, layout, seed, default))!.Actors);
            var frozen = layout with { EntryKind = DungeonEntryKind.ManualHell, Hell = plan };
            Assert.Equal(plan.MapId, (await structures.SelectMapAsync(frozen, null, plan.Cell, default)).MapId);
        }
    }

    [Fact]
    public async Task Any_member_missing_tickets_refuses_without_debit_or_run()
    {
        await using var f = Fixture();
        await f.SeedAsync();
        await SeedTicket(f, new(71), 3330, 20, 0);
        Register(f, new(72));
        var (hell, runs) = Service(f);
        await using (var tx = f.Scope.Begin())
        {
            var result = await hell.EnterManualAsync(Context(), default);
            Assert.Equal(DungeonEntryFailure.MissingHellTicket, result.Failure);
            Assert.Equal((byte)1, result.BlockedSlot);
            await tx.CommitAsync(default);
        }
        Assert.Equal(20, await Count(f, new(71), 3330));
        Assert.Null(runs.Find(new CharacterId(71)));
        Assert.Equal(0, (await Fatigue(f).BalanceOfAsync(new(71), default)).Used);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Successful_party_entry_uses_pass_first_and_debits_split_stacks_once(bool commit)
    {
        await using var f = Fixture();
        await f.SeedAsync();
        await SeedTicket(f, new(71), 4444, 1, 0);
        await SeedTicket(f, new(71), 3330, 30, 1);
        await SeedTicket(f, new(72), 3330, 7, 0);
        await SeedTicket(f, new(72), 3330, 20, 1);
        var (hell, runs) = Service(f);
        await using (var tx = f.Scope.Begin())
        {
            var result = await hell.EnterManualAsync(Context(), default);
            Assert.NotNull(result.Entry);
            Assert.Equal(3, result.Entry.InventoryChanges.Count);
            Assert.Single(result.Entry.Visits.Values.Select(x => x.Run.RunId).Distinct());
            Assert.All(result.Entry.Visits.Values, x =>
            {
                Assert.Equal(DungeonEntryKind.ManualHell, x.Run.EntryKind);
                Assert.Same(result.Entry.Layout.Hell, x.Run.Hell);
            });
            Assert.Null(runs.Find(new CharacterId(71))); // publish only at commit
            if (commit) await tx.CommitAsync(default);
        }
        Assert.Equal(commit ? 0 : 1, await Count(f, new(71), 4444));
        Assert.Equal(30, await Count(f, new(71), 3330));
        Assert.Equal(commit ? 7 : 27, await Count(f, new(72), 3330));
        Assert.Equal(commit ? 1 : 0, (await Fatigue(f).BalanceOfAsync(new(71), default)).Used);
        Assert.Equal(commit ? 1 : 0, (await Fatigue(f).BalanceOfAsync(new(72), default)).Used);
        if (commit) Assert.Equal(runs.Find(new CharacterId(71))!.RunId, runs.Find(new CharacterId(72))!.RunId);
        else Assert.Null(runs.Find(new CharacterId(72)));
    }

    [Fact]
    public async Task Missing_prerequisite_and_practice_mode_reject_without_spending()
    {
        await using var f = Fixture();
        await f.SeedAsync();
        await SeedTicket(f, new(71), 3330, 20, 0);
        var (hell, runs) = Service(f, Rules with { RequiredQuestIds = [4391] });
        await using (var tx = f.Scope.Begin())
        {
            var result = await hell.EnterManualAsync(Context() with { Participants = [Party[0]] }, default);
            Assert.Equal(DungeonEntryFailure.HellQuestNotCleared, result.Failure);
            Assert.Equal(DungeonEntryFailure.HellUnavailable,
                (await hell.EnterManualAsync(Context() with { PracticeMode = 1 }, default)).Failure);
            await tx.CommitAsync(default);
        }
        Assert.Equal(20, await Count(f, new(71), 3330));
        Assert.Null(runs.Find(new CharacterId(71)));
        Assert.Equal(0, (await Fatigue(f).BalanceOfAsync(new(71), default)).Used);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Every_party_member_uses_live_cleared_quests_instead_of_leaders_entry_facts(bool everyoneCleared)
    {
        await using var f = Fixture();
        await f.SeedAsync();
        await SeedTicket(f, new(71), 3330, 20, 0);
        await SeedTicket(f, new(72), 3330, 20, 0);
        f.Sql("INSERT INTO character_invisible_flags(character_id,slot_index,flag_value) VALUES(71,4391,1);");
        if (everyoneCleared) f.Sql("INSERT INTO character_invisible_flags(character_id,slot_index,flag_value) VALUES(72,4391,1);");
        var (hell, runs) = Service(f, Rules with { RequiredQuestIds = [4391] });
        await using (var tx = f.Scope.Begin())
        {
            var result = await hell.EnterManualAsync(Context() with { Facts = new(0, [], [4391]) }, default);
            if (everyoneCleared) Assert.NotNull(result.Entry);
            else
            {
                Assert.Equal(DungeonEntryFailure.HellQuestNotCleared, result.Failure);
                Assert.Equal((byte)1, result.BlockedSlot);
            }
            await tx.CommitAsync(default);
        }
        Assert.Equal(everyoneCleared ? 0 : 20, await Count(f, new(71), 3330));
        Assert.Equal(everyoneCleared ? 0 : 20, await Count(f, new(72), 3330));
        Assert.Equal(everyoneCleared, runs.Find(new CharacterId(71)) is not null);
    }

    [Fact]
    public async Task Database_ticket_write_failure_rolls_back_fatigue_and_staged_run_together()
    {
        await using var f = Fixture();
        await f.SeedAsync();
        await SeedTicket(f, new(71), 3330, 20, 0);
        var (hell, runs) = Service(f);
        f.Sql("CREATE TRIGGER fail_ticket BEFORE DELETE ON item_slots BEGIN SELECT RAISE(ABORT,'hell test failure'); END;");
        await using (var tx = f.Scope.Begin())
        {
            await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(async () =>
                await hell.EnterManualAsync(Context() with { Participants = [Party[0]] }, default));
        }
        Assert.Equal(20, await Count(f, new(71), 3330));
        Assert.Equal(0, (await Fatigue(f).BalanceOfAsync(new(71), default)).Used);
        Assert.Null(runs.Find(new CharacterId(71)));
    }

    [Fact]
    public async Task Frozen_hell_room_survives_move_and_revisit_without_a_second_ticket_debit()
    {
        await using var f = Fixture();
        await f.SeedAsync();
        await SeedTicket(f, new(71), 3330, 40, 0);
        var (hell, runs) = Service(f);
        await using (var tx = f.Scope.Begin())
        {
            Assert.NotNull((await hell.EnterManualAsync(Context() with { Participants = [Party[0]] }, default)).Entry);
            await tx.CommitAsync(default);
        }
        var start = runs.Find(new CharacterId(71))!;
        var visits = new DungeonVisitDomainService(new Structures(), runs, new Selection(), new Cells(),
            f.Services.GetRequiredService<ICharacterDungeonDifficultyDomainService>(), new DungeonDifficultyRuleService(), Fatigue(f));
        await using (var tx = f.Scope.Begin())
        {
            var room = await visits.MoveAsync(new(71), start, start.Hell!.Cell, default);
            Assert.NotNull(room);
            Assert.Equal(900, room.Run.CurrentRoomMapId);
            Assert.Same(start.Hell, room.Run.Hell);
            await tx.CommitAsync(default);
        }
        await using (var tx = f.Scope.Begin())
        {
            Assert.NotNull(await visits.MoveAsync(new(71), runs.Find(new CharacterId(71))!, start.CurrentCell, default));
            await tx.CommitAsync(default);
        }
        await using (var tx = f.Scope.Begin())
        {
            var room = await visits.MoveAsync(new(71), runs.Find(new CharacterId(71))!, start.Hell!.Cell, default);
            Assert.NotNull(room);
            Assert.False(room.FirstEntry);
            Assert.Empty(room.Actors);
            Assert.Same(start.Hell, room.Run.Hell);
            await tx.CommitAsync(default);
        }
        Assert.Equal(20, await Count(f, new(71), 3330));
        Assert.Equal(2, (await Fatigue(f).BalanceOfAsync(new(71), default)).Used);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Disabled_or_invalid_room_configuration_rejects_without_debit(bool disabled)
    {
        await using var f = Fixture();
        await f.SeedAsync();
        await SeedTicket(f, new(71), 3330, 20, 0);
        var (hell, runs) = Service(f, disabled ? Rules with { Enabled = false } : Rules with { Rooms = new Dictionary<int, HellRoomDefinition>() });
        await using (var tx = f.Scope.Begin())
        {
            var result = await hell.EnterManualAsync(Context() with { Participants = [Party[0]] }, default);
            Assert.Equal(disabled ? DungeonEntryFailure.HellUnavailable : DungeonEntryFailure.HellConfigurationInvalid, result.Failure);
            await tx.CommitAsync(default);
        }
        Assert.Equal(20, await Count(f, new(71), 3330));
        Assert.Null(runs.Find(new CharacterId(71)));
        Assert.Equal(0, (await Fatigue(f).BalanceOfAsync(new(71), default)).Used);
    }

    private static readonly DungeonRunParticipant[] Party = [new(new(1), new(71), 0, true), new(new(2), new(72), 1, false)];
    private static readonly DungeonLayout Layout = new(150, 0, 2, 1, new(0, 0), new(1, 0), 800, 42,
        new Dictionary<MazeCell, DungeonCellDefinition> { [new(0, 0)] = DungeonCellDefinition.Fixed(800), [new(1, 0)] = DungeonCellDefinition.Fixed(801) }) { BasisLevel = 80 };
    private static readonly HellDungeonDefinition Rules = new(true, [], [new(4444, 1)], [3330],
        new Dictionary<int, HellRoomDefinition> { [0] = new(900, new(1, 0), [new(11, 100, 1)]) });
    private static DungeonEntryContext Context() => new(150, DungeonEntryFacts.None, 1, 0, Party);
    private static LearningFixture Fixture() => new(configure: services => services.AddSingleton<IItemPlacementDefinitionSource>(new Placements()));
    private static ICharacterFatigueDomainService Fatigue(LearningFixture f) => f.Services.GetRequiredService<ICharacterFatigueDomainService>();
    private static HellDungeonEntryRuleService RuleService(Content content) => new(content, new RoomActors());

    private static (IHellDungeonEntryDomainService, DungeonRunDomainService) Service(LearningFixture f, HellDungeonDefinition? rules = null)
    {
        var structures = new Structures();
        var runs = new DungeonRunDomainService(f.Scope);
        var visits = new DungeonVisitDomainService(structures, runs, new Selection(), new Cells(),
            f.Services.GetRequiredService<ICharacterDungeonDifficultyDomainService>(), new DungeonDifficultyRuleService(), Fatigue(f));
        return (new HellDungeonEntryDomainService(RuleService(new Content { Rules = rules ?? Rules }), structures, visits,
            f.Services.GetRequiredService<ICharacterDomainService>(), f.Services.GetRequiredService<Dfo.Server.Abstractions.Quests.ICharacterQuestLogDomainService>(),
            f.Services.GetRequiredService<ICharacterBusinessInventoryDomainService>(), f.Services.GetRequiredService<IItemLifecycleDomainService>(), f.Scope), runs);
    }
    private static void Register(LearningFixture f, CharacterId character)
    {
        var registry = f.Services.GetRequiredService<ICharacterContainerDomainService>();
        if (registry.FindContainer(character, InventoryContainerType.Main) is not null) return;
        registry.Register(character, new PersistentItemContainer(ContainerId.ForCharacter(character, InventoryContainerType.Main),
            new(InventoryContainerType.Main, 0), f.Services.GetRequiredService<IItemDomainStore>(), f.Scope,
            f.Services.GetRequiredService<IItemPlacementRuleService>(), f.Services.GetRequiredService<IItemSlotRuleService>()));
    }
    private static async Task SeedTicket(LearningFixture f, CharacterId character, int id, int count, int slot)
    {
        Register(f, character);
        var material = f.Services.GetRequiredService<ICharacterBusinessInventoryDomainService>().GetStore(character, BusinessInventoryStoreKind.Item).GetContainer(BusinessInventorySpace.Material);
        await using var tx = f.Scope.Begin();
        var item = await f.Services.GetRequiredService<IItemLifecycleDomainService>().CreateAsync(ItemSnapshot.FromCore(StackableItemCore.Create(3, id, count), true), default);
        await material.PutAsync(slot, item, default);
        await tx.CommitAsync(default);
    }
    private static async Task<int> Count(LearningFixture f, CharacterId character, int id) =>
        (await f.Services.GetRequiredService<ICharacterBusinessInventoryDomainService>().GetStore(character, BusinessInventoryStoreKind.Item)
            .GetContainer(BusinessInventorySpace.Material).GetAllAsync(default)).Where(x => x.ItemId == id).Sum(x => x.Count);
    private sealed class Placements : IItemPlacementDefinitionSource
    {
        public ValueTask<ItemPlacement?> GetAsync(int id, CancellationToken token) => ValueTask.FromResult<ItemPlacement?>(new(3, 1000, new(BusinessInventorySpace.Material, 0, 55)) { ItemId = id });
    }
    private sealed class Content : IHellDungeonDefinitionSource
    {
        public HellDungeonDefinition Rules { get; init; } = HellDungeonEntryTests.Rules;
        public IReadOnlyList<DungeonRoomActor> Actors { get; set; } = [new(1000, 0, 0)];
        public ValueTask<HellDungeonDefinition?> GetAsync(int id, CancellationToken token) => ValueTask.FromResult<HellDungeonDefinition?>(Rules);
        public ValueTask<HellPartyDefinition> GetPartyAsync(CancellationToken token) => ValueTask.FromResult(new HellPartyDefinition([new(1, 100)], [new(11, 1, Actors)]));
    }
    private sealed class RoomActors : IDungeonRoomActorSource
    {
        public ValueTask<IReadOnlyList<DungeonRoomActor>> GetAsync(int map, CancellationToken token) => ValueTask.FromResult<IReadOnlyList<DungeonRoomActor>>([]);
    }
    private sealed class Structures : IDungeonStructureRuleService
    {
        public ValueTask<DungeonDefinition?> GetDefinitionAsync(int id, CancellationToken token) => ValueTask.FromResult<DungeonDefinition?>(new(150, "test", 70, true, [], 80));
        public ValueTask<DungeonLayout?> ResolveAsync(int id, DungeonEntryFacts facts, int seed, CancellationToken token) => ValueTask.FromResult<DungeonLayout?>(Layout);
        public ValueTask<DungeonMapSelection> SelectMapAsync(DungeonLayout layout, MazeCell? from, MazeCell target, CancellationToken token) => ValueTask.FromResult(DungeonMapSelection.Selected(layout.Hell?.Cell == target ? layout.Hell.MapId : layout.Cells[target].MapCandidates[0]));
    }
    private sealed class Selection : IDungeonSelectionRuleService
    {
        public bool DefersSelectionState(byte level, bool tutorialCompleted = false) => false;
    }
    private sealed class Cells : IDungeonCellFactory
    {
        public ValueTask<DungeonCellSeed> CreateAsync(DungeonCellCreationContext context, CancellationToken token) => ValueTask.FromResult(DungeonCellSeed.CompleteEmpty(context.Cell, context.MapId));
    }
}
