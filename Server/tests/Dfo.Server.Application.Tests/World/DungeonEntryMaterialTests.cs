using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Tests.Characters;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.World;

public sealed class DungeonEntryMaterialTests
{
    private static readonly DungeonRunParticipant[] Party = [new(new(1), new(71), 0, true), new(new(2), new(72), 1, false)];
    private static readonly DungeonLayout Layout = new(1500, 0, 1, 1, new(0, 0), new(0, 0), 800, 42,
        new Dictionary<MazeCell, DungeonCellDefinition> { [new(0,0)] = DungeonCellDefinition.Fixed(800) });
    private static readonly DungeonRequiredItem[] Requirements = [new(6337, 12, true), new(3332, 1, true), new(5555, 1, false)];
    private static LearningFixture Fixture() => new(configure: services =>
    {
        services.AddSingleton<IItemPlacementDefinitionSource>(new Placements());
        services.AddSingleton<ICharacterStatDefinitionSource>(new Dfo.Server.Application.Tests.Support.TestCharacterContent());
    });
    private static ICharacterFatigueDomainService Fatigue(LearningFixture f) => f.Services.GetRequiredService<ICharacterFatigueDomainService>();
    private static (DungeonVisitDomainService Visits, DungeonRunDomainService Runs) Service(LearningFixture f, bool throwCell = false)
    {
        var runs = new DungeonRunDomainService(f.Scope);
        return (Dfo.Server.Application.Tests.Support.DungeonVisitTestFactory.Create(new Structures(), runs, new Selection(), new Cells(throwCell),
            f.Services.GetRequiredService<ICharacterDungeonDifficultyDomainService>(), new DungeonDifficultyRuleService(), Fatigue(f),
            materials: f.Services.GetRequiredService<IDungeonEntryMaterialsDomainService>(),
            characters: f.Services.GetRequiredService<ICharacterDomainService>(), transactions: f.Scope), runs);
    }

    private static async Task SeedMember(LearningFixture f, CharacterId character, bool enough = true)
    {
        await SeedTicket(f, character, 6337, 7, 0);
        await SeedTicket(f, character, 6337, 8, 1);
        if (enough) await SeedTicket(f, character, 3332, 1, 2);
        await SeedTicket(f, character, 5555, 1, 3);
    }

    [Fact]
    public async Task Missing_material_on_second_member_refuses_without_spending_anyones_materials_or_fatigue()
    {
        await using var f = Fixture(); await f.SeedAsync();
        await SeedMember(f, new(71)); await SeedMember(f, new(72), false);
        var (visits, runs) = Service(f);
        await using (var tx = f.Scope.Begin())
        {
            var result = await visits.TryEnterPartyAsync(Party, 1500, DungeonEntryFacts.None, default);
            Assert.Equal(DungeonEntryFailure.MissingRequiredItem, result.Failure);
            Assert.Equal((byte)1, result.BlockedSlot);
            await tx.CommitAsync(default);
        }
        foreach (var member in Party)
        {
            Assert.Null(runs.Find(member.CharacterId));
            Assert.Equal(15, await Count(f, member.CharacterId, 6337));
            Assert.Equal(0, (await Fatigue(f).BalanceOfAsync(member.CharacterId, default)).Used);
        }
        Assert.Equal(1, await Count(f, new(71), 3332));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Admission_debits_split_stacks_once_preserves_nonconsumed_items_and_obeys_transaction(bool commit)
    {
        await using var f = Fixture(); await f.SeedAsync();
        await SeedMember(f, new(71)); await SeedMember(f, new(72));
        var (visits, runs) = Service(f);
        await using (var tx = f.Scope.Begin())
        {
            var result = await visits.TryEnterPartyAsync(Party, 1500, DungeonEntryFacts.None, default);
            Assert.NotNull(result.Entry);
            Assert.Equal(6, result.Entry.InventoryChanges.Count);
            Assert.Null(runs.Find(new CharacterId(71)));
            if (commit) await tx.CommitAsync(default);
        }
        foreach (var member in Party)
        {
            Assert.Equal(commit ? 3 : 15, await Count(f, member.CharacterId, 6337));
            Assert.Equal(commit ? 0 : 1, await Count(f, member.CharacterId, 3332));
            Assert.Equal(1, await Count(f, member.CharacterId, 5555));
            Assert.Equal(commit ? 1 : 0, (await Fatigue(f).BalanceOfAsync(member.CharacterId, default)).Used);
            Assert.Equal(commit, runs.Find(member.CharacterId) is not null);
        }
    }

    [Fact]
    public async Task Cell_creation_failure_does_not_debit_validated_materials()
    {
        await using var f = Fixture(); await f.SeedAsync(); await SeedMember(f, new(71));
        var (visits, runs) = Service(f, true);
        await using (var tx = f.Scope.Begin())
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await visits.TryEnterPartyAsync([Party[0]], 1500, DungeonEntryFacts.None, default));
        Assert.Equal(15, await Count(f, new(71), 6337)); Assert.Equal(1, await Count(f, new(71), 3332));
        Assert.Null(runs.Find(new CharacterId(71)));
        Assert.Equal(0, (await Fatigue(f).BalanceOfAsync(new(71), default)).Used);
    }

