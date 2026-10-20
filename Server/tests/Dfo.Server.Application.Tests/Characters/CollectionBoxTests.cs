using System.Buffers.Binary;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Infrastructure;
using Dfo.Server.Persistence;
using Dfo.Server.Persistence.Database;
using Dfo.Server.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Characters;

public sealed class CollectionBoxTests
{
    private static readonly CharacterId Character = new(1);
    [Fact]
    public async Task Current_state_can_read_a_consistent_snapshot_outside_a_command_without_persisting_defaults()
    {
        await using var f = new Fixture(synthetic: true);
        var before = f.Dump();
        Assert.Equal(1, (await f.Boxes.ReadAsync(Character, default)).SelectedBoxId);
        Assert.Equal(before, f.Dump());
        Assert.Null(f.Scope.Current);
    }
    [Fact]
    public async Task New_series_and_selection_use_the_same_persistence_without_cross_character_leakage()
    {
        await using var f = new Fixture(synthetic: true);
        await f.Write(async () => { await f.Boxes.ExecuteAsync(new(1), Character, CollectionBoxAction.Select, 4, null, 0, default); });
        var reloaded = ActivatorUtilities.CreateInstance<CollectionBoxDomainService>(f.Services);
        await f.Write(async () =>
        {
            Assert.Equal(4, (await reloaded.ReadAsync(Character, default)).SelectedBoxId);
            Assert.Equal(1, (await reloaded.ReadAsync(new(2), default)).SelectedBoxId);
            Assert.Equal(new byte[] { 1, 4 }, (await reloaded.ReadAsync(Character, default)).Boxes.Select(x => x.BoxId));
        });
    }
    [Fact]
    public async Task Conflicting_definitions_and_legacy_rows_preserve_assets_and_do_not_invent_instances()
    {
        await using var f = new Fixture(synthetic: true);
        f.Sql("INSERT INTO character_collectbox_slots VALUES(1,1,0,12345)");
        await f.Write(async () =>
        {
            var s = f.Items;
            await s.PutAsync(ContainerId.ForCharacter(Character, InventoryContainerType.CollectionBox), 4 * 256,
                await s.CreateAsync(new() { ItemId = 9999, IsPile = true, LegacyKind = 6, Type = ItemTraitClassification.For(6, 9999) }, default), default);
        });
        var before = f.Dump();
        await f.Write(async () =>
        {
            var state = await f.Boxes.ReadAsync(Character, default);
            Assert.Equal(2, state.Diagnostics.Length);
            Assert.All(state.Boxes, box => Assert.Empty(box.ItemIds));
        });
        await Assert.ThrowsAsync<CollectionBoxRejectedException>(async () => await f.Write(async () =>
            { await f.Boxes.ExecuteAsync(new(1), Character, CollectionBoxAction.Put, 1, new(BusinessInventorySpace.Consumable, 0), 10007322, default); }));
        Assert.Equal(before, f.Dump());
    }
    [Fact]
    public async Task Duplicate_items_holes_and_out_of_range_series_are_rejected_by_catalog_validation()
    {
        foreach (var definition in new[] {
            new CollectionBoxDefinition(256,1,1,null,null,[new(0,0,1,0)],[]),
            new CollectionBoxDefinition(1,2,1,null,null,[new(0,0,1,0),new(1,0,1,0)],[]),
            new CollectionBoxDefinition(1,1,1,null,null,[new(1,0,1,0)],[]) })
            await Assert.ThrowsAsync<InvalidDataException>(async () => await new CollectionBoxRuleService(new Definitions([definition])).CatalogAsync(default));
        var catalog = await new CollectionBoxRuleService(new Definitions([new(1,2,1,null,null,[new(0,0,1,0),new(1,0,-1,0)],[])])).CatalogAsync(default);
        Assert.Single(catalog);
    }

