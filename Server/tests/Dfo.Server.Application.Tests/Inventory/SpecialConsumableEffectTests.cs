using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Tests.Characters;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Characters;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Immutable;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class SpecialConsumableEffectTests
{
    [Theory]
    [InlineData(10155510, "[guild dungeon time ticket]", true, false)]
    [InlineData(10155510, "[other]", false, false)]
    [InlineData(10155511, "[guild dungeon time ticket]", false, false)]
    [InlineData(10155511, "[other]", false, true)]
    public async Task Guild_extension_requires_matching_item_and_action(int itemId, string action, bool extension, bool ordinary)
    {
        var declaration = ItemUseRules.Potion() with { Action = action };
        var rules = new ItemUseEffectRuleService(new ItemUseRuleService(new FakeItemUseRules((itemId, declaration))),
            TestRequired.Absent<IStackableActionDefinitionSource>());
        Assert.Equal(extension ? declaration : null, await rules.GuildExtensionActionRuleAsync(itemId, default));
        Assert.Equal(ordinary ? declaration : null, await rules.OrdinaryUseRuleAsync(itemId, default));
    }

    [Theory]
    [InlineData(3,3,true)]
    [InlineData(0,3,false)]
    [InlineData(3,4,false)]
    public async Task Consumption_checks_the_decided_lock_without_changing_it(byte expected, byte actual, bool success)
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var f = new ShopFixture();
            await f.SeedCore(65, StackableItemCore.Create(2,2000,2) with { EquipmentLockId=actual,ExpireTime=int.MaxValue });
            var scope = (DomainTransactionScope)f.Services.GetRequiredService<IDomainTransactionAccessor>();
            await using var tx=scope.Begin();
            var apply=f.Services.GetRequiredService<IApplyEffectsDomainService>();
            using var footprint = new Dfo.Server.Application.Tests.Support.TestEffectFootprints.Lease(f.Session);
            var context=new EffectExecutionContext(f.Session.AccountId,f.Session.CharacterId,f.Clock.GetUtcNow(),footprint,f.Session);
            if(success)
            {
                await apply.ApplyAsync(context,[new ConsumeFromContainerEffect(new(BusinessInventorySpace.Consumable,0),2000,1,expected)],default);
                await tx.CommitAsync(default);
                Assert.Equal(1,f.Count(2000));
                Assert.Equal(actual,(await f.Services.GetRequiredService<IBusinessInventorySlotDomainService>().Get(f.Session.CharacterId,
                    new(BusinessInventorySpace.Consumable,0)).Container.GetAsync(0,default))!.Snapshot.ToCore().EquipmentLockId);
            }
            else
            {
                await Assert.ThrowsAsync<InventoryEffectException>(()=>apply.ApplyAsync(context,[new ConsumeFromContainerEffect(new(BusinessInventorySpace.Consumable,0),2000,1,expected)],default).AsTask());
                await tx.CommitAsync(default); Assert.Equal(2,f.Count(2000));
            }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Real_potion_inventory_fatigue_usage_and_cooldown_commit_or_rollback_together(bool fail)
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var f=new ShopFixture(s=>
            {
                s.AddSingleton<IStackableActionDefinitionSource>(new ActionSource());
                s.AddSingleton<IItemUsageRuleSource>(new UsageSource());
                s.AddSingleton<IItemUseRuleDefinitionSource>(new FakeItemUseRules((2000,ItemUseRules.PersistentPotion(1000) with { Action = "[add fatigue]" })));
            });
            await f.Seed(65,2000,2);
            var scope=(DomainTransactionScope)f.Services.GetRequiredService<IDomainTransactionAccessor>();
            var fatigue=f.Services.GetRequiredService<Dfo.Server.Abstractions.Characters.ICharacterFatigueDomainService>();
            await using(var tx=scope.Begin()){await fatigue.SpendAsync(f.Session.CharacterId,100,default);await tx.CommitAsync(default);}
            if(fail) f.Sql("CREATE TRIGGER fail_potion BEFORE INSERT ON character_daily_counters BEGIN SELECT RAISE(ABORT,'usage'); END");
            async Task Use()
            {
                await using var tx=scope.Begin();
                await TestEffectFootprints.Run(f.Session, cap => f.Services.GetRequiredService<IFatiguePotionDomainService>().UseAsync(f.Session,new(BusinessInventorySpace.Consumable,0),f.Clock.GetUtcNow(), cap,default));
                await tx.CommitAsync(default);
            }
            if(fail) await Assert.ThrowsAsync<SqliteException>(Use); else await Use();
            Assert.Equal(fail?2:1,f.Count(2000));
            var balance=await fatigue.BalanceOfAsync(f.Session.CharacterId,default);
            Assert.Equal(fail?100:70,balance.Used);Assert.Equal(100,balance.ConsumedToday);
            Assert.Equal(fail?0:1,f.Scalar("SELECT COUNT(*) FROM character_daily_counters WHERE counter_key='consumable_used_2000'"));
            Assert.False(f.Services.GetRequiredService<IOnlineCharacterRuntimeDomainService>().IsCoolingDown(f.Session.CharacterId,ItemCooldownKey.ForItem(2000)));
            Assert.Equal(!fail, (await f.Services.GetRequiredService<IItemUsageDomainService>()
                .GetAsync(f.Session.CharacterId, 2000, default)).CooldownUntil is not null);
        });
    }

    [Fact]
    public async Task Raw_cross_day_facts_are_read_only_and_cooldown_survives_effective_usage_reset()
    {
        await using var f=new LearningFixture();await f.SeedAsync();
        var at=new DateTimeOffset(2026,10,1,6,1,0,TimeSpan.FromHours(8));
        var previous=at.AddMinutes(-2);
        f.Sql($"INSERT INTO character_fatigue VALUES(71,100,110,{previous.ToUnixTimeMilliseconds()}); INSERT INTO character_daily_counters(character_id,counter_key,value,last_consumed_at) VALUES(71,'consumable_used_2000',3,{previous.ToUnixTimeMilliseconds()})");
        var states=f.Services.GetRequiredService<IConsumableStateDomainService>();
        var raw=await states.FatigueAsync(f.Session.CharacterId,default);
        var usage=await states.UsageAsync(f.Session.CharacterId,2000,default);
        Assert.Equal(100,raw.Raw!.Used);Assert.Equal(3,usage!.UsedCount);
        var rules=new SpecialConsumableEffectPlanRuleService(new ActionSource(),new UsageSource(86400000),
            f.Services.GetRequiredService<Dfo.Server.Abstractions.Configuration.IDailyResetRuleService>(),
            new ItemUseRuleService(new FakeItemUseRules((2000,ItemUseRules.Potion(0)))),null!, clearance: TestRequired.Absent<Dfo.Server.Abstractions.Quests.IQuestClearancePlanRuleService>());
        var item=TestItemReferences.FromCore(0,StackableItemCore.Create(2,2000,2),ItemWorldClass.Interchangeable);
        await Assert.ThrowsAsync<OpeningRejectedException>(()=>rules.FatigueAsync(new(new(BusinessInventorySpace.Consumable,0),item,85,true,
            raw with { Raw=new(100,110,at) },usage,at),seed: 0,default).AsTask());
        Assert.Equal(100,f.Scalar("SELECT used FROM character_fatigue WHERE character_id=71"));
        Assert.Equal(3,f.Scalar("SELECT value FROM character_daily_counters WHERE character_id=71"));
        rules=new SpecialConsumableEffectPlanRuleService(new ActionSource(),new UsageSource(0),
            f.Services.GetRequiredService<Dfo.Server.Abstractions.Configuration.IDailyResetRuleService>(),
            new ItemUseRuleService(new FakeItemUseRules((2000,ItemUseRules.Potion(0)))),null!, clearance: TestRequired.Absent<Dfo.Server.Abstractions.Quests.IQuestClearancePlanRuleService>());
        var plan=await rules.FatigueAsync(new(new(BusinessInventorySpace.Consumable,0),item,85,true,
            raw with { Raw=new(100,110,at) },usage,at),seed: 0,default);
        var usageEffect=Assert.Single(plan.OfType<SetItemUsageStateEffect>());
        Assert.Equal(usage,usageEffect.Expected);Assert.Equal(1,usageEffect.Value.UsedCount);Assert.Equal(at,usageEffect.Value.LastUsedAt);
        await Assert.ThrowsAsync<OpeningRejectedException>(()=>rules.FatigueAsync(new(new(BusinessInventorySpace.Consumable,0),item,85,true,raw,usage,at),seed: 0,default).AsTask());
        Assert.Equal(100,f.Scalar("SELECT used FROM character_fatigue WHERE character_id=71"));
    }

    [Theory]
    [InlineData(SkillGroup.Pve, "pve", "pvp")]
    [InlineData(SkillGroup.Duel, "pvp", "pve")]
    public async Task Sequential_skill_effects_use_prior_effect_state_and_replaced_identity_rolls_back_the_batch(
        SkillGroup group, string mode, string otherMode)
    {
        await TestGameplay.RunAsync(async () =>
        {
            await using var f=new LearningFixture();await f.SeedAsync();
            f.Sql("INSERT INTO character_skills(character_id,mode,page_index,slot,skill_id,level) VALUES(71,'pvp',0,7,1,2)");
            int Level(string storedMode) => f.Scalar(
                $"SELECT level FROM character_skills WHERE character_id=71 AND mode='{storedMode}' AND page_index=0 AND slot=7");
            var apply=f.Services.GetRequiredService<IApplyEffectsDomainService>();
            using var footprint = new Dfo.Server.Application.Tests.Support.TestEffectFootprints.Lease(f.Session);
            var context=new EffectExecutionContext(f.Session.AccountId,f.Session.CharacterId,DateTimeOffset.UtcNow,footprint,f.Session);
            await using(var tx=f.Scope.Begin())
            {
                await apply.ApplyAsync(context,[new SetLearnedSkillEffect(0,7,1,2,3,group),new SetLearnedSkillEffect(0,7,1,3,4,group)],default);
                await tx.CommitAsync(default);
            }
            Assert.Equal(4,Level(mode));
            Assert.Equal(2,Level(otherMode));
            await using(f.Scope.Begin())
                await Assert.ThrowsAsync<InvalidOperationException>(()=>apply.ApplyAsync(context,[new SetLearnedSkillEffect(0,7,1,4,5,group),
                    new SetLearnedSkillEffect(0,7,2,5,6,group)],default).AsTask());
            Assert.Equal(4,Level(mode));
            Assert.Equal(2,Level(otherMode));
        });
    }

    [Fact]
    public async Task Sequential_quest_effects_keep_the_original_SQL_expectation_until_flush()
    {
        await TestGameplay.RunAsync(async () =>
        {
            await using var f=new LearningFixture();await f.SeedAsync();f.Sql("INSERT INTO character_active_quests VALUES(71,2,2003,3)");
            var first=new StoredActiveQuest(2,2003,3);var next=first with {TriggerValue=2};
            await using var tx=f.Scope.Begin();
            using var footprint = new Dfo.Server.Application.Tests.Support.TestEffectFootprints.Lease(f.Session);
            await f.Services.GetRequiredService<IApplyEffectsDomainService>().ApplyAsync(new(f.Session.AccountId,f.Session.CharacterId,DateTimeOffset.UtcNow,footprint,f.Session),
                [new SetCarriedQuestEffect(first,next),new SetCarriedQuestEffect(next,next with {TriggerValue=1})],default);
            await tx.CommitAsync(default);
            Assert.Equal(1,f.Scalar("SELECT trigger_value FROM character_active_quests WHERE character_id=71"));
        });
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(51)]
    public void Creature_consumable_binder_rejects_outside_local_bounds(int index)
    {
        using var f=new ShopFixture();
        Assert.Throws<InventorySlotMappingException>(()=>f.Services.GetRequiredService<IBusinessInventorySlotDomainService>().Get(f.Session.CharacterId,new(BusinessInventorySpace.CreatureConsumable,index)));
    }

    private sealed class ActionSource : IStackableActionDefinitionSource
    {
        public ValueTask<StackableActionDefinition?> GetAsync(int id,CancellationToken ct)=>ValueTask.FromResult<StackableActionDefinition?>(new("[add fatigue]",1,85,0,true){FatigueRecovery=30});
    }
    private sealed class UsageSource(int cooldown=1000) : IItemUsageRuleSource
    {
        public ValueTask<ItemUsageRule?> GetAsync(int id,CancellationToken ct)=>ValueTask.FromResult<ItemUsageRule?>(new(3,cooldown,true) { CooldownMaintenance = new(true, true, true) });
    }

    [Fact]
    public void Permanent_item_can_be_used_after_2038()
    {
        var rule = ItemUseRules.Potion(0);
        var plans = new ItemUseEffectRuleService(new ItemUseRuleService(new FakeItemUseRules((8474, rule))), Dfo.Server.Testing.TestRequired.Absent<Dfo.Server.Abstractions.Content.IStackableActionDefinitionSource>());
        var item = TestItemReferences.FromCore(0, StackableItemCore.Create(2, 8474, 2) with { ExpireTime = 0 },
            ItemWorldClass.Interchangeable);
        var result = plans.Build(rule, new(BusinessInventorySpace.QuickSlot, 0), item,
            new ItemUseCandidate(60, QuestJobRequirement.Swordman, ItemUsePlace.Village, false), null,
            new DateTimeOffset(2050, 1, 1, 0, 0, 0, TimeSpan.Zero));
        Assert.Equal(ItemUsability.Allowed, result.Status);
    }
}