    [Fact]
    public async Task Material_database_failure_rolls_back_earlier_debits_fatigue_and_staged_run()
    {
        await using var f = Fixture(); await f.SeedAsync(); await SeedMember(f, new(71));
        var (visits, runs) = Service(f);
        f.Sql("CREATE TRIGGER fail_entry_material BEFORE DELETE ON item_slots BEGIN SELECT RAISE(ABORT,'entry material test failure'); END;");
        await using (var tx = f.Scope.Begin())
            await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(async () => await visits.TryEnterPartyAsync([Party[0]], 1500, DungeonEntryFacts.None, default));
        Assert.Equal(15, await Count(f, new(71), 6337)); Assert.Equal(1, await Count(f, new(71), 3332));
        Assert.Null(runs.Find(new CharacterId(71)));
        Assert.Equal(0, (await Fatigue(f).BalanceOfAsync(new(71), default)).Used);
    }

    [Theory]
    [InlineData(11, false)]
    [InlineData(15, true)]
    public async Task Account_shared_clear_cubes_are_checked_debited_and_projected_from_the_real_container(int available, bool success)
    {
        await using var f = Fixture(); await f.SeedAsync(); Register(f, new(71));
        var inventory = f.Services.GetRequiredService<ICharacterBusinessInventoryDomainService>();
        var shared = inventory.GetPersistentStore(new ContainerOwner(InventoryOwnerType.Account, 1), BusinessInventoryStoreKind.Item)
            .GetContainer(BusinessInventorySpace.SharedMaterial);
        await using (var tx = f.Scope.Begin())
        {
            await shared.AddAsync(3037, available, default); await tx.CommitAsync(default);
        }
        var runs = new DungeonRunDomainService(f.Scope);
        var visits = Dfo.Server.Application.Tests.Support.DungeonVisitTestFactory.Create(new Structures([new(3037, 12, true)]), runs, new Selection(), new Cells(false),
            f.Services.GetRequiredService<ICharacterDungeonDifficultyDomainService>(), new DungeonDifficultyRuleService(), Fatigue(f),
            materials: f.Services.GetRequiredService<IDungeonEntryMaterialsDomainService>(), transactions: f.Scope);
        await using (var tx = f.Scope.Begin())
        {
            var result = await visits.TryEnterPartyAsync([Party[0]], 1500, DungeonEntryFacts.None, default);
            if (success)
            {
                var change = Assert.Single(result.Entry!.InventoryChanges);
                Assert.Equal(BusinessInventorySpace.SharedMaterial, change.Slot.Space);
                var wire = f.Services.GetRequiredService<IInventorySlotProjectionDomainService>().Project(change.Slot);
                Assert.Equal(358, wire.Index);
                Assert.Equal(available - 12, change.Remaining!.Value);
            }
            else Assert.Equal(DungeonEntryFailure.MissingRequiredItem, result.Failure);
            await tx.CommitAsync(default);
        }
        Assert.Equal(success ? available - 12 : available, (await shared.GetAllAsync(default)).Single().Count);
        Assert.Equal(success, runs.Find(new CharacterId(71)) is not null);
    }

