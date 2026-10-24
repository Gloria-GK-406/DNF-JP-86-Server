using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Settlement;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Tests.Characters;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Tests.Inventory;
using Dfo.Server.Protocol.Game.QuestCompletion;
using Microsoft.Extensions.DependencyInjection;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Infrastructure.Content;
using Microsoft.Data.Sqlite;
using Dfo.Server.Application.Effects;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.Quests;

public sealed class OrdinaryQuestEffectTests
{
    [Fact]
    public async Task Reward_fills_every_pile_instead_of_silently_losing_the_remainder()
    {
        using var shop = Shop(s => s.AddSingleton<IQuestContentRuleService>(new Rules()));
        await shop.Seed(3, 2000, 9);
        shop.Sql("INSERT INTO character_active_quests(character_id,slot,quest_id,trigger_value) VALUES(1,0,2001,0)");
        var result = await shop.Services.GetRequiredService<ICommandBus>().SendAsync(new FinishQuestCommand(new(1), new(1), 2001, ushort.MaxValue, 1, 0));
        Assert.True(result.Finished);
        Assert.Equal(12, shop.Count(2000));
        Assert.Equal(3u, checked((uint)result.Items.Sum(i => (long)i.Count)));
        Assert.Equal(2, result.Items.Count);
    }

    internal sealed class Rules : IQuestContentRuleService
    {
        public QuestDefinition Quest { get; set; } = new(2001, "reward", QuestGrade.Epic, null, new LevelRange(1,99), null,
            null, null, [], "meet npc", "M", null, null, null, null, null, null, null, [], [], [], [],
            ItemRewards: new([new QuestItemRewardRule(2000,3)], [], false, 0));
        public List<QuestDefinition> Others { get; } = [];
        public ValueTask<IReadOnlyList<QuestDefinition>> AllAsync(CancellationToken ct) => ValueTask.FromResult<IReadOnlyList<QuestDefinition>>([Quest,..Others]);
        public ValueTask<QuestDefinition?> FindAsync(int id, CancellationToken ct) => ValueTask.FromResult(id == Quest.QuestId ? Quest : Others.FirstOrDefault(x=>x.QuestId==id));
        public ValueTask<IReadOnlyList<TrainingQuestAvailability>> TrainingAvailabilityAsync(CancellationToken ct) => ValueTask.FromResult<IReadOnlyList<TrainingQuestAvailability>>([]);
        public QuestParameters ParametersValue { get; set; } = new([100,100], new Dictionary<char,int>(),100,100,100,100);
        public QuestParameters Parameters() => ParametersValue;
    }

    // These settlement cases use synthetic content, including progression, without a client archive.
    private static ShopFixture Shop(Action<IServiceCollection>? configure = null) => new(services =>
    {
        services.AddSingleton<IExperienceRuleService>(new FixedProgression());
        services.AddSingleton<ICarriedGoldRuleService>(new CompositeGoldLimits());
        configure?.Invoke(services);
    });

    private static Task<QuestFinishOutcome> Finish(ShopFixture shop, ushort selection = ushort.MaxValue, ushort count = 1) =>
        shop.Services.GetRequiredService<ICommandBus>().SendAsync(new FinishQuestCommand(new(1),new(1),2001,selection,count,0)).AsTask();
    private static void Carry(ShopFixture shop, int trigger=0) => shop.Sql($"INSERT INTO character_active_quests(character_id,slot,quest_id,trigger_value) VALUES(1,0,2001,{trigger})");

    [Fact]
    public void Quest_planning_query_and_settlement_services_resolve_from_the_production_service_graph()
    {
        using var shop = Shop(s => s.AddSingleton<IQuestContentRuleService>(new Rules()));
        Assert.NotNull(shop.Services.GetRequiredService<IQuestQueryDomainService>());
        Assert.NotNull(shop.Services.GetRequiredService<IQuestCompletionDomainService>());
        Assert.NotNull(shop.Services.GetRequiredService<IQuestCompletionSettlementDomainService>());
        Assert.NotNull(shop.Services.GetRequiredService<IQuestClearancePlanRuleService>());
        Assert.NotNull(shop.Services.GetRequiredService<IQuestClearCouponSettlementDomainService>());
    }

    [Fact]
    public async Task Prepare_describes_cost_reward_and_quest_changes_without_writing_even_when_committed()
    {
        var rules = new Rules();
        rules.Quest = rules.Quest with { ObjectiveTypeCode = "seeking",
            SeekingItems = [new(3034, 1000), new(1000, 2), new(0, 100)],
            ItemRewards = new([new(2000, 3)], [], false, 20) };
        using var shop = Shop(s => s.AddSingleton<IQuestContentRuleService>(rules));
        await shop.SeedShared(3034, 1000);
        await shop.Seed(121, 1000, 2, 3);
        Carry(shop, 1);
        var events = shop.Scalar("SELECT COUNT(*) FROM domain_events");
        await TestGameplay.RunAsync(shop.Services, async () =>
        {
            await using (var tx = shop.Services.GetRequiredService<DomainTransactionScope>().Begin())
            {
                var prepared = await TestEffectFootprints.Run(new Dfo.Server.Abstractions.Effects.EffectFootprintOwner(new(1), new(1), null), cap => shop.Services.GetRequiredService<IQuestCompletionDomainService>().PrepareAsync(new(1), new(1), 2001, ushort.MaxValue, 1, DateTimeOffset.UnixEpoch, cap, default));
                Assert.True(prepared.Prepared);
                var reward = prepared.Plan!.Reward!;
                Assert.Equal(rules.Quest.SeekingItems, reward.ConsumedItems);
                Assert.Equal(new QuestRewardItemGrant(2000, 3), Assert.Single(reward.Items));
                Assert.Equal(20, reward.Gold);
                Assert.Contains(prepared.Plan.Effects, x => x is SetCarriedQuestEffect { Value: null });
                Assert.Contains(prepared.Plan.Effects, x => x is SetClearedQuestEffect { QuestId: 2001 });
                Assert.Contains(prepared.Plan.Effects, x => x is SpendGoldEffect { Amount: 100 });
                await tx.CommitAsync(default);
            }
        });
        Assert.Equal(1000, shop.SharedCount(3034));
        Assert.Equal(2, shop.Count(1000));
        Assert.Equal(0, shop.Count(2000));
        Assert.Equal(100000, shop.Gold);
        Assert.Equal(1, shop.Scalar("SELECT trigger_value FROM character_active_quests WHERE quest_id=2001"));
        Assert.Equal(0, shop.Scalar("SELECT COUNT(*) FROM character_invisible_flags WHERE slot_index=2001"));
        Assert.Equal(events, shop.Scalar("SELECT COUNT(*) FROM domain_events"));
        Assert.True((await Finish(shop)).Finished);
        Assert.Equal(0, shop.SharedCount(3034));
        Assert.Equal(0, shop.Count(1000));
        Assert.Equal(3, shop.Count(2000));
        Assert.Equal(99920, shop.Gold);
    }

