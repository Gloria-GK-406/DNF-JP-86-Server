using Dfo.Server.Application.Tests.Support;
using System.Buffers.Binary;
using Dfo.Server.Abstractions.Achievements;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Settlement;
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

using Dfo.Server.Application.Inventory;

namespace Dfo.Server.Application.Tests.Achievements;

public sealed partial class AchievementTitleBookTests(ITestOutputHelper output)
{
    private static readonly CharacterId Character = new(1);
    [PvfTheory]
    [InlineData(69,false)]
    [InlineData(70,true)]
    public async Task Level_title_uses_reached_threshold_instead_of_requiring_a_new_level_event(int level,bool expected)
    {
        await using var f=new Fixture(definitions:new FakeDefinitions([new(6531,10,99,new("level up",-1,[70],1),[])]));
        f.Sql($"UPDATE characters SET level={level} WHERE character_id=1");
        Assert.Equal(expected,(await f.WriteAsync(async()=>await f.Achievements.UnlockSatisfiedAchievementsAsync(Character,default))).Contains(6531));
    }
    [PvfFact]
    public async Task Archive_snapshot_support_has_79_bound_titles_and_excludes_unknown_duel_and_clone_conditions()
    {
        await using var f=new Fixture();
        var rules=f.Services.GetRequiredService<IAchievementRuleService>();
        var slots=f.Services.GetRequiredService<ITitleBookRuleService>().Catalog().SelectMany(x=>x.Slots).Where(x=>x.SlotType>=0).Select(x=>x.QuestId).ToHashSet();
        Assert.Equal(79,rules.Catalog().Count(x=>slots.Contains(x.Id)&&rules.CanCheckSnapshot(x)));
        Assert.False(rules.CanCheckSnapshot(rules.Catalog().Single(x=>x.Id==6663)));
        Assert.False(rules.CanCheckSnapshot(rules.Catalog().Single(x=>x.Id==6582)));
    }
    [PvfFact]
    public async Task Equipped_item_unlock_reads_pvf_grade_and_level_at_the_worn_slot()
    {
        await using var f=new Fixture(definitions:new FakeDefinitions([new(6567,10,99,new("equipped item",0,[3,55],1),[]){EquipmentSlots=["weapon"]}]));
        await f.WriteAsync(async()=>{
            var s=f.Services.GetRequiredService<IItemDomainStore>().SessionIn(f.Scope.Current!);
            var item=await s.CreateAsync(new(){ItemId=2747466,LegacyKind=1,Type=ItemTraits.Equipment},default);
            await s.PutAsync(ContainerId.ForCharacter(Character,InventoryContainerType.Equipment),12,item,default);
        });
        Assert.Contains(6567,await f.WriteAsync(async()=>await f.Achievements.UnlockSatisfiedAchievementsAsync(Character,default)));
    }
    [PvfFact]
    public async Task Existing_level_unlocks_once_and_does_not_grant_hardcore_titles_to_ordinary_characters()
    {
        await using var f = new Fixture();
        f.Sql("UPDATE characters SET level=85 WHERE character_id=1");
        await f.WriteAsync(async () => await f.Services.GetRequiredService<Dfo.Server.Abstractions.Persistence.ICharacterConditionTable>().InsertIfAbsentAsync(Character, default));
        var completed = await f.WriteAsync(async () => await f.Achievements.UnlockSatisfiedAchievementsAsync(Character, default));
        Assert.Contains(6531, completed); Assert.Contains(6697, completed); Assert.DoesNotContain(6721, completed);
        await f.DrainAsync();
        Assert.Single(f.ItemUids(26628)); Assert.Single(f.ItemUids(100331240)); Assert.Empty(f.ItemUids(100331297));
        var before = f.Scalar("SELECT COUNT(*) FROM domain_events");
        Assert.Empty(await f.WriteAsync(async () => await f.Achievements.UnlockSatisfiedAchievementsAsync(Character, default)));
        await f.WriteAsync(async () => await f.Titles.PrepareAsync(Character, default));
        Assert.Equal(before, f.Scalar("SELECT COUNT(*) FROM domain_events"));
        Assert.Single(f.ItemUids(26628));
    }
    [PvfFact]
    public async Task Hardcore_career_and_level_checks_respect_mode_and_reconcile_dependencies_in_one_pass()
    {
        await using var f = new Fixture();
        f.Sql("UPDATE characters SET level=85,grow_type=17 WHERE character_id=1");
        await f.WriteAsync(async () => await f.Services.GetRequiredService<Dfo.Server.Abstractions.Persistence.ICharacterConditionTable>().InsertIfAbsentAsync(Character, default));
        f.Sql("UPDATE character_condition SET is_hardcore_mode=1 WHERE character_id=1");
        Assert.True((await f.Services.GetRequiredService<IAchievementCharacterFactsDomainService>().ReadAsync(Character,default)).Hardcore);
        var completed = await f.WriteAsync(async () => await f.Achievements.UnlockSatisfiedAchievementsAsync(Character, default));
        Assert.Contains(6719, completed); Assert.Contains(6720, completed); Assert.Contains(6721, completed);
        Assert.DoesNotContain(6697, completed);
        await f.DrainAsync();
        Assert.Single(f.ItemUids(100331295)); Assert.Single(f.ItemUids(100331296)); Assert.Single(f.ItemUids(100331297));
        Assert.Empty(f.ItemUids(100331240));
    }
    [PvfFact]
    public async Task Possession_checks_do_not_consume_items_and_equipment_checks_require_actual_worn_items()
    {
        await using var f = new Fixture();
        await f.WriteAsync(async () => {
            var s = f.Services.GetRequiredService<IItemDomainStore>().SessionIn(f.Scope.Current!);
            foreach (var (id, slot) in new[] {(7463,65), (7464,66)})
            {
                var item = await s.CreateAsync(new(){ItemId=id, Count=1000, IsPile=true, LegacyKind=2, Type=ItemTraits.Consumable}, default);
                await s.PutAsync(ContainerId.ForCharacter(Character,InventoryContainerType.Main),slot,item,default);
            }
            var weapon = await s.CreateAsync(new(){ItemId=21009, Count=1, LegacyKind=1, Type=ItemTraits.Equipment}, default);
            await s.PutAsync(ContainerId.ForCharacter(Character,InventoryContainerType.Main),9,weapon,default);
        });
        var before = f.ItemsAndPositions();
        var completed = await f.WriteAsync(async () => await f.Achievements.UnlockSatisfiedAchievementsAsync(Character, default));
        Assert.Contains(6580, completed); Assert.DoesNotContain(6567, completed);
        Assert.Equal(before, f.ItemsAndPositions());
        await f.DrainAsync();
        Assert.Single(f.ItemUids(26688));
    }
    [PvfFact]
    public async Task Activated_or_completed_rules_do_not_read_character_snapshot_and_queries_do_not_unlock()
    {
        var facts = new RefuseSnapshot();
        await using var f = new Fixture(definitions:new FakeDefinitions([
            new(6531,1,99,new("level up",-1,[70],1),[]),
            new(6697,1,99,new("level up",-1,[85],1),[])]), facts:facts);
        f.Sql("INSERT INTO title_book_unlocks(character_id,category,slot_index,item_id) VALUES(1,0,46,26628); INSERT INTO character_achievement_complete(character_id,sort_order,achievement_id,p1,p2,p3,p4) VALUES(1,0,6697,0,0,0,0)");
        _ = await f.Achievements.GetAsync(Character, default); _ = await f.Titles.GetAsync(Character, default);
        Assert.Empty(await f.WriteAsync(async () => await f.Achievements.UnlockSatisfiedAchievementsAsync(Character, default)));
        Assert.Empty(f.ItemUids(26628)); Assert.Equal(0, facts.Reads);
    }
    [PvfFact]
    public async Task Permanently_activated_title_satisfies_dependencies_without_rechecking_or_recompleting_its_achievement()
    {
        await using var f = new Fixture(definitions:new FakeDefinitions([
            new(6531,1,99,new("level up",-1,[70],1),[]),
            new(9002,1,99,new("clear quest",-1,[6531],1),[])]));
        f.Sql("INSERT INTO title_book_unlocks(character_id,category,slot_index,item_id) VALUES(1,0,46,26628)");
        Assert.Equal(new[]{9002},await f.WriteAsync(async()=>await f.Achievements.UnlockSatisfiedAchievementsAsync(Character,default)));
        await f.WriteAsync(async()=>await f.Achievements.PrepareAsync(Character,80,default));
        Assert.DoesNotContain(await f.Achievements.GetAsync(Character,default),x=>x.AchievementId==6531);
        Assert.Empty(f.ItemUids(26628));
    }
    [PvfFact]
    public async Task Snapshot_completion_rolls_back_without_events_or_rewards_and_follows_dependency_chain()
    {
        await TestGameplay.RunAsync(async () =>
        {
            await using var f = new Fixture(definitions:new FakeDefinitions([
                new(9002,1,99,new("clear quest",-1,[9001],1),[]),
                new(9001,1,99,new("level up",-1,[70],1),[])]));
            await using (var tx=f.Scope.Begin()) Assert.Equal(2,(await f.Achievements.UnlockSatisfiedAchievementsAsync(Character,default)).Count);
            Assert.Empty(await f.Achievements.GetAsync(Character,default)); Assert.Equal(0,f.Scalar("SELECT COUNT(*) FROM domain_events"));
            Assert.Equal(new[]{9001,9002},await f.WriteAsync(async()=>await f.Achievements.UnlockSatisfiedAchievementsAsync(Character,default)));
        });
    }
    private sealed class RefuseSnapshot : IAchievementCharacterFactsDomainService
    {
        public int Reads {get;private set;}
        public ValueTask<AchievementCharacterFacts> ReadForUnlockAsync(CharacterId character,IReadOnlyList<AchievementDefinition> pending,CancellationToken ct)
            => ReadAsync(character,ct);
        public ValueTask<AchievementCharacterFacts> ReadAsync(CharacterId character,CancellationToken ct)
        {Reads++; throw new InvalidOperationException("Completed rules must not load a snapshot.");}
    }
    [PvfFact]
    public async Task Rolled_back_facts_have_no_progress_and_duplicate_envelopes_do_not_count_twice()
    {
        await using var f = new Fixture(definitions:new FakeDefinitions([new(9001,1,99,new("huntmonster",-1,[-1,-1,-1,30],1),[])]));
        await f.WriteAsync(async () => await f.Achievements.PrepareAsync(Character,80,default));
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            await using (var tx=f.Scope.Begin()) await f.Events.EnqueueAsync(new MonsterDefeated(Character,100,0,1,0));
        });
        await f.DrainAsync();
        Assert.Equal(30,(await f.Achievements.GetAsync(Character,default)).Single(x=>x.AchievementId==9001).P1);
        var envelope = new DomainEventEnvelope<MonsterDefeated>(Guid.NewGuid(),Guid.NewGuid(),DateTimeOffset.UtcNow,Guid.NewGuid(),new(Character,100,0,1,0));
        for (var i=0;i<2;i++) await f.WriteAsync(async () => await f.Services.GetRequiredService<AchievementDomainService>().HandleAsync(envelope,default));
        Assert.Equal(29,(await f.Achievements.GetAsync(Character,default)).Single(x=>x.AchievementId==9001).P1);
        var before=f.Scalar("SELECT SUM(p1) FROM character_achievement_complete");
        await f.WriteAsync(async () => await f.Achievements.ReconcileAsync(Character,9001,default));
        for (var i=0;i<3;i++) { _=await f.Achievements.GetAsync(Character,default); _=await f.Titles.GetAsync(Character,default); }
        Assert.Equal(before,f.Scalar("SELECT SUM(p1) FROM character_achievement_complete"));
        Assert.Null(await f.WriteAsync(async () => await f.Achievements.ReconcileAsync(Character,int.MaxValue,default)));
    }
    [PvfTheory]
    [InlineData((byte)0)]
    [InlineData((byte)1)]
    public async Task Complete_item_identity_and_attributes_survive_store_take_and_permanent_unlock_prevents_regrant(byte sealFlag)
    {
        await using var f=new Fixture();
        var slot=f.Services.GetRequiredService<ITitleBookRuleService>().Catalog().SelectMany(x=>x.Slots).Single(x=>x.QuestId==6501);
        await f.WriteAsync(async()=> await f.Achievements.PrepareAsync(Character,80,default));
        f.Sql("INSERT INTO character_invisible_flags(character_id,slot_index,flag_value) VALUES(1,4303,1); UPDATE character_achievement_complete SET p1=0 WHERE achievement_id=6501");
        var carried=new BusinessSlot(BusinessInventorySpace.CarriedEquipment,0);
        var worn=await f.Services.GetRequiredService<IItemPlacementRuleService>().WornSlotForAsync(26596,default);
        Assert.NotNull(worn);
        ItemSnapshot? original=null;
        await f.WriteAsync(async()=>
        {
            var session=f.Services.GetRequiredService<IItemDomainStore>().SessionIn(f.Scope.Current!);
            var record=await session.CreateAsync(new() {ItemId=26596,LegacyKind=1,Type=ItemTraits.Equipment,SealFlag=sealFlag,Attribute=12,EnchantCardId=123,Durability=47,AmplifyType=3,AmplifyValue=91},default);
            var put=await session.PutAsync(ContainerId.ForCharacter(Character,InventoryContainerType.Main),9,record,default);
            original=put.Snapshot;
        });
        Assert.True((await f.WriteAsync(async()=> await f.Titles.StoreAsync(Character,carried,26596,slot.Category,slot.Index,default))).Success);
        var refused=await f.WriteAsync(async()=> await f.Titles.WithdrawAsync(Character,carried,26596,slot.Category,slot.Index,default));
        Assert.False(refused.Success);
        Assert.True((await f.WriteAsync(async()=> await f.Titles.WithdrawAsync(Character,worn!.Value,26596,slot.Category,slot.Index,default))).Success);
        await f.WriteAsync(async()=>
        {
            var game=A21InventoryLayout.ToGameSlot(worn!.Value);
            var snapshot=(await f.Services.GetRequiredService<IItemDomainStore>().SessionIn(f.Scope.Current!).GetAsync(ContainerId.ForCharacter(Character,game.Space),game.Index,default))!.Snapshot;
            Assert.True(snapshot.IsMirror);
            Assert.Equal(original!.ItemUid, snapshot.MirrorSourceUid);
            Assert.NotEqual(original.ItemUid, snapshot.ItemUid);
            Assert.Equal(original with { SealFlag = 0, IsMirror = true, MirrorSourceUid = original.ItemUid, ItemUid = snapshot.ItemUid },snapshot);
            var book = (await f.Services.GetRequiredService<IItemDomainStore>().SessionIn(f.Scope.Current!).GetAsync(ContainerId.ForCharacter(Character,(InventoryContainerType)(19+slot.Category)),slot.Index,default))!.Snapshot;
            Assert.Equal(original with { SealFlag = 0 },book);
            await f.Titles.PrepareAsync(Character,default);
        });
        Assert.True((await f.WriteAsync(async()=> await f.Titles.StoreAsync(Character,worn!.Value,26596,slot.Category,slot.Index,default))).Success);
        var stored = Assert.Single((await f.Titles.GetAsync(Character,default)).SelectMany(x=>x.Entries), x=>x.ItemId==26596);
        Assert.Equal((byte)0, stored.SealFlag);
        await using var restored = new Fixture(f.Path);
        Assert.Equal((byte)0, Assert.Single((await restored.Titles.GetAsync(Character,default)).SelectMany(x=>x.Entries),x=>x.ItemId==26596).SealFlag);
        Assert.Equal(1,f.Scalar("SELECT COUNT(*) FROM items"));
    }
    [PvfFact]
    public async Task Main_exchange_preserves_instances_and_equipping_a_book_title_returns_real_equipment_to_the_bag()
    {
        await using var f=new Fixture();
        var rule=f.Services.GetRequiredService<ITitleBookRuleService>().Catalog().SelectMany(x=>x.Slots).Single(x=>x.QuestId==6501);
        await f.WriteAsync(async()=>await f.Achievements.PrepareAsync(Character,80,default));
        f.Sql("INSERT INTO character_invisible_flags(character_id,slot_index,flag_value) VALUES(1,4303,1); UPDATE character_achievement_complete SET p1=0 WHERE achievement_id=6501");
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
        Assert.True((await f.WriteAsync(async()=>await f.Titles.WithdrawAsync(Character,worn,26596,rule.Category,rule.Index,default))).Success);
        Assert.Equal(1,f.Scalar("SELECT COUNT(*) FROM items WHERE is_mirror=1"));
        Assert.All(ids,id=>Assert.Contains(id,f.ItemUids(26596)));
        Assert.Equal((byte)12, Assert.Single((await f.Titles.GetAsync(Character,default)).SelectMany(x=>x.Entries)).Attribute);
        Assert.Equal((byte)12, (await ReadTitle(f, book, rule.Index))!.Attribute);
        Assert.True((await f.WriteAsync(async()=>await f.Titles.StoreAsync(Character,worn,26596,rule.Category,rule.Index,default))).Success);
        Assert.Equal(0,f.Scalar("SELECT COUNT(*) FROM items WHERE is_mirror=1"));
        Assert.Equal(ids,f.ItemUids(26596));
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
        f.Sql("INSERT INTO character_invisible_flags(character_id,slot_index,flag_value) VALUES(1,4303,1); UPDATE character_achievement_complete SET p1=0 WHERE achievement_id=6501");
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
        f.Sql("INSERT INTO character_invisible_flags(character_id,slot_index,flag_value) VALUES(1,4303,1); UPDATE character_achievement_complete SET p1=0 WHERE achievement_id=6501");
        await f.WriteAsync(async()=>await f.Titles.PrepareAsync(Character,default));
        var before=f.ItemsAndPositions();
        var result=await f.WriteAsync(async()=>await f.Titles.WithdrawAsync(Character,new(BusinessInventorySpace.WornEquipment,1),26596,rule.Category,rule.Index,default));
        Assert.False(result.Success);Assert.Equal("NotWearable",result.Reason);Assert.Equal(before,f.ItemsAndPositions());
    }
    [PvfTheory]
    [InlineData(6501)]
    public async Task Ordinary_quest_entry_points_cannot_accept_or_reward_passive_title_definitions(ushort questId)
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
        var finish = await f.WriteAsync(async () => await TestEffectFootprints.Run(new Dfo.Server.Abstractions.Effects.EffectFootprintOwner(new(1), Character, null), cap => f.Services.GetRequiredService<IQuestCompletionSettlementDomainService>().FinishAsync(new(1), Character, questId, ushort.MaxValue, 1, DateTimeOffset.UtcNow, cap, default)));
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
            var item = await s.CreateAsync(new() { ItemId = 26596, LegacyKind = 1, Type = ItemTraits.Equipment, SealFlag = 1, Attribute = 12, Durability = 47 }, default);
            await s.PutAsync(bag, 9, item, default);
        });
        var uid = Assert.Single(f.ItemUids(26596));
        var original = f.ItemsAndPositions();
        Assert.True((await f.WriteAsync(async () => await f.Titles.StoreAsync(Character, carried, 26596, slot.Category, slot.Index, default))).Success);
        var open = await f.Services.GetRequiredService<IInventoryCapacityDomainService>().OpenSlotCountAsync(bag, carried.Space, default);
        var before = f.ItemsAndPositions();
        var refused = await f.WriteAsync(async () => await f.Titles.WithdrawAsync(Character, new(carried.Space, open), 26596, slot.Category, slot.Index, default));
        Assert.False(refused.Success); Assert.Equal("InvalidDestination", refused.Reason); Assert.Equal(before, f.ItemsAndPositions());
        Assert.True((await f.WriteAsync(async () => await f.Titles.WithdrawAsync(Character, carried, 26596, slot.Category, slot.Index, default))).Success);
        Assert.Equal(uid, Assert.Single(f.ItemUids(26596)));
        Assert.Equal(original, f.ItemsAndPositions());
    }
    [PvfTheory]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    [InlineData(false, true, false)]
    public async Task Equipping_a_sealed_title_unseals_only_the_incoming_instance_and_rolls_back_with_the_move(bool reverse, bool occupied, bool commit)
    {
        await TestGameplay.RunAsync(async () =>
        {
            await using var f = new Fixture();
            var bag = ContainerId.ForCharacter(Character, InventoryContainerType.Main);
            var wornContainer = ContainerId.ForCharacter(Character, InventoryContainerType.Equipment);
            var worn = (await f.Services.GetRequiredService<IItemPlacementRuleService>().WornSlotForAsync(26596, default))!.Value;
            var game = A21InventoryLayout.ToGameSlot(worn);
            ItemSnapshot? original = null, previous = null;
            await f.WriteAsync(async () => {
                var session = f.Services.GetRequiredService<IItemDomainStore>().SessionIn(f.Scope.Current!);
                var item = await session.CreateAsync(new() { ItemId=26596, LegacyKind=1, Type=ItemTraits.Equipment, SealFlag=1, Attribute=12, EnchantCardId=123, Durability=47, AmplifyType=3, AmplifyValue=91 }, default);
                original = (await session.PutAsync(bag,9,item,default)).Snapshot;
                if (occupied) {
                    var old = await session.CreateAsync(new() { ItemId=26596, LegacyKind=1, Type=ItemTraits.Equipment, Attribute=8, Durability=23 },default);
                    previous = (await session.PutAsync(wornContainer,game.Index,old,default)).Snapshot;
                }
            });
            var before = f.ItemsAndPositions();
            foreach (var type in new[] { InventoryContainerType.Main, InventoryContainerType.Equipment })
                f.Services.GetRequiredService<ICharacterContainerDomainService>().Register(Character,
                    new Dfo.Server.Application.Inventory.PersistentItemContainer(ContainerId.ForCharacter(Character,type),new(type,0),
                        f.Services.GetRequiredService<IItemDomainStore>(),f.Services.GetRequiredService<IDomainTransactionAccessor>(),
                        f.Services.GetRequiredService<IItemPlacementRuleService>(),f.Services.GetRequiredService<IItemSlotRuleService>()));
            var handler = new Dfo.Server.Application.Inventory.InventoryMoveDomainService(
                f.Services.GetRequiredService<IBusinessInventorySlotDomainService>(), f.Services.GetRequiredService<IItemMovementDomainService>());
            var carried = new BusinessSlot(BusinessInventorySpace.CarriedEquipment,0);
            await using (var tx = f.Scope.Begin()) {
                var result = await handler.MoveAsync(
                    new(Character,reverse ? worn : carried,1,reverse ? carried : worn),default);
                Assert.Equal(Dfo.Server.Abstractions.Inventory.ItemMoveStatus.Moved,result.Status);
                if (commit) await tx.CommitAsync(default);
            }
            if (!commit) { Assert.Equal(before,f.ItemsAndPositions()); return; }
            await f.WriteAsync(async () => {
                var session = f.Services.GetRequiredService<IItemDomainStore>().SessionIn(f.Scope.Current!);
                Assert.Equal(original! with { SealFlag=0 },(await session.GetAsync(wornContainer,game.Index,default))!.Snapshot);
                Assert.Equal(previous,(await session.GetAsync(bag,9,default))?.Snapshot);
            });
            var slot = f.Services.GetRequiredService<ITitleBookRuleService>().Catalog().Single(x=>x.Category==4).Slots.First(x=>x.SlotType>=0 && x.QuestId==-1 && x.ItemIds.Contains(-1));
            Assert.True((await f.WriteAsync(async () => await f.Titles.StoreAsync(Character,worn,26596,slot.Category,slot.Index,default))).Success);
            Assert.Equal((byte)0, Assert.Single((await f.Titles.GetAsync(Character,default)).SelectMany(x=>x.Entries),x=>x.ItemId==26596).SealFlag);
        });
    }
    [PvfTheory]
    [InlineData((byte)1,(byte)1,(byte)0)]
    [InlineData((byte)6,(byte)1,(byte)0)]
    [InlineData((byte)5,(byte)1,(byte)1)]
    [InlineData((byte)8,(byte)1,(byte)1)]
    [InlineData((byte)1,(byte)0,(byte)0)]
    [InlineData((byte)1,(byte)2,(byte)2)]
    public async Task Equip_preparation_preserves_other_item_state_and_does_not_unseal_creature_or_avatar_fields(byte kind, byte seal, byte expectedSeal)
    {
        await using var f = new Fixture();
        var original = new ItemSnapshot { ItemId=26596, LegacyKind=kind, SealFlag=seal, Attribute=12, Durability=47, EnchantCardId=123, AmplifyType=3, AmplifyValue=91 };
        Assert.Equal(original with { SealFlag=expectedSeal }, f.Services.GetRequiredService<IEquipmentWearDomainService>().UnsealForEquip(original));
    }
    private sealed class RefuseWear : IEquipmentWearDomainService
    {
        public ItemSnapshot UnsealForEquip(ItemSnapshot item) => throw new InvalidOperationException("Rejected equip must not unseal.");
        public ValueTask<bool> CanWearAsync(CharacterId character,int itemId,CancellationToken ct)=>ValueTask.FromResult(false);
    }
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
        public Fixture(string? path=null,IAchievementDefinitionSource? definitions=null,IEquipmentWearDomainService? wear=null,IAchievementCharacterFactsDomainService? facts=null)
        {
            owns=path is null; Path=path??System.IO.Path.Combine(System.IO.Path.GetTempPath(),"achievement-"+Guid.NewGuid().ToString("N")+".db");
            var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"Database:Path",Path},{"Pvf:ArchivePath",Environment.GetEnvironmentVariable("PVF_ARCHIVE_PATH")}}).Build();
            var services=new ServiceCollection().AddLogging();
            new ApplicationModule().ConfigureServices(services,config);
            new InfrastructureModule().ConfigureServices(services,config);
            new PersistenceModule().ConfigureServices(services,config);
            if(definitions is not null)services.AddSingleton(definitions);
            if(wear is not null)services.AddSingleton(wear);
            if(facts is not null)services.AddSingleton(facts);
            Services=services.BuildServiceProvider();
            _=Services.GetRequiredService<SqliteDatabase>();
            if(owns) Sql("INSERT INTO accounts(account_id,m_id,password_hash) VALUES(1,'test','test'); INSERT INTO characters(character_id,account_id,slot_index,name,job,level,exp) VALUES(1,1,0,'test',0,80,0)");
            if(owns) Sql($"UPDATE characters SET exp={Services.GetRequiredService<IExperienceRuleService>().CharacterScale().ExperienceForLevel(80)} WHERE character_id=1");
        }
        public async ValueTask SendAsync<T>(T fact) where T:class,IDomainEvent => await WriteAsync(async()=> {await Events.EnqueueAsync(fact);});
        public async ValueTask WriteAsync(Func<ValueTask> action) {await TestGameplay.RunAsync(Services, async () => {await using var tx=Scope.Begin(); await action(); await tx.CommitAsync(default);});}
        public async ValueTask<T> WriteAsync<T>(Func<ValueTask<T>> action) {return await TestGameplay.RunAsync(Services, async () => {await using var tx=Scope.Begin(); var result=await action(); await tx.CommitAsync(default); return result;});}
        public async ValueTask DrainAsync() {var dispatcher=Services.GetRequiredService<DomainEventDispatcher>(); for(var i=0;i<8;i++) if(await dispatcher.DrainOnceAsync()==0) break;}
        public IReadOnlyList<long> ItemUids(int itemId) {using var connection=new SqliteConnection("Data Source="+Path);connection.Open();using var command=connection.CreateCommand();command.CommandText="SELECT item_uid,item_core FROM items";using var reader=command.ExecuteReader();var result=new List<long>();while(reader.Read())if(BinaryPrimitives.ReadInt32LittleEndian(((byte[])reader[1]).AsSpan(1))==itemId)result.Add(reader.GetInt64(0));return result;}
        public string ItemsAndPositions() {using var connection=new SqliteConnection("Data Source="+Path);connection.Open();using var command=connection.CreateCommand();command.CommandText="SELECT i.item_uid,hex(i.item_core),s.owner_scope,s.owner_id,s.list_type,s.slot_index,s.count FROM items i LEFT JOIN item_slots s ON s.core_uid=i.item_uid ORDER BY i.item_uid";using var reader=command.ExecuteReader();var rows=new List<string>();while(reader.Read())rows.Add(string.Join('|',Enumerable.Range(0,reader.FieldCount).Select(reader.GetValue)));return string.Join('\n',rows);}
        public long Scalar(string sql) {using var connection=new SqliteConnection("Data Source="+Path);connection.Open();using var command=connection.CreateCommand();command.CommandText=sql;return Convert.ToInt64(command.ExecuteScalar());}
        public void Sql(string sql) {using var connection=new SqliteConnection("Data Source="+Path);connection.Open();using var command=connection.CreateCommand();command.CommandText=sql;command.ExecuteNonQuery();}
        public async ValueTask DisposeAsync() {await Services.DisposeAsync();if(!owns)return;using var connection=new SqliteConnection("Data Source="+Path);SqliteConnection.ClearPool(connection);foreach(var suffix in new[]{"","-wal","-shm"})File.Delete(Path+suffix);}
    }
}