    [PvfTheory]
    [InlineData(1,10007322)] [InlineData(2,10007548)] [InlineData(3,490000282)]
    public async Task Real_catalog_piles_round_trip_without_reconstructing_any_item_fields(byte box, int itemId)
    {
        await using var f = new Fixture();
        var (source, original) = await f.Seed(itemId, 3);
        await f.Write(async () => { await f.Boxes.ExecuteAsync(new(1), Character, CollectionBoxAction.Select, box, null, 0, default); });
        await f.Write(async () =>
        {
            var added = await f.Boxes.ExecuteAsync(new(1), Character, CollectionBoxAction.Put, box, source, itemId, default);
            Assert.Equal(2, Assert.Single(added.Inventory).Item!.Count);
            Assert.Contains(itemId, added.State.Boxes.Single(x => x.BoxId == box).ItemIds);
        });
        await Assert.ThrowsAsync<CollectionBoxRejectedException>(async () => await f.Write(async () =>
            { await f.Boxes.ExecuteAsync(new(1), Character, CollectionBoxAction.Put, box, source, itemId, default); }));
        await f.Write(async () =>
        {
            var removed = await f.Boxes.ExecuteAsync(new(1), Character, CollectionBoxAction.Withdraw, box, null, itemId, default);
            Assert.Equal(original, Assert.Single(removed.Inventory).Item);
            Assert.Empty(removed.State.Boxes.Single(x => x.BoxId == box).ItemIds);
        });
    }
    [PvfFact]
    public async Task Failure_after_transfer_rolls_back_items_and_selection_in_the_same_database_transaction()
    {
        await using var f = new Fixture();
        var (source, _) = await f.Seed(10007322, 1);
        var before = f.Dump();
        f.Sql("CREATE TRIGGER fail_selection BEFORE INSERT ON character_collectbox_state BEGIN SELECT RAISE(ABORT,'injected'); END");
        await Assert.ThrowsAsync<SqliteException>(async () => await f.Write(async () =>
            { await f.Boxes.ExecuteAsync(new(1), Character, CollectionBoxAction.Put, 1, source, 10007322, default); }));
        Assert.Equal(before, f.Dump());
    }
    [PvfFact]
    public async Task Full_bag_refuses_withdrawal_and_preserves_the_original_item_without_mail_fallback()
    {
        await using var f = new Fixture();
        var (source, _) = await f.Seed(10007322, 1);
        await f.Write(async () => { await f.Boxes.ExecuteAsync(new(1), Character, CollectionBoxAction.Put, 1, source, 10007322, default); });
        await f.Write(async () =>
        {
            var p = (await f.Services.GetRequiredService<IItemPlacementRuleService>().PlacementForAsync(10007322, default))!;
            var template = await f.Services.GetRequiredService<IItemDeliveryRuleService>().TemplateAsync(10007548, default);
            var mapping = new InventorySlotMappingRuleService();
            foreach (var slot in Enumerable.Range(p.Destination.FirstIndex, p.Destination.LastIndex - p.Destination.FirstIndex + 1)
                .Select(i => new BusinessSlot(p.Destination.Space, i)).Concat(Enumerable.Range(0,6).Select(i => new BusinessSlot(BusinessInventorySpace.QuickSlot,i))))
                await f.Items.PutAsync(ContainerId.ForCharacter(Character, InventoryContainerType.Main), mapping.ToGameSlot(slot).Index,
                    await f.Items.CreateAsync(template with { Count = 1000 }, default), default);
        });
        var before = f.Dump();
        var error = await Assert.ThrowsAsync<ContainerAdditionException>(async () => await f.Write(async () =>
            { await f.Boxes.ExecuteAsync(new(1), Character, CollectionBoxAction.Withdraw, 1, null, 10007322, default); }));
        Assert.Equal(ContainerAdditionFailure.NoRoom, error.Reason);
        Assert.Equal(before, f.Dump());
    }
    [PvfTheory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public async Task Timed_or_locked_source_and_permanent_extension_never_consume_items(int kind)
    {
        await using var f = new Fixture();
        var (source, _) = await f.Seed(10007322, 2, kind);
        var before = f.Dump();
        await Assert.ThrowsAsync<CollectionBoxRejectedException>(async () => await f.Write(async () =>
            { await f.Boxes.ExecuteAsync(new(1), Character, CollectionBoxAction.Put, 1, source, 10007322, default); }));
        await Assert.ThrowsAsync<CollectionBoxRejectedException>(async () => await f.Write(async () =>
            { await f.Boxes.ExecuteAsync(new(1), Character, CollectionBoxAction.Extend, 1, source, 10007322, default); }));
        Assert.Equal(before, f.Dump());
    }

    private sealed class Definitions(IReadOnlyList<CollectionBoxDefinition> catalog) : ICollectionBoxDefinitionSource
    {
        public ValueTask<IReadOnlyList<CollectionBoxDefinition>> GetAllAsync(CancellationToken ct) => ValueTask.FromResult(catalog);
        public ValueTask<CollectionBoxDefinition?> GetAsync(int id, CancellationToken ct) => ValueTask.FromResult(catalog.SingleOrDefault(x => x.BoxIndex == id));
    }
    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string path = Path.Combine(Path.GetTempPath(), $"collection-{Guid.NewGuid():N}.db");
        public ServiceProvider Services { get; }
        public DomainTransactionScope Scope => Services.GetRequiredService<DomainTransactionScope>();
        public IItemDomainStoreSession Items => Services.GetRequiredService<IItemDomainStore>().SessionIn(Scope.Current!);
        public ICollectionBoxDomainService Boxes => Services.GetRequiredService<ICollectionBoxDomainService>();
        public Fixture(bool synthetic = false)
        {
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
                ["Database:Path"] = path, ["Pvf:ArchivePath"] = Environment.GetEnvironmentVariable("PVF_ARCHIVE_PATH") }).Build();
            var services = new ServiceCollection().AddLogging();
            new ApplicationModule().ConfigureServices(services, config);
            new InfrastructureModule().ConfigureServices(services, config);
            new PersistenceModule().ConfigureServices(services, config);
            if (synthetic) services.AddSingleton<ICollectionBoxDefinitionSource>(new Definitions([
                new(1,1,1,null,null,[new(0,0,10007322,0)],[]), new(4,1,1,null,null,[new(0,0,10007548,0)],[])]));
            Services = services.BuildServiceProvider();
            _ = Services.GetRequiredService<SqliteDatabase>();
            Sql("INSERT INTO accounts(account_id,m_id,password_hash) VALUES(1,'test','test'); INSERT INTO characters(character_id,account_id,slot_index,name) VALUES(1,1,0,'one'),(2,1,1,'two')");
            Services.GetRequiredService<ICharacterContainerDomainService>().Register(Character,
                new PersistentItemContainer(ContainerId.ForCharacter(Character,InventoryContainerType.Main),new(InventoryContainerType.Main,0),
                    Services.GetRequiredService<IItemDomainStore>(),Services.GetRequiredService<IDomainTransactionAccessor>(),
                    Services.GetRequiredService<IItemPlacementRuleService>(),Services.GetRequiredService<IItemSlotRuleService>()));
        }
        public async ValueTask Write(Func<ValueTask> action) { await using var tx = Scope.Begin(); await action(); await tx.CommitAsync(default); }
        public async ValueTask<(BusinessSlot,ItemSnapshot)> Seed(int itemId, int count, int invalid = 0)
        {
            var p = (await Services.GetRequiredService<IItemPlacementRuleService>().PlacementForAsync(itemId, default))!;
            var source = new BusinessSlot(p.Destination.Space,p.Destination.FirstIndex);
            var snapshot = (await Services.GetRequiredService<IItemDeliveryRuleService>().TemplateAsync(itemId, default)) with
            { Count = count, SealFlag = 1, EnchantCardId = 321, TradeRestriction = 2, TailUnknown1 = 9,
                ExpireTime = invalid == 1 ? 1 : 0, SortLockFlag = invalid == 2 ? (byte)1 : (byte)0, EquipmentLockId = invalid == 3 ? (byte)1 : (byte)0 };
            await Write(async () => { await Items.PutAsync(ContainerId.ForCharacter(Character, InventoryContainerType.Main),
                new InventorySlotMappingRuleService().ToGameSlot(source).Index, await Items.CreateAsync(snapshot, default), default); });
            return (source,snapshot);
        }
        public void Sql(string sql) { using var c = new SqliteConnection("Data Source="+path); c.Open(); using var q = c.CreateCommand(); q.CommandText=sql; q.ExecuteNonQuery(); }
        public string Dump()
        {
            using var c = new SqliteConnection("Data Source="+path); c.Open(); using var q = c.CreateCommand();
            q.CommandText="SELECT i.item_uid,hex(i.item_core),s.owner_id,s.list_type,s.slot_index,s.count FROM items i LEFT JOIN item_slots s ON s.core_uid=i.item_uid ORDER BY i.item_uid";
            using var r=q.ExecuteReader(); var rows=new List<string>(); while(r.Read()) rows.Add(string.Join('|', Enumerable.Range(0,r.FieldCount).Select(r.GetValue)));
            r.Close(); q.CommandText="SELECT character_id,selected_box_index FROM character_collectbox_state ORDER BY character_id";
            using var selected=q.ExecuteReader(); while(selected.Read()) rows.Add($"selected:{selected.GetInt32(0)}:{selected.GetInt32(1)}"); return string.Join('\n',rows);
        }
        public async ValueTask DisposeAsync() { await Services.DisposeAsync(); SqliteConnection.ClearAllPools(); foreach(var suffix in new[]{"","-wal","-shm"}) File.Delete(path+suffix); }
    }
}
