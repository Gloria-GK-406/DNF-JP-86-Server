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
    [Theory]
    [InlineData("rules")]
    [InlineData("definition")]
    [InlineData("resolve")]
    [InlineData("prepare")]
    [InlineData("final-layout")]
    public async Task Invalid_hell_configuration_refuses_without_ticket_fatigue_or_run(string boundary)
    {
        await using var f = Fixture();
        await f.SeedAsync();
        await SeedTicket(f, new(71), 3330, 30, 0);
        var (hell, runs) = Service(f, failureBoundary: boundary);
        await using (var tx = f.Scope.Begin())
        {
            var result = await hell.EnterManualAsync(Context() with { Participants = [Party[0]] }, default);
            Assert.Equal(DungeonEntryFailure.HellConfigurationInvalid, result.Failure);
            Assert.NotNull(result.Detail);
            Assert.Null(result.Entry);
            await tx.CommitAsync(default);
        }
        Assert.Equal(30, await Count(f, new(71), 3330));
        Assert.Equal(0, (await Fatigue(f).BalanceOfAsync(new(71), default)).Used);
        Assert.Null(runs.Find(new CharacterId(71)));
    }

    [Fact]
    public async Task InvalidData_from_hell_cell_creation_remains_an_exception()
    {
        await using var f = Fixture();
        await f.SeedAsync();
        await SeedTicket(f, new(71), 3330, 30, 0);
        var (hell, runs) = Service(f, failureBoundary: "cell");
        await using (var tx = f.Scope.Begin())
        {
            var error = await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await hell.EnterManualAsync(Context() with { Participants = [Party[0]] }, default));
            Assert.Equal("unexpected hell cell failure", error.Message);
        }
        Assert.Equal(30, await Count(f, new(71), 3330));
        Assert.Equal(0, (await Fatigue(f).BalanceOfAsync(new(71), default)).Used);
        Assert.Null(runs.Find(new CharacterId(71)));
    }
    [Fact]
    public async Task Registered_CMD_commits_manual_entry_and_ticket_debit_without_an_outer_transaction()
    {
        await using var f = new LearningFixture(configure: services =>
        {
            new Dfo.Server.Protocol.ProtocolModule().ConfigureServices(services,
                new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
            services.AddSingleton<IItemPlacementDefinitionSource>(new Placements());
            services.AddSingleton<IDungeonStructureRuleService>(new Structures());
            services.AddSingleton<IDungeonDifficultyRuleService>(new Dfo.Server.Application.Tests.Support.TestDungeonDifficultyRules());
            services.AddSingleton<IHellDungeonDefinitionSource>(new Content());
            services.AddSingleton<IDespairTowerContentSource>(new NoDespairFloors());
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

    [SkippableTheory]
    [InlineData(72)]
    [InlineData(77)]
    public async Task Real_PVF_preparation_selects_a_declared_timegate_room_without_request_difficulty_override(int dungeonId)
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var content = ContentArchive.CreateContentProvider();
        var structures = new DungeonStructureRuleService(content.GetRequiredService<IDungeonDefinitionSource>(),
            content.GetRequiredService<IDungeonMapDefinitionSource>());
        var rules = new HellDungeonEntryRuleService(content.GetRequiredService<IHellDungeonDefinitionSource>(),
            content.GetRequiredService<IDungeonRoomActorSource>());
        var declarations = (await rules.GetRulesAsync(dungeonId, default))!;
        for (var seed = 0; seed < 20; seed++)
        {
            var layout = (await structures.ResolveTopologyAsync(dungeonId, DungeonEntryFacts.None, seed, default))!;
            var plan = await rules.PrepareAsync(declarations, layout, seed, default);
            Assert.NotNull(plan);
            Assert.Contains(plan.Cell, layout.Cells.Keys);
            Assert.Equal(declarations.Rooms[layout.MazeIndex].MapId, plan.MapId);
            Assert.NotEmpty(plan.Actors);
            Assert.All(plan.Actors, x => Assert.InRange(x.Actor.Level, (byte)1, byte.MaxValue));
            Assert.Equal(plan.Actors, (await rules.PrepareAsync(declarations, layout, seed, default))!.Actors);
            var frozen = await structures.PrepareLayoutAsync(
                layout with { EntryKind = DungeonEntryKind.ManualHell, Hell = plan }, default);
            Assert.Equal(plan.MapId, (await structures.SelectMapAsync(frozen, null, plan.Cell, default)).MapId);
        }
    }

    [Theory]
    [InlineData(24, false)]
    [InlineData(30, true)]
    public async Task Ordinary_and_hell_costs_sharing_an_item_are_reserved_together_and_rebind_split_stacks(int total, bool success)
    {
        await using var f = Fixture(); await f.SeedAsync();
        await SeedTicket(f, new(71), 3330, 10, 0);
        await SeedTicket(f, new(71), 3330, total - 10, 1);
        var (hell, runs) = Service(f, required: [new(3330, 5, true)]);
        await using (var tx = f.Scope.Begin())
        {
            var result = await hell.EnterManualAsync(Context() with { Participants = [Party[0]] }, default);
            if (success) Assert.NotNull(result.Entry);
            else Assert.Equal(DungeonEntryFailure.MissingHellTicket, result.Failure);
            await tx.CommitAsync(default);
        }
        Assert.Equal(success ? total - 25 : total, await Count(f, new(71), 3330));
        Assert.Equal(success, runs.Find(new CharacterId(71)) is not null);
    }

    [Fact]
    public async Task Enough_hell_tickets_but_missing_a_different_ordinary_material_preserves_ordinary_refusal()
    {
        await using var f = Fixture(); await f.SeedAsync(); await SeedTicket(f, new(71), 3330, 30, 0);
        var (hell, runs) = Service(f, required: [new(5555, 1, true)]);
        await using (var tx = f.Scope.Begin())
        {
            var result = await hell.EnterManualAsync(Context() with { Participants = [Party[0]] }, default);
            Assert.Equal(DungeonEntryFailure.MissingRequiredItem, result.Failure);
            Assert.Equal((byte)0, result.BlockedSlot);
            await tx.CommitAsync(default);
        }
        Assert.Equal(30, await Count(f, new(71), 3330)); Assert.Null(runs.Find(new CharacterId(71)));
    }

    [Theory]
    [InlineData(3330, DungeonEntryFailure.MissingHellTicket)]
    [InlineData(5555, DungeonEntryFailure.MissingRequiredItem)]
    public async Task Payment_recheck_preserves_which_kind_of_material_became_insufficient(int removedItem, DungeonEntryFailure expected)
    {
        await using var f = Fixture(); await f.SeedAsync();
        await SeedTicket(f, new(71), 3330, 30, 0); await SeedTicket(f, new(71), 5555, 1, 1);
        var admission = new DungeonAdmissionDomainService(new Structures([new(5555, 1, true)]),
            f.Services.GetRequiredService<ICharacterDungeonDifficultyDomainService>(), new DungeonDifficultyRuleService(), Fatigue(f),
            f.Services.GetRequiredService<IDungeonEntryMaterialsDomainService>(), transactions: f.Scope);
        await using (var tx = f.Scope.Begin())
        {
            var prepared = await admission.PrepareAsync(Context() with { Participants = [Party[0]] },
                new() { HellTickets = [new(3330, 20)] }, default);
            Assert.NotNull(prepared.Batch);
            var material = f.Services.GetRequiredService<ICharacterBusinessInventoryDomainService>().GetStore(new(71), BusinessInventoryStoreKind.Item)
                .GetContainer(BusinessInventorySpace.Material);
            await material.TakeAsync((await material.GetAllAsync(default)).Single(x => x.ItemId == removedItem), null, default);
            var result = await admission.ConsumeAsync(prepared.Batch, default);
            Assert.Equal(expected, result.Refusal!.Failure);
        }
        Assert.Equal(30, await Count(f, new(71), 3330)); Assert.Equal(1, await Count(f, new(71), 5555));
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
        var visits = Dfo.Server.Application.Tests.Support.DungeonVisitTestFactory.Create(new Structures(), runs, new Selection(), new Cells(),
            f.Services.GetRequiredService<ICharacterDungeonDifficultyDomainService>(), new DungeonDifficultyRuleService(), Fatigue(f), materials: f.Services.GetRequiredService<IDungeonEntryMaterialsDomainService>(), transactions: f.Scope);
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
    private static LearningFixture Fixture() => new(configure: services =>
    {
        services.AddSingleton<IItemPlacementDefinitionSource>(new Placements());
        services.AddSingleton<IDungeonDifficultyRuleService>(new Dfo.Server.Application.Tests.Support.TestDungeonDifficultyRules());
    });
    private static ICharacterFatigueDomainService Fatigue(LearningFixture f) => f.Services.GetRequiredService<ICharacterFatigueDomainService>();
    private static HellDungeonEntryRuleService RuleService(Content content) => new(content, new RoomActors());

    private static (IHellDungeonEntryDomainService, DungeonRunDomainService) Service(LearningFixture f, HellDungeonDefinition? rules = null, IReadOnlyList<DungeonRequiredItem>? required = null, string? failureBoundary = null)
    {
        var structures = new Structures(required, failureBoundary);
        var runs = new DungeonRunDomainService(f.Scope);
        var admission = new DungeonAdmissionDomainService(structures,
            f.Services.GetRequiredService<ICharacterDungeonDifficultyDomainService>(), new DungeonDifficultyRuleService(), Fatigue(f),
            f.Services.GetRequiredService<IDungeonEntryMaterialsDomainService>(), f.Services.GetRequiredService<ICharacterDomainService>(),
            quests: f.Services.GetRequiredService<Dfo.Server.Abstractions.Quests.ICharacterQuestLogDomainService>(), transactions: f.Scope);
        var visits = new DungeonVisitDomainService(structures, runs, new Selection(), new Cells(failureBoundary), Fatigue(f), admission);
        return (new HellDungeonEntryDomainService(new FaultingHellRules(RuleService(new Content { Rules = rules ?? Rules }), failureBoundary),
            structures, visits, admission), runs);
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
    private sealed class NoDespairFloors : IDespairTowerContentSource
    {
        public DespairTowerCatalog Load() => new(70, 3, []);
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
    private sealed class FaultingHellRules(IHellDungeonEntryRuleService inner, string? boundary) : IHellDungeonEntryRuleService
    {
        public ValueTask<HellDungeonDefinition?> GetRulesAsync(int id, CancellationToken token) =>
            boundary == "rules" ? throw new InvalidDataException("invalid hell rules") : inner.GetRulesAsync(id, token);
        public ValueTask<HellEntryPlan?> PrepareAsync(HellDungeonDefinition rules, DungeonLayout layout, int seed, CancellationToken token) =>
            boundary == "prepare" ? throw new InvalidDataException("invalid hell plan") : inner.PrepareAsync(rules, layout, seed, token);
        public IReadOnlyList<HellTicketAlternative> TicketAlternatives(HellDungeonDefinition rules, byte level) => inner.TicketAlternatives(rules, level);
    }
    private sealed class Structures(IReadOnlyList<DungeonRequiredItem>? required = null, string? failureBoundary = null) : IDungeonStructureRuleService
    {
        public ValueTask<DungeonDefinition?> GetDefinitionAsync(int id, CancellationToken token) => failureBoundary == "definition"
            ? throw new InvalidDataException("invalid test maze") : ValueTask.FromResult<DungeonDefinition?>(new(150, "test", 70, true, [], 80) { RequiredItems = required ?? [] });
        public ValueTask<DungeonLayout?> ResolveAsync(int id, DungeonEntryFacts facts, int seed, CancellationToken token) => failureBoundary == "resolve"
            ? throw new InvalidDataException("invalid test maze") : ValueTask.FromResult<DungeonLayout?>(Layout);
        public ValueTask<DungeonLayout> PrepareLayoutAsync(DungeonLayout layout, CancellationToken token)
        {
            Assert.Equal(DungeonEntryKind.ManualHell, layout.EntryKind);
            Assert.NotNull(layout.Hell);
            return failureBoundary == "final-layout" ? throw new InvalidDataException("invalid final hell layout")
                : ValueTask.FromResult(layout);
        }
        public ValueTask<DungeonMapSelection> SelectMapAsync(DungeonLayout layout, MazeCell? from, MazeCell target, CancellationToken token) => ValueTask.FromResult(DungeonMapSelection.Selected(layout.Hell?.Cell == target ? layout.Hell.MapId : layout.Cells[target].MapCandidates[0]));
    }
    private sealed class Selection : IDungeonSelectionRuleService
    {
        public bool DefersSelectionState(byte level, bool tutorialCompleted = false) => false;
    }
    private sealed class Cells(string? failureBoundary = null) : IDungeonCellFactory
    {
        public ValueTask<DungeonCellSeed> CreateAsync(DungeonCellCreationContext context, CancellationToken token) => failureBoundary == "cell"
            ? throw new InvalidDataException("unexpected hell cell failure") : ValueTask.FromResult(DungeonCellSeed.CompleteEmpty(context.Cell, context.MapId));
    }
}
