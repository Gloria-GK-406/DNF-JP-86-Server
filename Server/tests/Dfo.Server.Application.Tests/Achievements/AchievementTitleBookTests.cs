using System.Buffers.Binary;
using Dfo.Server.Abstractions.Achievements;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Achievements;
using Dfo.Server.Application.DomainEvents;
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
using Xunit.Abstractions;

namespace Dfo.Server.Application.Tests.Achievements;

public sealed class AchievementTitleBookTests(ITestOutputHelper output)
{
    private static readonly CharacterId Character = new(1);
    [PvfFact]
    public async Task Real_rules_complete_from_verified_kills_and_grant_one_title_across_restart_and_repeated_delivery()
    {
        await using var f = new Fixture();
        var rules = f.Services.GetRequiredService<IAchievementRuleService>();
        var bookRules = f.Services.GetRequiredService<ITitleBookRuleService>();
        Assert.Equal(new[] {80,170,50,100,100}, bookRules.Catalog().Select(x => x.Capacity));
        var hunt = rules.Catalog().Single(x => x.Id == 4303);
        Assert.Equal(30, rules.InitialRemaining(hunt));
        output.WriteLine($"Rules: {rules.Catalog().Count}; supported: {rules.Catalog().Count(rules.IsSupported)}");
        foreach (var group in rules.Catalog().GroupBy(x => (x.Condition.Type, x.Condition.SubType, Supported: rules.IsSupported(x))))
            output.WriteLine($"{group.Key}: {group.Count()}");
        await f.WriteAsync(async () => await f.Achievements.PrepareAsync(Character,80,default));
        for (var i=0;i<30;i++) await f.SendAsync(new MonsterDefeated(Character,100,0,1,0));
        await f.DrainAsync();
        Assert.Equal(0, (await f.Achievements.GetAsync(Character,default)).Single(x=>x.AchievementId==4303).P1);
        Assert.Equal(0, (await f.Achievements.GetAsync(Character,default)).Single(x=>x.AchievementId==6501).P1);
        var titles = await f.Titles.GetAsync(Character,default);
        var granted = Assert.Single(titles.SelectMany(x=>x.Entries),x=>x.ItemId==26596);
        Assert.True(granted.Value>=0);
        var uid = Assert.Single(f.ItemUids(26596));
        await using var restored = new Fixture(f.Path);
        await restored.WriteAsync(async () => await restored.Titles.PrepareAsync(Character,default));
        await restored.WriteAsync(async () => await restored.Services.GetRequiredService<TitleBookDomainService>()
            .HandleAsync(new(Guid.NewGuid(),Guid.NewGuid(),DateTimeOffset.UtcNow,Guid.NewGuid(),new(Character,6501)),default));
        Assert.Equal(uid, Assert.Single(f.ItemUids(26596)));
    }
    [PvfFact]
    public async Task Rolled_back_facts_have_no_progress_and_duplicate_envelopes_do_not_count_twice()
    {
        await using var f = new Fixture();
        await f.WriteAsync(async () => await f.Achievements.PrepareAsync(Character,80,default));
        await using (var tx=f.Scope.Begin()) await f.Events.EnqueueAsync(new MonsterDefeated(Character,100,0,1,0));
        await f.DrainAsync();
        Assert.Equal(30,(await f.Achievements.GetAsync(Character,default)).Single(x=>x.AchievementId==4303).P1);
        var envelope = new DomainEventEnvelope<MonsterDefeated>(Guid.NewGuid(),Guid.NewGuid(),DateTimeOffset.UtcNow,Guid.NewGuid(),new(Character,100,0,1,0));
        for (var i=0;i<2;i++) await f.WriteAsync(async () => await f.Services.GetRequiredService<AchievementDomainService>().HandleAsync(envelope,default));
        Assert.Equal(29,(await f.Achievements.GetAsync(Character,default)).Single(x=>x.AchievementId==4303).P1);
        var before=f.Scalar("SELECT SUM(p1) FROM character_achievement_complete");
        await f.WriteAsync(async () => await f.Achievements.ReconcileAsync(Character,4303,default));
        for (var i=0;i<3;i++) { _=await f.Achievements.GetAsync(Character,default); _=await f.Titles.GetAsync(Character,default); }
        Assert.Equal(before,f.Scalar("SELECT SUM(p1) FROM character_achievement_complete"));
        Assert.Null(await f.WriteAsync(async () => await f.Achievements.ReconcileAsync(Character,int.MaxValue,default)));
    }
    [PvfFact]
    public async Task Complete_item_identity_and_attributes_survive_store_take_and_permanent_unlock_prevents_regrant()
    {
        await using var f=new Fixture();
        var slot=f.Services.GetRequiredService<ITitleBookRuleService>().Catalog().SelectMany(x=>x.Slots).Single(x=>x.QuestId==6501);
        await f.WriteAsync(async()=> await f.Achievements.PrepareAsync(Character,80,default));
        f.Sql("UPDATE character_achievement_complete SET p1=0 WHERE achievement_id=6501");
        var carried=new BusinessSlot(BusinessInventorySpace.CarriedEquipment,0);
        var worn=await f.Services.GetRequiredService<IItemPlacementRuleService>().WornSlotForAsync(26596,default);
        Assert.NotNull(worn);
        ItemSnapshot? original=null;
        await f.WriteAsync(async()=>
        {
            var session=f.Services.GetRequiredService<IItemDomainStore>().SessionIn(f.Scope.Current!);
            var record=await session.CreateAsync(new() {ItemId=26596,LegacyKind=1,Type=ItemTraits.Equipment,Attribute=12,EnchantCardId=123,Durability=47,AmplifyType=3,AmplifyValue=91},default);
            var put=await session.PutAsync(ContainerId.ForCharacter(Character,InventoryContainerType.Main),9,record,default);
            original=put.Snapshot;
        });
        Assert.True((await f.WriteAsync(async()=> await f.Titles.StoreAsync(Character,carried,26596,slot.Category,slot.Index,default))).Success);
        var refused=await f.WriteAsync(async()=> await f.Titles.WithdrawAsync(Character,carried,26596,slot.Category,slot.Index,default));
        Assert.False(refused.Success);
        Assert.True((await f.WriteAsync(async()=> await f.Titles.WithdrawAsync(Character,worn!.Value,26596,slot.Category,slot.Index,default))).Success);
        await f.WriteAsync(async()=>
        {
            var game=f.Services.GetRequiredService<IInventorySlotMappingRuleService>().ToGameSlot(worn!.Value);
            var snapshot=(await f.Services.GetRequiredService<IItemDomainStore>().SessionIn(f.Scope.Current!).GetAsync(ContainerId.ForCharacter(Character,game.Space),game.Index,default))!.Snapshot;
            Assert.Equal(original,snapshot);
            await f.Titles.PrepareAsync(Character,default);
        });
        Assert.Equal(1,f.Scalar("SELECT COUNT(*) FROM items"));
    }
    [PvfFact]
    public async Task Main_exchange_preserves_both_instances_and_equipment_occupied_refusal_preserves_all_positions()
    {
        await using var f=new Fixture();
        var rule=f.Services.GetRequiredService<ITitleBookRuleService>().Catalog().SelectMany(x=>x.Slots).Single(x=>x.QuestId==6501);
        await f.WriteAsync(async()=>await f.Achievements.PrepareAsync(Character,80,default));
        f.Sql("UPDATE character_achievement_complete SET p1=0 WHERE achievement_id=6501");
        var book=ContainerId.ForCharacter(Character,(InventoryContainerType)(19+rule.Category));
        var bag=ContainerId.ForCharacter(Character,InventoryContainerType.Main);
        var equip=ContainerId.ForCharacter(Character,InventoryContainerType.Equipment);
        var main=new BusinessSlot(BusinessInventorySpace.CarriedEquipment,0);
        var worn=new BusinessSlot(BusinessInventorySpace.WornEquipment,1);
        await f.WriteAsync(async()=>
        {
            var s=f.Services.GetRequiredService<IItemDomainStore>().SessionIn(f.Scope.Current!);
            async ValueTask Seed(ContainerId container,int index,byte attribute)
            { var item=await s.CreateAsync(new(){ItemId=26596,LegacyKind=1,Type=ItemTraits.Equipment,Attribute=attribute,EnchantCardId=attribute*10},default);await s.PutAsync(container,index,item,default); }
            await Seed(bag,9,12); await Seed(book,rule.Index,7); await Seed(equip,13,3);
        });
        var ids=f.ItemUids(26596);
        Assert.True((await f.WriteAsync(async()=>await f.Titles.StoreAsync(Character,main,26596,rule.Category,rule.Index,default))).Success);
        Assert.Equal((byte)12,Assert.Single((await f.Titles.GetAsync(Character,default)).SelectMany(x=>x.Entries)).Attribute);
        var before=f.ItemsAndPositions();
        Assert.False((await f.WriteAsync(async()=>await f.Titles.WithdrawAsync(Character,worn,26596,rule.Category,rule.Index,default))).Success);
        Assert.False((await f.WriteAsync(async()=>await f.Titles.StoreAsync(Character,worn,26596,rule.Category,rule.Index,default))).Success);
        Assert.Equal(before,f.ItemsAndPositions()); Assert.Equal(ids,f.ItemUids(26596));
    }
    [PvfTheory]
    [InlineData(1,0,0)]
    [InlineData(0,1,0)]
    [InlineData(0,0,2000000000)]
    public async Task Locked_or_period_title_cannot_be_stored_and_remains_unchanged(byte equipmentLock,byte sortLock,int expiry)
    {
        await using var f=new Fixture();
        var rule=f.Services.GetRequiredService<ITitleBookRuleService>().Catalog().SelectMany(x=>x.Slots).Single(x=>x.QuestId==6501);
        await f.WriteAsync(async()=>await f.Achievements.PrepareAsync(Character,80,default));
        f.Sql("UPDATE character_achievement_complete SET p1=0 WHERE achievement_id=6501");
        await f.WriteAsync(async()=>
        {
            var s=f.Services.GetRequiredService<IItemDomainStore>().SessionIn(f.Scope.Current!);
            var item=await s.CreateAsync(new(){ItemId=26596,LegacyKind=1,Type=ItemTraits.Equipment,EquipmentLockId=equipmentLock,SortLockFlag=sortLock,ExpireTime=expiry},default);
            await s.PutAsync(ContainerId.ForCharacter(Character,InventoryContainerType.Main),9,item,default);
        });
        var before=f.ItemsAndPositions();
        Assert.False((await f.WriteAsync(async()=>await f.Titles.StoreAsync(Character,new(BusinessInventorySpace.CarriedEquipment,0),26596,rule.Category,rule.Index,default))).Success);
        Assert.Equal(before,f.ItemsAndPositions());
    }
    [PvfFact]
    public async Task Legacy_multiple_counter_dependency_does_not_complete_a_dependent_title()
    {
        await using var f=new Fixture(definitions:new FakeDefinitions([
            new(9001,1,99,new("clearquest",-1,[77],1),[]),
            new(9002,1,99,new("clearquest",-1,[9001],1),[]) ]));
        await f.WriteAsync(async()=>await f.Achievements.PrepareAsync(Character,80,default));
        f.Sql("UPDATE character_achievement_complete SET p2=1 WHERE achievement_id=9001; INSERT INTO character_invisible_flags(character_id,slot_index,flag_value) VALUES(1,77,1)");
        await f.WriteAsync(async()=>await f.Achievements.ReconcileAsync(Character,9001,default));
        Assert.Equal(1,(await f.Achievements.GetAsync(Character,default)).Single(x=>x.AchievementId==9002).P1);
    }
    [PvfFact]
    public async Task Prerequisite_groups_are_or_of_and_and_unknown_restrictions_never_advance()
    {
        var definition=new AchievementDefinition(9001,1,99,new("huntmonster",-1,[-1,-1,-1,2],1),[]){PrerequisiteGroups=[[77,78],[79]]};
        await using var f=new Fixture(definitions:new FakeDefinitions([definition,definition with{Id=9002,SupportedRestrictions=false}]));
        f.Sql("INSERT INTO character_invisible_flags(character_id,slot_index,flag_value) VALUES(1,79,1)");
        await f.SendAsync(new MonsterDefeated(Character,100,0,1,0));await f.DrainAsync();
        Assert.Equal(1,(await f.Achievements.GetAsync(Character,default)).Single(x=>x.AchievementId==9001).P1);
        Assert.Equal(2,(await f.Achievements.GetAsync(Character,default)).Single(x=>x.AchievementId==9002).P1);
    }
    [PvfFact]
    public async Task Taking_a_title_cannot_bypass_ordinary_equipment_eligibility()
    {
        await using var f=new Fixture(wear:new RefuseWear());
        var rule=f.Services.GetRequiredService<ITitleBookRuleService>().Catalog().SelectMany(x=>x.Slots).Single(x=>x.QuestId==6501);
        await f.WriteAsync(async()=>await f.Achievements.PrepareAsync(Character,80,default));
        f.Sql("UPDATE character_achievement_complete SET p1=0 WHERE achievement_id=6501");
        await f.WriteAsync(async()=>await f.Titles.PrepareAsync(Character,default));
        var before=f.ItemsAndPositions();
        var result=await f.WriteAsync(async()=>await f.Titles.WithdrawAsync(Character,new(BusinessInventorySpace.WornEquipment,1),26596,rule.Category,rule.Index,default));
        Assert.False(result.Success);Assert.Equal("NotWearable",result.Reason);Assert.Equal(before,f.ItemsAndPositions());
    }
    [PvfTheory]
    [InlineData(4303)]
    [InlineData(6501)]
    public async Task Ordinary_quest_entry_points_cannot_accept_or_reward_achievement_owned_definitions(ushort questId)
    {
        await using var f = new Fixture();
        var definition = await f.Services.GetRequiredService<IQuestContentRuleService>().FindAsync(questId, default);
        Assert.NotNull(definition);
        var log = f.Services.GetRequiredService<ICharacterQuestLogDomainService>().LogOf(new(1), Character);
        var accepted = await f.WriteAsync(async () => await log.AcceptAsync(definition!, default));
        Assert.False(accepted.Accepted); Assert.Equal(QuestRefusal.ManagedByAchievement, accepted.Refusal);
        Assert.Empty(await log.CarriedAsync(default));
        // Existing legacy active records must not provide a second reward route either.
        f.Sql($"INSERT INTO character_active_quests(character_id,slot,quest_id,trigger_value) VALUES(1,0,{questId},0)");
        var before = f.ItemsAndPositions();
        var finish = await f.WriteAsync(async () => await f.Services.GetRequiredService<IQuestCompletionDomainService>().FinishAsync(new(1), Character, questId, ushort.MaxValue, 1, DateTimeOffset.UtcNow, default));
        Assert.False(finish.Finished); Assert.Equal(before, f.ItemsAndPositions());
        Assert.Single(await log.CarriedAsync(default));
    }
    [PvfFact]
    public async Task Open_event_slot_preserves_identity_and_closed_inventory_destination_never_moves_a_title()
    {
        await using var f = new Fixture();
        var slot = f.Services.GetRequiredService<ITitleBookRuleService>().Catalog().Single(x => x.Category == 4).Slots.First(x => x.SlotType >= 0 && x.QuestId == -1 && x.ItemIds.Contains(-1));
        var bag = ContainerId.ForCharacter(Character, InventoryContainerType.Main);
        var carried = new BusinessSlot(BusinessInventorySpace.CarriedEquipment, 0);
        await f.WriteAsync(async () => {
            var s = f.Services.GetRequiredService<IItemDomainStore>().SessionIn(f.Scope.Current!);
            var item = await s.CreateAsync(new() { ItemId = 26596, LegacyKind = 1, Type = ItemTraits.Equipment, Attribute = 12, Durability = 47 }, default);
            await s.PutAsync(bag, 9, item, default);
        });
        var uid = Assert.Single(f.ItemUids(26596));
        Assert.True((await f.WriteAsync(async () => await f.Titles.StoreAsync(Character, carried, 26596, slot.Category, slot.Index, default))).Success);
        var open = await f.Services.GetRequiredService<IInventoryCapacityDomainService>().OpenSlotCountAsync(bag, carried.Space, default);
        var before = f.ItemsAndPositions();
        var refused = await f.WriteAsync(async () => await f.Titles.WithdrawAsync(Character, new(carried.Space, open), 26596, slot.Category, slot.Index, default));
        Assert.False(refused.Success); Assert.Equal("InvalidDestination", refused.Reason); Assert.Equal(before, f.ItemsAndPositions());
        Assert.True((await f.WriteAsync(async () => await f.Titles.WithdrawAsync(Character, carried, 26596, slot.Category, slot.Index, default))).Success);
        Assert.Equal(uid, Assert.Single(f.ItemUids(26596)));
    }
    private sealed class RefuseWear : IEquipmentWearDomainService
    {public ValueTask<bool> CanWearAsync(CharacterId character,int itemId,CancellationToken ct)=>ValueTask.FromResult(false);}
    private sealed class FakeDefinitions(IReadOnlyList<AchievementDefinition> definitions) : IAchievementDefinitionSource
    {public IReadOnlyList<AchievementDefinition> Load()=>definitions;}
    private sealed class Fixture : IAsyncDisposable
    {
        private readonly bool owns;
        public string Path {get;}
        public ServiceProvider Services {get;}
        public DomainTransactionScope Scope=>Services.GetRequiredService<DomainTransactionScope>();
        public IEventService Events=>Services.GetRequiredService<IEventService>();
        public IAchievementDomainService Achievements=>Services.GetRequiredService<IAchievementDomainService>();
        public ITitleBookDomainService Titles=>Services.GetRequiredService<ITitleBookDomainService>();
        public Fixture(string? path=null,IAchievementDefinitionSource? definitions=null,IEquipmentWearDomainService? wear=null)
        {
            owns=path is null; Path=path??System.IO.Path.Combine(System.IO.Path.GetTempPath(),"achievement-"+Guid.NewGuid().ToString("N")+".db");
            var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"Database:Path",Path},{"Pvf:ArchivePath",Environment.GetEnvironmentVariable("PVF_ARCHIVE_PATH")}}).Build();
            var services=new ServiceCollection().AddLogging();
            new ApplicationModule().ConfigureServices(services,config);
            new InfrastructureModule().ConfigureServices(services,config);
            new PersistenceModule().ConfigureServices(services,config);
            if(definitions is not null)services.AddSingleton(definitions);
            if(wear is not null)services.AddSingleton(wear);
            Services=services.BuildServiceProvider();
            _=Services.GetRequiredService<SqliteDatabase>();
            if(owns) Sql("INSERT INTO accounts(account_id,m_id,password_hash) VALUES(1,'test','test'); INSERT INTO characters(character_id,account_id,slot_index,name,job,level,exp) VALUES(1,1,0,'test',0,80,0)");
            if(owns) Sql($"UPDATE characters SET exp={Services.GetRequiredService<IExperienceRuleService>().CharacterScale().ExperienceForLevel(80)} WHERE character_id=1");
        }
        public async ValueTask SendAsync<T>(T fact) where T:class,IDomainEvent => await WriteAsync(async()=> {await Events.EnqueueAsync(fact);});
        public async ValueTask WriteAsync(Func<ValueTask> action) {await using var tx=Scope.Begin(); await action(); await tx.CommitAsync(default);}
        public async ValueTask<T> WriteAsync<T>(Func<ValueTask<T>> action) {await using var tx=Scope.Begin(); var result=await action(); await tx.CommitAsync(default); return result;}
        public async ValueTask DrainAsync() {var dispatcher=Services.GetRequiredService<DomainEventDispatcher>(); for(var i=0;i<8;i++) if(await dispatcher.DrainOnceAsync()==0) break;}
        public IReadOnlyList<long> ItemUids(int itemId) {using var connection=new SqliteConnection("Data Source="+Path);connection.Open();using var command=connection.CreateCommand();command.CommandText="SELECT item_uid,item_core FROM items";using var reader=command.ExecuteReader();var result=new List<long>();while(reader.Read())if(BinaryPrimitives.ReadInt32LittleEndian(((byte[])reader[1]).AsSpan(1))==itemId)result.Add(reader.GetInt64(0));return result;}
        public string ItemsAndPositions() {using var connection=new SqliteConnection("Data Source="+Path);connection.Open();using var command=connection.CreateCommand();command.CommandText="SELECT i.item_uid,hex(i.item_core),s.owner_scope,s.owner_id,s.list_type,s.slot_index,s.count FROM items i LEFT JOIN item_slots s ON s.core_uid=i.item_uid ORDER BY i.item_uid";using var reader=command.ExecuteReader();var rows=new List<string>();while(reader.Read())rows.Add(string.Join('|',Enumerable.Range(0,reader.FieldCount).Select(reader.GetValue)));return string.Join('\n',rows);}
        public long Scalar(string sql) {using var connection=new SqliteConnection("Data Source="+Path);connection.Open();using var command=connection.CreateCommand();command.CommandText=sql;return Convert.ToInt64(command.ExecuteScalar());}
        public void Sql(string sql) {using var connection=new SqliteConnection("Data Source="+Path);connection.Open();using var command=connection.CreateCommand();command.CommandText=sql;command.ExecuteNonQuery();}
        public async ValueTask DisposeAsync() {await Services.DisposeAsync();if(!owns)return;using var connection=new SqliteConnection("Data Source="+Path);SqliteConnection.ClearPool(connection);foreach(var suffix in new[]{"","-wal","-shm"})File.Delete(Path+suffix);}
    }
}