    [Theory]
    [InlineData(1000, true)]
    [InlineData(999, false)]
    public async Task Seeking_acceptance_and_completion_use_shared_cubes_and_carried_materials(int white, bool enough)
    {
        var rules = new Rules();
        rules.Quest = rules.Quest with { ObjectiveTypeCode = "seeking",
            SeekingItems = [new(3034, 1000), new(3035, 1000), new(1000, 100), new(2000, 100)] };
        using var shop = Shop(s => s.AddSingleton<IQuestContentRuleService>(rules));
        await shop.SeedShared(3034, white);
        await shop.SeedShared(3035, 1000);
        await shop.Seed(121, 1000, 100, 3);
        await shop.Seed(65, 2000, 100);
        var bus = shop.Services.GetRequiredService<ICommandBus>();
        var accepted = await bus.SendAsync(new Dfo.Server.Protocol.Game.QuestAcceptance.AcceptQuestCommand(new(1), new(1), 2001));
        Assert.True(accepted.Accepted);
        Assert.Equal(enough ? 0 : 1, shop.Scalar("SELECT trigger_value FROM character_active_quests WHERE quest_id=2001"));
        if (enough) Assert.Equal(0, Assert.Single(accepted.QuestProgress!.Quests).TriggerValue);

        var result = await Finish(shop);
        Assert.Equal(enough, result.Finished);
        Assert.Equal(enough ? 0 : white, shop.SharedCount(3034));
        Assert.Equal(enough ? 0 : 1000, shop.SharedCount(3035));
        Assert.Equal(enough ? 0 : 100, shop.Count(1000));
        Assert.Equal(enough ? 3 : 100, shop.Count(2000));
        if (enough)
        {
            Assert.Contains(result.ConsumedItemUpdates.SelectMany(x => x.Items), x => x.SlotIndex == 355 && x.Core.ItemId == -1);
            Assert.Contains(result.ConsumedItemUpdates.SelectMany(x => x.Items), x => x.SlotIndex == 356 && x.Core.ItemId == -1);
            Assert.False((await Finish(shop)).Finished);
            Assert.Equal(3, shop.Count(2000));
        }
    }

    [Fact]
    public async Task Seeking_gold_is_spent_before_reward_and_the_final_balance_is_projected()
    {
        var rules = new Rules();
        rules.Quest = rules.Quest with { ObjectiveTypeCode = "seeking", SeekingItems = [new(0, 100)],
            ItemRewards = new([], [], false, 20) };
        using var shop = Shop(s => s.AddSingleton<IQuestContentRuleService>(rules));
        Carry(shop);
        var result = await Finish(shop);
        Assert.True(result.Finished);
        Assert.Equal(99920, shop.Gold);
        Assert.Equal(99920, Assert.Single(Assert.Single(result.ConsumedItemUpdates).Items).Core.Value);
        Assert.Equal(20u, Assert.Single(result.Items).Count);
    }

    [Fact]
    public async Task Shared_material_changes_recompute_progress_and_post_consumption_projection_for_other_quests()
    {
        var rules = new Rules();
        rules.Quest = rules.Quest with { ObjectiveTypeCode = "seeking", SeekingItems = [new(3034, 1000)] };
        rules.Others.Add(rules.Quest with { QuestId = 2002 });
        using var shop = Shop(s => s.AddSingleton<IQuestContentRuleService>(rules));
        await shop.SeedShared(3034, 1000);
        Carry(shop, 1);
        shop.Sql("INSERT INTO character_active_quests VALUES(1,1,2002,1)");
        await TestGameplay.RunAsync(shop.Services, async () =>
        {
            var scope = shop.Services.GetRequiredService<DomainTransactionScope>();
            await using (var tx = scope.Begin())
            {
                var progress = await shop.Services.GetRequiredService<IQuestProgressDomainService>()
                    .ReportAsync(new(1), new(1), [new QuestHoldingsChanged()], default);
                Assert.All(progress.NotificationSnapshot!.Quests, x => Assert.Equal(0, x.TriggerValue));
                Assert.Empty(await shop.Services.GetRequiredService<IQuestMaterialRequirementDomainService>()
                    .GetOutstandingMaterialIdsAsync(new(1), new(1), default));
                await tx.CommitAsync(default);
            }
        });
        var result = await Finish(shop);
        Assert.True(result.Finished);
        Assert.Equal(511, Assert.Single(result.QuestProgress!.Quests).TriggerValue);
        Assert.Equal(0, shop.SharedCount(3034));
    }