    [Fact]
    public async Task Minimum_level_has_its_own_refusal_before_material_check()
    {
        await using var f = Fixture(); await f.SeedAsync();
        await using (var tx = f.Scope.Begin())
        {
            await f.Services.GetRequiredService<ICharacterDomainService>().Get(new(72)).SetLevelAsync(84, default);
            await tx.CommitAsync(default);
        }
        var (visits, runs) = Service(f);
        await using (var tx = f.Scope.Begin())
        {
            var result = await visits.TryEnterPartyAsync(Party, 1500, DungeonEntryFacts.None, default);
            Assert.Equal(DungeonEntryFailure.LevelTooLow, result.Failure); Assert.Equal((byte)1, result.BlockedSlot);
            await tx.CommitAsync(default);
        }
        Assert.Null(runs.Find(new CharacterId(71)));
    }


    [Fact]
    public async Task Materials_are_charged_before_room_creation_and_creation_failure_rolls_back()
    {
        await using var f = Fixture(); await f.SeedAsync(); await SeedMember(f, new(71));
        var runs = new DungeonRunDomainService(f.Scope);
        var cells = new InspectingCells(async () =>
        {
            Assert.Equal(3, await Count(f, new(71), 6337));
            Assert.Equal(0, await Count(f, new(71), 3332));
            throw new InvalidOperationException("room creation failed after payment");
        });
        var visits = Dfo.Server.Application.Tests.Support.DungeonVisitTestFactory.Create(new Structures(), runs, new Selection(), cells,
            f.Services.GetRequiredService<ICharacterDungeonDifficultyDomainService>(), new DungeonDifficultyRuleService(), Fatigue(f),
            materials: f.Services.GetRequiredService<IDungeonEntryMaterialsDomainService>(), transactions: f.Scope);
        await using (var tx = f.Scope.Begin())
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => visits.TryEnterPartyAsync([Party[0]], 1500,
                DungeonEntryFacts.None, default).AsTask());
            Assert.Equal("room creation failed after payment", error.Message);
        }
        Assert.Equal(15, await Count(f, new(71), 6337));
        Assert.Equal(1, await Count(f, new(71), 3332));
        Assert.Null(runs.Find(new CharacterId(71)));
        Assert.Equal(0, (await Fatigue(f).BalanceOfAsync(new(71), default)).Used);
    }

    [Fact]
    public async Task Committed_ordinary_run_can_be_replaced_by_a_valid_retry()
    {
        await using var f = Fixture(); await f.SeedAsync();
        await SeedTicket(f, new(71), 6337, 30, 0); await SeedTicket(f, new(71), 3332, 2, 1); await SeedTicket(f, new(71), 5555, 1, 2);
        var runs = new DungeonRunDomainService(f.Scope);
        var admission = new DungeonAdmissionDomainService(new Structures(), f.Services.GetRequiredService<ICharacterDungeonDifficultyDomainService>(),
            new DungeonDifficultyRuleService(), Fatigue(f), f.Services.GetRequiredService<IDungeonEntryMaterialsDomainService>(), transactions: f.Scope, runs: runs);
        var visits = Dfo.Server.Application.Tests.Support.DungeonVisitTestFactory.Create(new Structures(), runs, new Selection(), new Cells(false),
            f.Services.GetRequiredService<ICharacterDungeonDifficultyDomainService>(), new DungeonDifficultyRuleService(), Fatigue(f),
            admission: admission);
        await using (var tx = f.Scope.Begin())
        {
            Assert.NotNull((await visits.TryEnterPartyAsync([Party[0]], 1500, DungeonEntryFacts.None, default)).Entry);
            await tx.CommitAsync(default);
        }
        var firstRun = runs.Find(new CharacterId(71))!.RunId;
        await using (var tx = f.Scope.Begin())
        {
            var attempt = await visits.TryEnterPartyAsync([Party[0]], 1500, DungeonEntryFacts.None, default);
            Assert.NotNull(attempt.Entry); Assert.NotEqual(firstRun, attempt.Entry.Visits[new(71)].Run.RunId);
            await tx.CommitAsync(default);
        }
        Assert.NotEqual(firstRun, runs.Find(new CharacterId(71))!.RunId);
        Assert.Equal(6, await Count(f, new(71), 6337)); Assert.Equal(0, await Count(f, new(71), 3332));
        Assert.Equal(2, (await Fatigue(f).BalanceOfAsync(new(71), default)).Used);
    }

    private static DungeonAdmissionDomainService Admission(LearningFixture f, IReadOnlyList<DungeonRequiredItem>? requirements = null) =>
        new(new Structures(requirements), f.Services.GetRequiredService<ICharacterDungeonDifficultyDomainService>(),
            new DungeonDifficultyRuleService(), Fatigue(f), f.Services.GetRequiredService<IDungeonEntryMaterialsDomainService>(),
            transactions: f.Scope);
    private static DungeonEntryContext Context(params DungeonRunParticipant[] members) => new(1500, DungeonEntryFacts.None, 0, 0, members);

    [Fact]
    public async Task Free_party_permits_have_no_cost_and_can_be_consumed_only_once()
    {
        await using var f = Fixture(); await f.SeedAsync();
        var admission = Admission(f, []);
        await using var tx = f.Scope.Begin();
        var decision = await admission.PrepareAsync(Context(Party), new(), default);
        Assert.Equal(2, decision.Batch!.Permits.Length);
        Assert.All(decision.Batch.Permits, permit => Assert.Empty(permit.Costs));
        var paid = await admission.ConsumeAsync(decision.Batch, default);
        Assert.NotNull(paid.Receipt); Assert.Empty(paid.Receipt.InventoryChanges);
        await Assert.ThrowsAsync<InvalidOperationException>(() => admission.ConsumeAsync(decision.Batch, default).AsTask());
        admission.BeginEntry(paid.Receipt, Party, 1500);
        Assert.Throws<InvalidOperationException>(() => admission.BeginEntry(paid.Receipt, Party, 1500));
        await tx.CommitAsync(default);
        Assert.Equal(0, (await Fatigue(f).BalanceOfAsync(new(71), default)).Used);
    }

    [Fact]
    public async Task Foreign_and_cross_transaction_permits_cannot_charge_materials()
    {
        await using var f = Fixture(); await f.SeedAsync(); await SeedMember(f, new(71));
        var admission = Admission(f); IDungeonAdmissionBatch batch;
        await using (var tx = f.Scope.Begin())
        {
            batch = (await admission.PrepareAsync(Context(Party[0]), new(), default)).Batch!;
            await Assert.ThrowsAsync<InvalidOperationException>(() => Admission(f).ConsumeAsync(batch, default).AsTask());
            await tx.CommitAsync(default);
        }
        await using (var tx = f.Scope.Begin())
            await Assert.ThrowsAsync<InvalidOperationException>(() => admission.ConsumeAsync(batch, default).AsTask());
        Assert.Equal(15, await Count(f, new(71), 6337));
    }

    [Fact]
    public async Task Changed_material_balance_is_rechecked_before_any_party_cost_is_paid()
    {
        await using var f = Fixture(); await f.SeedAsync(); await SeedMember(f, new(71)); await SeedMember(f, new(72));
        var admission = Admission(f);
        await using (var tx = f.Scope.Begin())
        {
            var decision = await admission.PrepareAsync(Context(Party), new(), default);
            var material = f.Services.GetRequiredService<ICharacterBusinessInventoryDomainService>().GetStore(new(72), BusinessInventoryStoreKind.Item)
                .GetContainer(BusinessInventorySpace.Material);
            var ticket = (await material.GetAllAsync(default)).Single(x => x.ItemId == 3332);
            await material.TakeAsync(ticket, null, default);
            var result = await admission.ConsumeAsync(decision.Batch!, default);
            Assert.Equal(DungeonEntryFailure.MissingRequiredItem, result.Refusal!.Failure);
            Assert.Equal((byte)1, result.Refusal.BlockedSlot);
            Assert.Equal(15, await Count(f, new(71), 6337));
            Assert.Equal(1, await Count(f, new(71), 3332));
        }
    }

    [Fact]
    public async Task Second_member_write_failure_rolls_back_first_member_even_if_caller_attempts_commit()
    {
        await using var f = Fixture(); await f.SeedAsync(); await SeedMember(f, new(71)); await SeedMember(f, new(72));
        // Failing on the second character occurs after the first participant's real SQL debits.
        f.Sql("CREATE TRIGGER fail_second_entry BEFORE DELETE ON item_slots WHEN OLD.owner_id=72 BEGIN SELECT RAISE(ABORT,'second member payment failure'); END;");
        var (visits, runs) = Service(f);
        await using (var tx = f.Scope.Begin())
        {
            await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() => visits.TryEnterPartyAsync(Party, 1500, DungeonEntryFacts.None, default).AsTask());
            Assert.True(f.Scope.Current!.IsRollbackOnly);
            await tx.CommitAsync(default);
        }
        foreach (var member in Party)
        {
            Assert.Equal(15, await Count(f, member.CharacterId, 6337));
            Assert.Equal(1, await Count(f, member.CharacterId, 3332));
            Assert.Null(runs.Find(member.CharacterId));
        }
    }

    [Fact]
    public async Task Ordinary_refusal_after_payment_rolls_back_even_if_caller_commits()
    {
        await using var f = Fixture(); await f.SeedAsync(); await SeedMember(f, new(71));
        var admission = Admission(f);
        await using (var tx = f.Scope.Begin())
        {
            var result = await admission.ExecuteAsync(Context(Party[0]), new(), (paid, ct) =>
                ValueTask.FromResult(DungeonEntryAttempt.Refused(DungeonEntryFailure.ConfigurationInvalid)), default);
            Assert.Equal(DungeonEntryFailure.ConfigurationInvalid, result.Failure);
            Assert.True(f.Scope.Current!.IsRollbackOnly);
            await tx.CommitAsync(default);
        }
        Assert.Equal(15, await Count(f, new(71), 6337));
        Assert.Equal(1, await Count(f, new(71), 3332));
    }

    private sealed class InspectingCells(Func<Task> inspect) : IDungeonCellFactory
    {
        public async ValueTask<DungeonCellSeed> CreateAsync(DungeonCellCreationContext context, CancellationToken token)
        {
            await inspect();
            throw new InvalidOperationException("inspection must throw");
        }
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

    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(1, 0, true)]
    [InlineData(0, 1, true)]
    [InlineData(1, 1, true)]
    public async Task Event_ticket_replaces_regular_ticket_and_only_selected_group_is_charged(int regular, int free, bool success)
    {
        await using var f = Fixture(); await f.SeedAsync();
        Register(f, new(71));
        if (regular > 0) await SeedTicket(f, new(71), 10003196, regular, 0);
        if (free > 0) await SeedTicket(f, new(71), 10003235, free, 1);
        var structures = new Structures([new(10003196, 1, true)], [new(10003235, 1, true)]);
        var admission = new DungeonAdmissionDomainService(structures,
            f.Services.GetRequiredService<ICharacterDungeonDifficultyDomainService>(), new DungeonDifficultyRuleService(), Fatigue(f),
            f.Services.GetRequiredService<IDungeonEntryMaterialsDomainService>(), transactions: f.Scope);
        await using (var tx = f.Scope.Begin())
        {
            var result = await admission.PrepareAsync(Context(Party[0]), new(), default);
            Assert.Equal(success, result.Batch is not null);
            if (success)
            {
                Assert.NotNull((await admission.ConsumeAsync(result.Batch!, default)).Receipt);
                await Assert.ThrowsAsync<InvalidOperationException>(() => admission.ConsumeAsync(result.Batch!, default).AsTask());
            }
            await tx.CommitAsync(default);
        }
        Assert.Equal(regular - (success && free == 0 ? 1 : 0), await Count(f, new(71), 10003196));
        Assert.Equal(free - (free > 0 ? 1 : 0), await Count(f, new(71), 10003235));
    }

    [Fact]
    public async Task Incomplete_event_group_falls_back_to_regular_and_entry_refusal_rolls_payment_back()
    {
        await using var f = Fixture(); await f.SeedAsync();
        await SeedTicket(f, new(71), 6337, 2, 0); await SeedTicket(f, new(71), 3332, 1, 1);
        var structures = new Structures([new(6337, 1, true)], [new(3332, 1, true), new(5555, 1, true)]);
        var admission = new DungeonAdmissionDomainService(structures,
            f.Services.GetRequiredService<ICharacterDungeonDifficultyDomainService>(), new DungeonDifficultyRuleService(), Fatigue(f),
            f.Services.GetRequiredService<IDungeonEntryMaterialsDomainService>(), transactions: f.Scope);
        await using (var tx = f.Scope.Begin())
        {
            Assert.NotNull((await admission.ConsumeAsync((await admission.PrepareAsync(Context(Party[0]), new(), default)).Batch!, default)).Receipt);
            await tx.CommitAsync(default);
        }
        Assert.Equal(1, await Count(f, new(71), 6337)); Assert.Equal(1, await Count(f, new(71), 3332));
        await using (var tx = f.Scope.Begin())
        {
            Assert.Null((await admission.ExecuteAsync(Context(Party[0]), new(), (_, _) =>
                ValueTask.FromResult(DungeonEntryAttempt.Refused(DungeonEntryFailure.ConfigurationInvalid)), default)).Entry);
            await tx.CommitAsync(default);
        }
        Assert.Equal(1, await Count(f, new(71), 6337)); Assert.Equal(1, await Count(f, new(71), 3332));
    }
    private sealed class Placements : IItemPlacementDefinitionSource
    {
        public ValueTask<ItemPlacement?> GetAsync(int id, CancellationToken token) => ValueTask.FromResult<ItemPlacement?>(new(AccountMaterialCatalog.ContainsItem(id) ? (byte)11 : (byte)3, 1000, new(BusinessInventorySpace.Material, 0, 55)) { ItemId = id });
    }

    private sealed class Structures(IReadOnlyList<DungeonRequiredItem>? requirements = null,
        IReadOnlyList<DungeonRequiredItem>? eventRequirements = null) : IDungeonStructureRuleService
    {
        public ValueTask<DungeonDefinition?> GetDefinitionAsync(int id, CancellationToken token) =>
            ValueTask.FromResult<DungeonDefinition?>(new(id, "test", 85, true, []) {
                RequiredItems = requirements ?? Requirements, EventRequiredItems = eventRequirements ?? [] });
        public ValueTask<DungeonLayout?> ResolveAsync(int id, DungeonEntryFacts facts, int seed, CancellationToken token) => ValueTask.FromResult<DungeonLayout?>(Layout);
        public ValueTask<DungeonMapSelection> SelectMapAsync(DungeonLayout layout, MazeCell? from, MazeCell target, CancellationToken token) => ValueTask.FromResult(DungeonMapSelection.Selected(800));
    }
    private sealed class Selection : IDungeonSelectionRuleService
    {
        public bool DefersSelectionState(byte level, bool tutorialCompleted = false) => false;
    }
    private sealed class Cells(bool fail) : IDungeonCellFactory
    {
        public ValueTask<DungeonCellSeed> CreateAsync(DungeonCellCreationContext context, CancellationToken token) => fail
            ? throw new InvalidOperationException("Cell creation failed.")
            : ValueTask.FromResult(DungeonCellSeed.CompleteEmpty(context.Cell, context.MapId));
    }
}