    [Fact]
    public async Task Rolling_back_completion_restores_shared_materials_carried_materials_and_quest()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var rules = new Rules();
            rules.Quest = rules.Quest with { ObjectiveTypeCode = "seeking", SeekingItems = [new(3034, 1000), new(1000, 2)] };
            using var shop = Shop(s => s.AddSingleton<IQuestContentRuleService>(rules));
            await shop.SeedShared(3034, 1000);
            await shop.Seed(121, 1000, 2, 3);
            Carry(shop);
            await using (var tx = shop.Services.GetRequiredService<DomainTransactionScope>().Begin())
            {
                var receipt = await TestEffectFootprints.Run(new Dfo.Server.Abstractions.Effects.EffectFootprintOwner(new(1), new(1), null), cap => shop.Services.GetRequiredService<IQuestCompletionSettlementDomainService>().FinishAsync(new(1), new(1), 2001, ushort.MaxValue, 1, DateTimeOffset.UnixEpoch, cap, default));
                Assert.True(receipt.Finished);
            }
            Assert.Equal(1000, shop.SharedCount(3034));
            Assert.Equal(2, shop.Count(1000));
            Assert.Equal(1, shop.Scalar("SELECT COUNT(*) FROM character_active_quests WHERE quest_id=2001"));
            Assert.Equal(0, shop.Count(2000));
        });
    }

    [Fact]
    public async Task Stale_missing_trigger_uses_live_holdings_and_never_consumes_requirement_items()
    {
        var rules = new Rules();
        rules.Quest = rules.Quest with { SeekingItems = [new(2000,2)] };
        using var shop = Shop(s=>s.AddSingleton<IQuestContentRuleService>(rules));
        await shop.Seed(3,2000,2); Carry(shop,2);
        Assert.True((await Finish(shop)).Finished);
        Assert.Equal(5,shop.Count(2000));
    }
    [Theory]
    [InlineData(12,2)]
    [InlineData(23,3)]
    public async Task Empty_bag_receives_the_entire_multi_stack_reward(int amount,int entries)
    {
        var rules = new Rules(); rules.Quest=rules.Quest with {ItemRewards=new([new(2000,amount)],[],false,0)};
        using var shop=Shop(s=>s.AddSingleton<IQuestContentRuleService>(rules)); Carry(shop);
        var result=await Finish(shop); Assert.True(result.Finished); Assert.Equal(amount,shop.Count(2000)); Assert.Equal(entries,result.Items.Count);
    }
    [Fact]
    public async Task Full_bag_completes_once_and_mails_only_the_unplaced_remainder()
    {
        using var shop=Shop(s=>s.AddSingleton<IQuestContentRuleService>(new Rules()));
        for(var i=0;i<6;i++) await shop.Seed(3+i,2000,i==0?9:10);
        await shop.Seed(65,2000,10); await shop.Seed(66,2000,10); Carry(shop);
        Assert.True((await Finish(shop)).Finished); Assert.Equal(80,shop.Count(2000));
        Assert.Equal(0,shop.Scalar("SELECT COUNT(*) FROM character_active_quests WHERE character_id=1"));
        Assert.Equal(2,shop.Scalar("SELECT SUM(item_count) FROM mailbox_attachments WHERE claimed_flag=0"));
        Assert.False((await Finish(shop)).Finished);
        Assert.Equal(2,shop.Scalar("SELECT SUM(item_count) FROM mailbox_attachments WHERE claimed_flag=0"));
    }
    [Fact]
    public async Task Basic_rewards_preserve_existing_pile_fields_and_create_zero_default_core()
    {
        using var shop=Shop(s=>s.AddSingleton<IQuestContentRuleService>(new Rules()));
        await shop.SeedCore(3,StackableItemCore.Create(2,2000,9) with {Durability=7,Attribute=9,SealFlag=1,ExpireTime=12345}); Carry(shop);
        Assert.True((await Finish(shop)).Finished);
        var main=shop.Services.GetRequiredService<ICharacterContainerDomainService>().FindContainer(new(1),InventoryContainerType.Main)!;
        var existing=(await main.GetAsync(3,default))!.Snapshot.ToCore(); var created=(await main.GetAsync(4,default))!.Snapshot.ToCore();
        Assert.Equal((7,9,1,12345),((int)existing.Durability,(int)existing.Attribute,(int)existing.SealFlag,existing.ExpireTime));
        Assert.Equal(StackableItemCore.Create(2,2000,3),created);
    }
    [Fact]
    public async Task Equipment_reward_count_two_creates_two_distinct_durable_items()
    {
        var rules=new Rules(); rules.Quest=rules.Quest with {ItemRewards=new([new(3000,2)],[],false,0)};
        using var shop=Shop(s=>
        {
            s.AddSingleton<IQuestContentRuleService>(rules);
            s.AddSingleton<IEquipmentDefinitionSource>(new RewardEquipmentDefinition());
        }); Carry(shop);
        var result=await Finish(shop); Assert.True(result.Finished); Assert.Equal(2,result.Items.Count);
        Assert.Equal(2,shop.Scalar("SELECT COUNT(DISTINCT core_uid) FROM item_slots WHERE owner_scope='character' AND owner_id=1"));
        await TestGameplay.RunAsync(shop.Services, async () =>
        {
            await using var read = shop.Services.GetRequiredService<DomainTransactionScope>().Begin();
            var bag = shop.Services.GetRequiredService<ICharacterBusinessInventoryDomainService>()
                .GetPersistentStore(new(InventoryOwnerType.Character, 1), BusinessInventoryOwnership.StoreOf(BusinessInventorySpace.CarriedEquipment))
                .GetContainer(BusinessInventorySpace.CarriedEquipment);
            Assert.All(await bag.GetAllAsync(default), item => Assert.Equal((ushort)45, item.Snapshot.Durability));
        });
    }

    private sealed class RewardEquipmentDefinition : IEquipmentDefinitionSource
    {
        public ValueTask<int> InitializeAsync(CancellationToken ct) => ValueTask.FromResult(1);
        public ValueTask<EquipmentDefinition?> GetEquipmentAsync(int itemId, CancellationToken ct) =>
            ValueTask.FromResult<EquipmentDefinition?>(itemId == 3000 ? new(1, 1, 1, "weapon", 45) : null);
    }
    [Fact]
    public async Task Holdings_snapshot_contains_untouched_main_and_overlays_quick_and_account_rewards()
    {
        var rules=new Rules(); rules.Quest=rules.Quest with {ItemRewards=new([new(2000,3),new(3033,2)],[],false,0)};
        rules.Others.Add(rules.Quest with {QuestId=2002,SeekingItems=[new(1000,2),new(2000,3)]});
        rules.Others.Add(rules.Quest with {QuestId=2003,SeekingItems=[new(3033,1)]});
        using var shop=Shop(s=>s.AddSingleton<IQuestContentRuleService>(rules));
        await shop.Seed(121,1000,2,3); Carry(shop);
        shop.Sql("INSERT INTO character_active_quests VALUES(1,1,2002,5),(1,2,2003,1)");
        var result=await Finish(shop); Assert.True(result.Finished); Assert.Equal(0,result.QuestProgress!.Quests.Single(x=>x.QuestId==2002).TriggerValue);
        Assert.Equal(0,result.QuestProgress.Quests.Single(x=>x.QuestId==2003).TriggerValue);
        Assert.Equal(2,Assert.Single(result.AccountMaterials!).Value);
    }
    [Fact]
    public async Task Cleared_dependents_keep_other_trigger_bits_and_ordinary_completion_keeps_tracked_rows()
    {
        var rules=new Rules(); rules.Others.Add(rules.Quest with {QuestId=2002,ClearedQuestRequirements=[2001,2001]});
        using var shop=Shop(s=>s.AddSingleton<IQuestContentRuleService>(rules)); Carry(shop);
        shop.Sql("INSERT INTO character_active_quests VALUES(1,1,2002,514); INSERT INTO character_quest_notify_selections VALUES(1,9,2001)");
        var result=await Finish(shop); Assert.True(result.Finished); Assert.Equal(512,Assert.Single(result.QuestProgress!.Quests).TriggerValue);
        Assert.Equal(1,shop.Scalar("SELECT COUNT(*) FROM character_quest_notify_selections WHERE character_id=1"));
    }
    [Theory]
    [InlineData(1,0)]
    [InlineData(65535,2)]
    public async Task Invalid_selection_or_completion_count_never_pays(ushort selection,ushort count)
    {
        using var shop=Shop(s=>s.AddSingleton<IQuestContentRuleService>(new Rules())); Carry(shop);
        Assert.False((await Finish(shop,selection,count)).Finished); Assert.Equal(0,shop.Count(2000));
    }
    [Fact]
    public async Task Unmet_or_unowned_completion_pays_nothing()
    {
        using var shop=Shop(s=>s.AddSingleton<IQuestContentRuleService>(new Rules())); Carry(shop,1);
        Assert.False((await Finish(shop)).Finished);
        Assert.False((await shop.Services.GetRequiredService<ICommandBus>().SendAsync(new FinishQuestCommand(new(2),new(1),2001,ushort.MaxValue,1,0))).Finished);
        Assert.Equal(0,shop.Count(2000));
    }
    [Fact]
    public async Task Two_legitimate_completions_add_to_current_progress_and_a_duplicate_uncarried_attempt_cannot_pay()
    {
        using var shop=Shop(s=>s.AddSingleton<IQuestContentRuleService>(new Rules())); Carry(shop);
        var first=await Finish(shop); var duplicate=await Finish(shop); Assert.False(duplicate.Finished);
        Carry(shop); var second=await Finish(shop); Assert.Equal(first.NewExperience+second.Experience,second.NewExperience);
        Assert.Equal(6,shop.Count(2000));
    }
    [Fact]
    public async Task Two_experience_effects_validate_first_then_apply_expected_after_and_preserve_cap_earned()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var shop=Shop(); shop.Sql("UPDATE characters SET level=1,exp=90 WHERE character_id=1");
            IExperienceRuleService rules=new FixedProgression();
            var first=rules.DecideAward(new(1,90),new(Base:30));
            var second=rules.DecideAward(new(first.Award.Level,first.Award.Total),new(Base:400,Channel:50));
            await using var tx=shop.Services.GetRequiredService<DomainTransactionScope>().Begin();
            using var footprint = new TestEffectFootprints.Lease(new EffectFootprintOwner(new(1), new(1), null));
            var result=await shop.Services.GetRequiredService<IApplyEffectsDomainService>().ApplyAsync(new(new(1),new(1),DateTimeOffset.UnixEpoch,footprint),
                [new CharacterExperienceEffect(first),new CharacterExperienceEffect(second)],default);
            await tx.CommitAsync(default);
            Assert.Equal(450u,result.Changes<CharacterExperienceChange>().Last().Award.Gained);
            Assert.Equal(300,shop.Scalar("SELECT exp FROM characters WHERE character_id=1"));
            Assert.Equal(3,shop.Scalar("SELECT level FROM characters WHERE character_id=1"));
            var zero=rules.DecideAward(new(3,300),new()); Assert.Equal(3,zero.Award.Level); Assert.Equal(300u,zero.Award.Total);
        });
    }
    internal sealed class FixedProgression(IReadOnlyList<uint>? thresholds = null) : IExperienceRuleService
    {
        public DungeonExperienceProgressionPolicy Progression()=>new(thresholds ?? [100,300],0,1,100);
        public uint WorthOfKill(in KillCircumstances kill)=>throw new NotSupportedException();
        public bool IsMonster(byte type)=>throw new NotSupportedException();
        public ClearExperienceWorth WorthOfClear(in ClearCircumstances clear)=>throw new NotSupportedException();
        public IReadOnlyList<ExperienceModifier> ModifiersFor(ExperienceEvent occasion,IReadOnlyList<Dfo.Server.Data.Accounts.AccountPremiumEntryState> premiums,DateTimeOffset now,int channel,int dungeon)=>throw new NotSupportedException();
    }

    [Fact]
    public async Task Gold_is_capped_at_the_old_level_and_seeking_uses_actual_gold_after()
    {
        var rules=new Rules(); rules.Quest=rules.Quest with {ItemRewards=new([],[],false,50)};
        rules.Others.Add(rules.Quest with {QuestId=2002,SeekingItems=[new(0,120)]});
        using var shop=Shop(s=> {s.AddSingleton<IQuestContentRuleService>(rules);s.AddSingleton<IExperienceRuleService>(new FixedProgression());s.AddSingleton<ICarriedGoldRuleService>(new GoldLimits());});
        shop.Sql("UPDATE characters SET level=1,exp=90 WHERE character_id=1; UPDATE character_stats SET carried_gold=90 WHERE character_id=1; INSERT INTO character_active_quests VALUES(1,1,2002,30)"); Carry(shop);
        var result=await Finish(shop);
        Assert.True(result.Finished); Assert.Equal(2,result.NewLevel); Assert.Equal(100,shop.Gold);
        Assert.Equal(10u,Assert.Single(result.Items).Count); Assert.Equal(20,Assert.Single(result.QuestProgress!.Quests).TriggerValue);
    }
    private sealed class GoldLimits : ICarriedGoldRuleService
    { public int CarryLimitFor(int level,int stored)=>Math.Max(stored,level*100); }

    private static ShopFixture Composite(Rules rules)
    {
        var content=new LearningContent(); content.Grants.Add(new(7,3,1,0));
        return Shop(s=>
        {
            s.AddSingleton<IQuestContentRuleService>(rules);
            s.AddSingleton<ISkillRuleDefinitionSource>(content);
            s.AddSingleton<ICharacterStatDefinitionSource>(new CareerStats());
            s.AddSingleton<ICarriedGoldRuleService>(new CompositeGoldLimits());
            s.AddSingleton<IExperienceRuleService>(new FixedProgression());
        });
    }
    private sealed class CompositeGoldLimits : ICarriedGoldRuleService
    {
        public int CarryLimitFor(int level, int stored) => Math.Max(stored, 1_000_000);
    }

    private sealed class CareerStats : ICharacterStatDefinitionSource
    {
        public ValueTask<CharacterBaseStats> GetInitialAsync(byte job,CancellationToken ct)=>ValueTask.FromResult(new CharacterStatValues(HpMax:100).ToFixedPoint());
        public ValueTask<IReadOnlyList<CharacterStatDefinition>> GetAllAsync(CancellationToken ct)=>ValueTask.FromResult<IReadOnlyList<CharacterStatDefinition>>([new(12,new(HpMax:100),new Dictionary<CharacterCareer,CharacterStatValues>{[new(12)]=new(HpMax:1),[new(12,1)]=new(HpMax:2),[new(12,1,1)]=new(HpMax:3),[new(12,1,2)]=new(HpMax:4)})]);
    }
    private sealed class AwakeningContent : ISkillRuleDefinitionSource
    {
        public SkillRuleCatalog Get(byte job)
        {
            var content = new LearningContent();
            content.Grants.Add(new(7, 3, 1, 1));
            content.Definitions.Add(LearningContent.Definition(8));
            content.Grants.Add(new(8,1,1,2));
            return content.Get(job) with { SupportedCareers = new HashSet<CharacterCareer> { new(12), new(12,1), new(12,1,1), new(12,1,2) } };
        }
    }

    private static ShopFixture AwakeningShop(int stage = 1)
    {
        var rules = new Rules();
        rules.Quest = rules.Quest with { ObjectiveTypeCode = "seeking", SeekingItems = [new(2000,2)],
            Rewards = [new(QuestRewardKind.AwakeningType,[stage])], ItemRewards = QuestItemRewardDefinition.Empty };
        return Shop(s =>
        {
            s.AddSingleton<IQuestContentRuleService>(rules);
            s.AddSingleton<ISkillRuleDefinitionSource>(new AwakeningContent());
            s.AddSingleton<ICharacterStatDefinitionSource>(new CareerStats());
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task First_awakening_preserves_learning_slots_extras_and_builds_and_only_merges_free_grants(bool materialized)
    {
        using var shop = AwakeningShop(); CompositeSeed(shop,materialized);
        shop.Sql("UPDATE characters SET grow_type=1 WHERE character_id=1; DELETE FROM character_active_quests WHERE quest_id=2002; INSERT INTO character_skills VALUES(1,'pve',0,7,7,1,X'0506'),(1,'pve',1,9,7,5,X'0708')");
        await shop.Seed(3,2000,2);
        var result = await Finish(shop);
        Assert.True(result.Finished); Assert.Equal((byte?)17,result.GrowType);
        Assert.Equal(17,shop.Scalar("SELECT grow_type FROM characters WHERE character_id=1"));
        Assert.Equal(0,shop.Count(2000));
        Assert.Equal(3,shop.Scalar("SELECT level FROM character_skills WHERE character_id=1 AND page_index=0 AND slot=0 AND skill_id=999 AND hex(extra_values)='0102'"));
        Assert.Equal(2,shop.Scalar("SELECT level FROM character_skills WHERE character_id=1 AND page_index=1 AND slot=3 AND skill_id=1 AND hex(extra_values)='0908'"));
        Assert.Equal(3,shop.Scalar("SELECT level FROM character_skills WHERE character_id=1 AND page_index=0 AND slot=7 AND skill_id=7 AND hex(extra_values)='0506'"));
        Assert.Equal(5,shop.Scalar("SELECT level FROM character_skills WHERE character_id=1 AND page_index=1 AND slot=9 AND skill_id=7 AND hex(extra_values)='0708'"));
        var state = shop.Services.GetRequiredService<ICharacterSkillStateDomainService>();
        var facts = (await state.ReadAsync(new(1),default))!;
        Assert.Equal(1,facts.Career.Awakening); Assert.All(facts.Pages,page => Assert.True(page.Skills.Single(x => x.SkillId==7).Level >= 3));
        var builds = await state.BuildsAsync(new(1),default);
        Assert.Equal(0,builds.Active.Value); Assert.False(builds.IsUnlocked(new(1)));
        if (materialized) Assert.False(builds.IsUnlocked(new(256)));
        Assert.Equal(5,shop.Scalar("SELECT bonus_sp FROM characters WHERE character_id=1"));
        Assert.Equal(3,shop.Scalar("SELECT bonus_tp FROM characters WHERE character_id=1"));
        Assert.False((await Finish(shop)).Finished);
    }

    [Fact]
    public async Task Second_awakening_keeps_every_allocated_skill_page_and_only_adds_the_new_stage_grants()
    {
        using var shop = AwakeningShop(2); CompositeSeed(shop,true);
        shop.Sql("UPDATE characters SET grow_type=17 WHERE character_id=1; DELETE FROM character_active_quests WHERE quest_id=2002; INSERT INTO character_skills VALUES(1,'pve',0,7,7,5,X'0506')");
        await shop.Seed(3,2000,2);
        var state = shop.Services.GetRequiredService<ICharacterSkillStateDomainService>();
        var before = (await state.ReadAsync(new(1),default))!;
        var result = await Finish(shop);
        Assert.True(result.Finished); Assert.Equal((byte?)2,result.Awakening); Assert.Equal((byte?)33,result.GrowType);
        var after = (await state.ReadAsync(new(1),default))!;
        Assert.Equal(2,after.Career.Awakening); Assert.Equal(before.Pages.Count,after.Pages.Count);
        foreach (var page in before.Pages)
        {
            var next = after.Pages.Single(x => x.PageIndex==page.PageIndex);
            foreach (var skill in page.Skills)
            {
                var kept = next.Skills.Single(x => x.SkillId==skill.SkillId);
                Assert.Equal(skill.Slot,kept.Slot); Assert.Equal(skill.Level,kept.Level); Assert.Equal(skill.ExtraValues,kept.ExtraValues);
            }
            Assert.Equal(1,next.Skills.Single(x => x.SkillId==8).Level);
        }
        Assert.False((await state.BuildsAsync(new(1),default)).IsUnlocked(new(256)));
        Assert.Equal(0,shop.Count(2000));
        Assert.False((await Finish(shop)).Finished);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Failed_awakening_skill_grant_rolls_back_materials_quest_career_and_learning(int stage)
    {
        using var shop = AwakeningShop(stage); CompositeSeed(shop,true);
        var before = 1 | ((stage-1)<<4);
        shop.Sql($"UPDATE characters SET grow_type={before} WHERE character_id=1; DELETE FROM character_active_quests WHERE quest_id=2002; CREATE TRIGGER fail_awaken BEFORE INSERT ON character_skills WHEN NEW.skill_id IN (7,8) BEGIN SELECT RAISE(ABORT,'awakening grant failed'); END");
        await shop.Seed(3,2000,2);
        var error = await Assert.ThrowsAsync<QuestFinishRejectedException>(async () => await Finish(shop));
        Assert.Contains("awakening grant failed",error.InnerException!.Message);
        Assert.Equal(before,shop.Scalar("SELECT grow_type FROM characters WHERE character_id=1"));
        Assert.Equal(2,shop.Count(2000));
        Assert.Equal(1,shop.Scalar("SELECT COUNT(*) FROM character_active_quests WHERE quest_id=2001"));
        Assert.Equal(3,shop.Scalar("SELECT COUNT(*) FROM character_skills WHERE character_id=1"));
        Assert.Equal(90,shop.Scalar("SELECT exp FROM characters WHERE character_id=1"));
    }

    [SkippableTheory]
    [InlineData(977,1,1)]
    [InlineData(4730,17,2)]
    public async Task Real_awakening_final_quests_consume_materials_and_unlock_the_next_stage_without_resetting_skills(int quest, byte before, byte stage)
    {
        Skip.If(new PvfContentOptions().ResolveArchivePath() is null,"PVF_ARCHIVE_PATH is not available.");
        using var fixture = new CeraShopFixture(useArchive:true);
        var shop = fixture.Host;
        shop.Sql($"UPDATE characters SET job=0,grow_type={before} WHERE character_id=1; INSERT INTO character_active_quests VALUES(1,0,{quest},0); INSERT INTO character_skills(character_id,mode,page_index,slot,skill_id,level,extra_values) VALUES(1,'pve',0,5,179,7,X'0102')");
        if (stage==1) { await shop.Seed(121,3229,60,3); await shop.Seed(122,3051,80,3); await shop.Seed(123,3052,120,3); }
        else await shop.Seed(121,4831,30,3);
        var result = await shop.Services.GetRequiredService<ICommandBus>().SendAsync(new FinishQuestCommand(new(1),new(1),(ushort)quest,ushort.MaxValue,1,0));
        Assert.True(result.Finished); Assert.Equal((byte?)stage,result.Awakening); Assert.Equal((byte?)(1 | (stage<<4)),result.GrowType);
        Assert.Equal(0,shop.Count(3229)); Assert.Equal(0,shop.Count(3051)); Assert.Equal(0,shop.Count(3052));
        Assert.Equal(7,shop.Scalar("SELECT level FROM character_skills WHERE character_id=1 AND page_index=0 AND slot=5 AND skill_id=179 AND hex(extra_values)='0102'"));
        var character = shop.Services.GetRequiredService<ICharacterDomainService>().Get(new(1));
        Assert.Equal(1 | (stage<<4),(await character.IdentityAsync(default))!.GrowType);
        Assert.Equal(0,shop.Count(4831));
        Assert.NotNull(await character.BaseStatsAsync(default));
        var skills = shop.Services.GetRequiredService<ICharacterSkillRuleService>();
        var facts = (await shop.Services.GetRequiredService<ICharacterSkillStateDomainService>().ReadAsync(new(1),default))!;
        var grants = skills.GetAll(0).Where(x => x.GetFreeLevel(new(0,1,stage),85)>x.GetFreeLevel(new(0,1,stage-1),85)).ToArray();
        if (stage==1) Assert.NotEmpty(grants);
        else
        {
            Assert.Empty(grants); // Current swordman.chr has an empty [awakening 2]/[awakening skill] block.
            Assert.True(skills.Get(0,235)!.CanLearn(new(0,1,2),85,1));
        }
        Assert.All(facts.Pages,page => Assert.All(grants,rule =>
            Assert.True(page.Skills.Single(x => x.SkillId==rule.SkillId).Level >= rule.GetFreeLevel(new(0,1,stage),85))));
    }
    private static Rules CompositeRules()
    {
        var rules=new Rules(); rules.Quest=rules.Quest with {Rewards=[new(QuestRewardKind.GrowType,[1])],ItemRewards=new([new(2000,3),new(3033,2)],[],false,50)};
        rules.Others.Add(rules.Quest with {QuestId=2002,SeekingItems=[new(2000,3)]});
        return rules;
    }
    private static void CompositeSeed(ShopFixture shop,bool materialized)
    {
        shop.Sql("UPDATE characters SET level=1,exp=90,bonus_sp=5,bonus_tp=3,skill_tree_index=-1 WHERE character_id=1; INSERT INTO character_active_quests VALUES(1,0,2001,0),(1,1,2002,3); INSERT INTO character_skills(character_id,mode,page_index,slot,skill_id,level,extra_values) VALUES(1,'pve',0,0,999,3,X'0102'),(1,'pve',1,3,1,2,X'0908')");
        if(materialized) shop.Sql("UPDATE characters SET skill_tree_index=0 WHERE character_id=1; INSERT INTO character_skill_builds VALUES(1,0,1),(1,1,0),(1,256,0); INSERT INTO character_skills(character_id,mode,page_index,slot,skill_id,level,extra_values) VALUES(1,'pve',256,4,999,3,X'0304')");
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Advancement_expands_all_pages_preserves_build_flags_and_bonus_basis(bool materialized)
    {
        using var shop=Composite(CompositeRules()); CompositeSeed(shop,materialized);
        var result=await Finish(shop); Assert.True(result.Finished); Assert.Equal((byte?)1,result.GrowType);
        var state=shop.Services.GetRequiredService<ICharacterSkillStateDomainService>(); var facts=(await state.ReadAsync(new(1),default))!;
        Assert.Equal(2,facts.Level); Assert.Equal(materialized?3:2,facts.Pages.Count);
        foreach(var page in facts.Pages) {Assert.DoesNotContain(page.Skills,x=>x.SkillId is 999 or 1);Assert.Equal(3,page.Skills.Single(x=>x.SkillId==7).Level);Assert.All(page.Skills,x=>Assert.Empty(x.ExtraValues));}
        var builds=await state.BuildsAsync(new(1),default); Assert.Equal(0,builds.Active.Value); Assert.False(builds.IsUnlocked(new(1)));
        if(materialized) Assert.False(builds.IsUnlocked(new(256)));
        Assert.Equal(5,shop.Scalar("SELECT bonus_sp FROM characters WHERE character_id=1")); Assert.Equal(3,shop.Scalar("SELECT bonus_tp FROM characters WHERE character_id=1"));
        var ledger=shop.Services.GetRequiredService<ICharacterSkillRuleService>().GetLedger(facts.Career,facts.Level,facts.Pages,facts.PointBasis); Assert.Equal(1005,ledger.For(new(0)).Sp);
        Assert.Equal(0,Assert.Single(result.QuestProgress!.Quests).TriggerValue);
    }
    [Theory]
    [InlineData("skill")]
    [InlineData("trigger")]
    [InlineData("xp")]
    public async Task Production_sqlite_late_fault_rolls_back_every_part_of_the_composite(string fault)
    {
        using var shop=Composite(CompositeRules()); CompositeSeed(shop,true); await shop.Seed(3,2000,9);
        var trigger=fault switch
        {
            "skill"=>"CREATE TRIGGER fail_late BEFORE INSERT ON character_skills WHEN NEW.character_id=1 AND NEW.skill_id=7 BEGIN SELECT RAISE(ABORT,'late skill'); END",
            "xp"=>"CREATE TRIGGER fail_late BEFORE UPDATE OF exp ON characters WHEN NEW.character_id=1 BEGIN SELECT RAISE(ABORT,'late xp'); END",
            _=>"CREATE TRIGGER fail_late BEFORE UPDATE OF trigger_value ON character_active_quests WHEN NEW.character_id=1 AND NEW.quest_id=2002 BEGIN SELECT RAISE(ABORT,'late related trigger'); END"
        };
        shop.Sql(trigger);
        SqliteException cause;
        if(fault=="trigger")
            cause=await Assert.ThrowsAsync<SqliteException>(async()=>await Finish(shop)); // Raw quest section flushes during UoW Prepare, after the command returns.
        else
        {
            var error=await Assert.ThrowsAsync<QuestFinishRejectedException>(async()=>await Finish(shop));
            cause=Assert.IsType<SqliteException>(error.InnerException);
        }
        Assert.Contains(fault=="trigger"?"late related trigger":$"late {fault}",cause.Message);
        Assert.Equal(2,shop.Scalar("SELECT COUNT(*) FROM character_active_quests WHERE character_id=1")); Assert.Equal(3,shop.Scalar("SELECT trigger_value FROM character_active_quests WHERE character_id=1 AND quest_id=2002"));
        Assert.Equal(0,shop.Scalar("SELECT COUNT(*) FROM character_invisible_flags WHERE character_id=1"));
        Assert.Equal(9,shop.Count(2000)); Assert.Equal(1,shop.Scalar("SELECT COUNT(*) FROM item_slots WHERE owner_scope='character' AND owner_id=1"));
        Assert.Equal(0, shop.SharedCount(3033));
        Assert.Equal(100000,shop.Gold); Assert.Equal(90,shop.Scalar("SELECT exp FROM characters WHERE character_id=1")); Assert.Equal(1,shop.Scalar("SELECT level FROM characters WHERE character_id=1")); Assert.Equal(0,shop.Scalar("SELECT grow_type FROM characters WHERE character_id=1"));
        Assert.Equal(3,shop.Scalar("SELECT COUNT(*) FROM character_skills WHERE character_id=1")); Assert.Equal(2,shop.Scalar("SELECT length(extra_values) FROM character_skills WHERE character_id=1 AND page_index=0"));
        Assert.Equal(0,shop.Scalar("SELECT unlocked FROM character_skill_builds WHERE character_id=1 AND build_id=256")); Assert.Equal(5,shop.Scalar("SELECT bonus_sp FROM characters WHERE character_id=1"));
    }
    [Fact]
    public async Task Basic_experience_handler_rejects_missing_target_even_for_zero_award()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var shop=Shop();
            await using var tx=shop.Services.GetRequiredService<DomainTransactionScope>().Begin();
            var handler=new CharacterExperienceEffectHandler(shop.Services.GetRequiredService<Dfo.Server.Abstractions.Characters.ICharacterDomainService>());
            using var footprint = new TestEffectFootprints.Lease(new EffectFootprintOwner(new(1), new(999), null));
            var error=await Assert.ThrowsAsync<InvalidOperationException>(async()=>await handler.ApplyAsync(new(new(1),new(999),DateTimeOffset.UnixEpoch,footprint),new(new(new(1,0),ExperienceAward.Nothing(1,0))),default));
            Assert.Equal("Decided experience target changed.",error.Message);
        });
    }
    [Fact]
    public async Task Quest_pays_its_decided_amount_into_sqlite_and_prices_each_completion_at_current_db_level()
    {
        var rules=new Rules { ParametersValue=new([..Enumerable.Range(1,100).Select(x=>x*100)],new Dictionary<char,int>{{'M',10}},80,30,120,140) };
        rules.Quest=rules.Quest with {PaysAtCharacterLevel=true,ItemRewards=QuestItemRewardDefinition.Empty};
        using var shop=Shop(s=>
        {
            s.AddSingleton<IQuestContentRuleService>(rules);
            s.AddSingleton<IExperienceRuleService>(new FixedProgression(
                Enumerable.Range(1,100).Select(level => checked((uint)level * 1_000_000)).ToArray()));
        });
        shop.Sql("UPDATE characters SET level=1,exp=1000 WHERE character_id=1"); Carry(shop);
        var first=await Finish(shop); Assert.Equal(101u,first.Experience); Assert.Equal(1101u,first.NewExperience);
        Assert.Equal(1101,shop.Scalar("SELECT exp FROM characters WHERE character_id=1")); Assert.Equal(1,shop.Scalar("SELECT flag_value FROM character_invisible_flags WHERE character_id=1 AND slot_index=2001"));
        shop.Sql("UPDATE characters SET level=40,exp=39001101 WHERE character_id=1"); Carry(shop);
        var high=await Finish(shop); Assert.True(high.Experience>first.Experience); Assert.Equal(39001101u+high.Experience,high.NewExperience);
    }
    [Fact]
    public async Task Reward_filters_selection_by_old_job_and_grow_before_advancement()
    {
        var rules=CompositeRules(); rules.Quest=rules.Quest with {ItemRewards=new([new(2000,3,new(12,0)),new(2000,9,new(12,1)),new(2000,7,new(0,-1))],[new(2000,5,new(12,1)),new(2000,2,new(12,0))],false,0)};
        using var shop=Composite(rules); CompositeSeed(shop,true);
        Assert.True((await Finish(shop,0)).Finished); Assert.Equal(5,shop.Count(2000));
    }
    [SkippableFact]
    public async Task Real_quest_1776_grants_only_job_zero_equipment_and_101_gold_in_sqlite()
    {
        Skip.If(new PvfContentOptions().ResolveArchivePath() is null,"PVF_ARCHIVE_PATH is not available.");
        using var archive=new PvfArchiveProvider(new PvfContentOptions());
        using var shop=new ShopFixture(s=>s.AddSingleton<IItemPlacementDefinitionSource>(new PvfItemPlacementDefinitionSource(archive)));
        shop.Sql("UPDATE characters SET job=0,level=1,exp=0 WHERE character_id=1; UPDATE character_stats SET carried_gold=0 WHERE character_id=1; INSERT INTO character_active_quests VALUES(1,0,1776,0)");
        var result=await shop.Services.GetRequiredService<ICommandBus>().SendAsync(new FinishQuestCommand(new(1),new(1),1776,ushort.MaxValue,1,0));
        Assert.True(result.Finished); Assert.Equal(1,shop.Count(101010437)); Assert.Equal(1,shop.Scalar("SELECT COUNT(*) FROM item_slots WHERE owner_scope='character' AND owner_id=1"));
        Assert.Equal(101,shop.Gold); Assert.Contains(new FinishedQuestItem(0,0,101),result.Items);
    }
    [SkippableFact]
    public async Task Basic_avatar_and_creature_rewards_keep_permanent_default_creation_facts()
    {
        Skip.If(new PvfContentOptions().ResolveArchivePath() is null,"PVF_ARCHIVE_PATH is not available.");
        var rules=new Rules(); rules.Quest=rules.Quest with {ItemRewards=new([new(39032,2),new(63000,1)],[],false,0)};
        using var shop=new CeraShopFixture(useArchive:true,configure:s=>s.AddSingleton<IQuestContentRuleService>(rules)); Carry(shop.Host);
        var result=await Finish(shop.Host); Assert.True(result.Finished); Assert.Equal(3,result.Items.Count);
        Assert.Equal(2,shop.Host.Count(39032)); Assert.Equal(1,shop.Host.Count(63000));
        Assert.Equal(2,shop.Host.Scalar("SELECT COUNT(*) FROM items WHERE avatar_expire_date=0 AND avatar_delete_date=0 AND length(avatar_jewel_socket)=30"));
        Assert.Equal(1,shop.Host.Scalar("SELECT COUNT(*) FROM items WHERE creature_field04=100 AND creature_field_after_value=1 AND creature_progress_value=0"));
        var bags=shop.Host.Services.GetRequiredService<ICharacterContainerDomainService>();
        var avatars=await bags.FindContainer(new(1),InventoryContainerType.Avatar)!.GetAllAsync(default);
        Assert.Equal(2,avatars.Select(x=>x.Snapshot.ToCore().Value).Distinct().Count());
        foreach(var type in new[]{InventoryContainerType.Avatar,InventoryContainerType.Pet})
            foreach(var item in await bags.FindContainer(new(1),type)!.GetAllAsync(default))
                Assert.Equal((0,0,0,0),((int)item.Snapshot.ToCore().Durability,(int)item.Snapshot.ToCore().Attribute,(int)item.Snapshot.ToCore().SealFlag,item.Snapshot.ToCore().ExpireTime));
    }
    [Theory]
    [InlineData(1,0,450,3,300)]
    [InlineData(3,300,100,3,300)]
    [InlineData(3,300,0,3,300)]
    public async Task Decided_experience_separates_multilevel_cap_and_zero_from_earned(byte level,uint held,uint earned,byte afterLevel,uint after)
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var shop=Shop();shop.Sql($"UPDATE characters SET level={level},exp={held} WHERE character_id=1");
            IExperienceRuleService rules=new FixedProgression();var decision=rules.DecideAward(new(level,held),new(Base:earned));
            if(earned==0) shop.Sql("CREATE TRIGGER forbid_xp_write BEFORE UPDATE OF exp ON characters BEGIN SELECT RAISE(ABORT,'zero XP wrote'); END");
            await using var tx=shop.Services.GetRequiredService<DomainTransactionScope>().Begin();
            using var footprint = new TestEffectFootprints.Lease(new EffectFootprintOwner(new(1), new(1), null));
            var result=await shop.Services.GetRequiredService<IApplyEffectsDomainService>().ApplyAsync(new(new(1),new(1),DateTimeOffset.UnixEpoch,footprint),[new CharacterExperienceEffect(decision)],default);await tx.CommitAsync(default);
            var award=Assert.Single(result.Changes<CharacterExperienceChange>()).Award;Assert.Equal(earned,award.Gained);Assert.Equal(after,award.Total);Assert.Equal(afterLevel,award.Level);
            Assert.Equal((int)after,shop.Scalar("SELECT exp FROM characters WHERE character_id=1"));Assert.Equal((int)afterLevel,shop.Scalar("SELECT level FROM characters WHERE character_id=1"));
        });
    }
    [SkippableFact]
    public async Task Large_reward_finishes_once_with_inventory_entries_and_mailed_overflow()
    {
        Skip.If(new PvfContentOptions().ResolveArchivePath() is null,"PVF_ARCHIVE_PATH is not available.");
        var rules=new Rules();rules.Quest=rules.Quest with {ItemRewards=new([new(39032,200),new(63000,60)],[],false,0)};
        using var shop=new CeraShopFixture(useArchive:true,configure:s=>s.AddSingleton<IQuestContentRuleService>(rules));Carry(shop.Host);
        var result=await Finish(shop.Host);Assert.True(result.Finished);Assert.True(result.Items.Count<=byte.MaxValue);
        Assert.Equal(260,shop.Host.Count(39032)+shop.Host.Count(63000)+shop.Host.Scalar("SELECT SUM(item_count) FROM mailbox_attachments WHERE claimed_flag=0"));
        Assert.Equal(0,shop.Host.Scalar("SELECT COUNT(*) FROM character_active_quests WHERE character_id=1"));
    }
    [Fact]
    public async Task Missing_character_cannot_report_a_placeholder_completion()
    {
        using var shop=Shop(s=>s.AddSingleton<IQuestContentRuleService>(new Rules()));
        var result=await shop.Services.GetRequiredService<ICommandBus>().SendAsync(new FinishQuestCommand(new(1),new(999),2001,ushort.MaxValue,1,0));
        Assert.False(result.Finished);Assert.Equal(QuestFinishRefusal.Unusable,result.Refusal);
    }
}
