using System.Buffers.Binary;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Creatures;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Settlement;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Tests.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Characters;
using Dfo.Server.Persistence.Database;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.Inventory;

/// <summary>Real item, creature, quest and skill facets share one temporary database and transaction.</summary>
public sealed class RealConsumableIntegrationTests
{
    private static readonly BusinessSlot PetSource = new(BusinessInventorySpace.CreatureConsumable, 0);
    private static readonly BusinessSlot MainSource = new(BusinessInventorySpace.Consumable, 4);

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Food_and_pet_stomach_share_SQL_commit_late_prepare_rollback_and_retry(bool fail)
    {
        await TestGameplay.RunAsync(async () =>
        {
            await using var f = Fixture(); await f.SeedAsync();
            await SeedSource(f, 24, InventoryContainerType.Pet, 189);
            SeedPet(f);
            var runtime = f.Services.GetRequiredService<ICreatureRuntimeDomainService>(); runtime.Enter(f.Session);
            async Task Feed(bool lateFailure)
            {
                await using var tx = f.Scope.Begin();
                var source = (await f.Services.GetRequiredService<IBusinessInventorySlotDomainService>()
                    .Get(f.Session.CharacterId, PetSource).Container.GetAsync(0, default))!;
                var applied = await f.Services.GetRequiredService<IItemUseEffectDomainService>().UseAsync(f.Session, PetSource, source, default);
                Assert.Equal(ItemUsability.Allowed, applied.Status);
                Assert.Equal(80, Assert.Single(applied.Applied!.Changes<PetStomachChange>()).Change.After.Stomach);
                Assert.Empty(runtime.DrainProjections());
                var database = f.Services.GetRequiredService<SqliteDatabase>();
                var participant = f.Scope.Current!.Enlist<SqliteDatabaseParticipant>(database.ConnectionString, () => throw new InvalidOperationException("Expected existing SQL participant."));
                if (lateFailure) f.Scope.Current!.Enlist(new object(), () => new AfterFlush(async () => {
                    Assert.Equal(1, await PreparedScalar(participant, "SELECT count FROM item_slots WHERE owner_id=71 AND list_type=7 AND slot_index=189"));
                    Assert.Equal(80, await PreparedScalar(participant, "SELECT creature_field04 FROM items WHERE item_uid=101"));
                    Assert.Empty(runtime.DrainProjections());
                    throw new IOException("after actual food and creature facet flush");
                }));
                await tx.CommitAsync(default);
            }
            if (fail)
            {
                await Assert.ThrowsAsync<IOException>(() => Feed(true));
                Assert.Equal(2, SourceCount(f, InventoryContainerType.Pet, 189));
                Assert.Equal(50, f.Scalar("SELECT creature_field04 FROM items WHERE item_uid=101"));
                Assert.Empty(runtime.DrainProjections());
            }
            await Feed(false);
            Assert.Equal(1, SourceCount(f, InventoryContainerType.Pet, 189));
            Assert.Equal(80, f.Scalar("SELECT creature_field04 FROM items WHERE item_uid=101"));
            Assert.NotEmpty(runtime.DrainProjections()); AssertPetIdentityAndGrowth(f);
        });
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Rename_card_and_actual_pet_name_share_late_prepare_rollback_and_retry(bool fail)
    {
        await TestGameplay.RunAsync(async () =>
        {
            await using var f = Fixture(); await f.SeedAsync();
            await SeedSource(f, 25, InventoryContainerType.Pet, 189); SeedPet(f);
            f.Services.GetRequiredService<ICreatureRuntimeDomainService>().Enter(f.Session);
            async Task Rename(bool lateFailure)
            {
                await using var tx = f.Scope.Begin();
                var receipt = await f.Services.GetRequiredService<ICreatureNamingDomainService>()
                    .RenameAsync(f.Session, PetSource, [0x82, 0xA0], default);
                Assert.NotNull(receipt); Assert.Equal(1, receipt.RemainingSourceCore!.ToCore().Value);
                var database = f.Services.GetRequiredService<SqliteDatabase>();
                var participant = f.Scope.Current!.Enlist<SqliteDatabaseParticipant>(database.ConnectionString, () => throw new InvalidOperationException("Expected existing SQL participant."));
                if (lateFailure) f.Scope.Current!.Enlist(new object(), () => new AfterFlush(async () => {
                    Assert.Equal(1, await PreparedScalar(participant, "SELECT count FROM item_slots WHERE owner_id=71 AND list_type=7 AND slot_index=189"));
                    Assert.Equal(1, await PreparedScalar(participant, "SELECT COUNT(*) FROM items WHERE item_uid=101 AND creature_text=X'82A0'"));
                    throw new IOException("after actual rename and card facet flush");
                }));
                await tx.CommitAsync(default);
            }
            if (fail)
            {
                await Assert.ThrowsAsync<IOException>(() => Rename(true));
                Assert.Equal(2, SourceCount(f, InventoryContainerType.Pet, 189));
                Assert.Equal(1, f.Scalar("SELECT COUNT(*) FROM items WHERE item_uid=101 AND creature_text=X'80'"));
                AssertPetIdentityAndGrowth(f);
            }
            await Rename(false);
            Assert.Equal(1, SourceCount(f, InventoryContainerType.Pet, 189));
            Assert.Equal(1, f.Scalar("SELECT COUNT(*) FROM items WHERE item_uid=101 AND creature_text=X'82A0'"));
            Assert.Equal(50, f.Scalar("SELECT creature_field04 FROM items WHERE item_uid=101"));
            AssertPetIdentityAndGrowth(f);
        });
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Coupon_real_source_and_all_quest_sections_rollback_and_retry_together(bool fail)
    {
        await TestGameplay.RunAsync(async () =>
        {
            await using var f = Fixture(); await f.SeedAsync();
            await SeedSource(f, 10_006_007, InventoryContainerType.Main, 69);
            f.Sql("INSERT INTO character_active_quests VALUES(71,0,101,9),(71,2,2003,0x05040303); INSERT INTO character_quest_notify_selections VALUES(71,2,101),(71,7,101),(71,9,2003);");
            async Task Use()
            {
                await using var tx = f.Scope.Begin();
                var result = await f.Services.GetRequiredService<IQuestClearCouponSettlementDomainService>()
                    .UseAsync(f.Session, MainSource, f.Services.GetRequiredService<TimeProvider>().GetUtcNow(), default);
                Assert.Equal([101], result.CompletedQuestIds);
                await tx.CommitAsync(default);
            }
            if (fail)
            {
                f.Sql("CREATE TRIGGER late_coupon BEFORE INSERT ON character_invisible_flags BEGIN SELECT RAISE(ABORT,'late coupon flags'); END;");
                await Assert.ThrowsAsync<SqliteException>(Use);
                Assert.Equal(2, SourceCount(f, InventoryContainerType.Main, 69));
                Assert.Equal(2, f.Scalar("SELECT COUNT(*) FROM character_active_quests WHERE character_id=71"));
                Assert.Equal(0x05040303, f.Scalar("SELECT trigger_value FROM character_active_quests WHERE character_id=71 AND quest_id=2003"));
                Assert.Equal(3, f.Scalar("SELECT COUNT(*) FROM character_quest_notify_selections WHERE character_id=71"));
                Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM character_invisible_flags WHERE character_id=71"));
                f.Sql("DROP TRIGGER late_coupon;");
            }
            await Use();
            Assert.Equal(1, SourceCount(f, InventoryContainerType.Main, 69));
            Assert.Equal(1, f.Scalar("SELECT COUNT(*) FROM character_active_quests WHERE character_id=71"));
            Assert.Equal((0x05040303 & ~0x1FF) | 2, f.Scalar("SELECT trigger_value FROM character_active_quests WHERE character_id=71 AND quest_id=2003"));
            Assert.Equal(1, f.Scalar("SELECT COUNT(*) FROM character_quest_notify_selections WHERE character_id=71"));
            Assert.Equal(1, f.Scalar("SELECT COUNT(*) FROM character_invisible_flags WHERE character_id=71 AND slot_index=101"));
            Assert.Equal(5, f.Scalar("SELECT bonus_sp FROM characters WHERE character_id=71"));
            Assert.Equal((int)f.Services.GetRequiredService<Dfo.Server.Abstractions.World.IExperienceRuleService>().CharacterScale().ExperienceForLevel(85), f.Scalar("SELECT exp FROM characters WHERE character_id=71"));
        });
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Water_real_source_and_mixed_skill_batch_rollback_on_late_INSERT_and_retry(bool fail)
    {
        await TestGameplay.RunAsync(async () =>
        {
            await using var f = Fixture(); await f.SeedAsync();
            await SeedSource(f, 3, InventoryContainerType.Main, 69);
            Assert.Equal(CharacterSkillLearningRejection.None, (await f.LearnAsync([new(2, 0, 1)])).Rejection);
            async Task Use()
            {
                var result = await f.LearnAsync([new(1, 1, 2), new(2, 0, 2), new(3, 0, 2)]);
                Assert.Equal(CharacterSkillLearningRejection.None, result.Rejection);
                Assert.Equal([0, 3, 2], result.Receipt!.Skills.Select(x => (int)x.Level));
            }
            if (fail)
            {
                f.Sql("CREATE TRIGGER late_water BEFORE INSERT ON character_skills WHEN NEW.character_id=71 AND NEW.skill_id=3 BEGIN SELECT RAISE(ABORT,'late mixed skill'); END;");
                await Assert.ThrowsAsync<SqliteException>(Use);
                Assert.Equal(2, SourceCount(f, InventoryContainerType.Main, 69));
                Assert.Equal(2, f.Scalar("SELECT level FROM character_skills WHERE character_id=71 AND page_index=0 AND skill_id=1"));
                Assert.Equal(1, f.Scalar("SELECT level FROM character_skills WHERE character_id=71 AND page_index=0 AND skill_id=2"));
                Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM character_skills WHERE character_id=71 AND page_index=0 AND skill_id=3"));
                f.Sql("DROP TRIGGER late_water;");
            }
            await Use();
            Assert.Equal(1, SourceCount(f, InventoryContainerType.Main, 69));
            Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM character_skills WHERE character_id=71 AND page_index=0 AND skill_id=1"));
            Assert.Equal(3, f.Scalar("SELECT level FROM character_skills WHERE character_id=71 AND page_index=0 AND skill_id=2"));
            Assert.Equal(2, f.Scalar("SELECT level FROM character_skills WHERE character_id=71 AND page_index=0 AND skill_id=3"));
            Assert.Equal(1, f.Scalar("SELECT level FROM character_skills WHERE character_id=71 AND page_index=1"));
            Assert.Equal(8, f.Scalar("SELECT level FROM character_skills WHERE character_id=72"));
            Assert.Equal(3, f.Scalar("SELECT bonus_tp FROM characters WHERE character_id=71"));
        });
    }

    [Fact]
    public void Every_concrete_effect_in_the_entire_Abstractions_assembly_has_one_resolvable_handler()
    {
        using var f = new ShopFixture();
        var declared = typeof(DomainEffect).Assembly.GetTypes().Where(x => !x.IsAbstract && typeof(DomainEffect).IsAssignableFrom(x)).OrderBy(x => x.FullName).ToArray();
        var handlers = f.Services.GetServices<IEffectHandler>().ToArray();
        Assert.All(handlers.GroupBy(x => x.EffectType), group => Assert.Single(group));
        Assert.Equal(declared, handlers.Select(x => x.EffectType).OrderBy(x => x.FullName));
        Assert.NotNull(f.Services.GetRequiredService<IApplyEffectsDomainService>());
    }

    private static LearningFixture Fixture() => new(configure: services => {
        services.AddSingleton<IItemPlacementDefinitionSource>(new ConsumablePlacements());
        services.AddSingleton<IItemUseRuleDefinitionSource>(new FakeItemUseRules((24, ItemUseRules.Potion(0)),
            (25, ItemUseRules.Potion(0)), (3, ItemUseRules.Potion(0)), (10_006_007, ItemUseRules.Potion(0))));
        services.AddSingleton<TimeProvider>(new AdvanceableTimeProvider(new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero)));
        services.AddSingleton<ICreatureRuleService>(new PetRules());
        services.AddSingleton<IQuestClearCouponRuleService>(new CouponSelection());
        services.AddSingleton<IQuestContentRuleService>(new CouponContent());
    });
    private sealed class ConsumablePlacements : IItemPlacementDefinitionSource
    {
        public ValueTask<ItemPlacement?> GetAsync(int itemId, CancellationToken ct) =>
            ValueTask.FromResult<ItemPlacement?>(itemId switch
            {
                24 or 25 => new(7, 1000, new(BusinessInventorySpace.CreatureConsumable, 0, 50)),
                3 or 10_006_007 => new(2, 1000, new(BusinessInventorySpace.Consumable, 0, 55)),
                63001 => new(5, 1, new(BusinessInventorySpace.CreatureInventory, 0, 139))
                    { WorldClass = ItemWorldClass.Creature },
                _ => throw new InvalidOperationException($"Undeclared fixture item {itemId}."),
            });
    }

    private static PersistentItemContainer Register(LearningFixture f, InventoryContainerType type)
    {
        var container = new PersistentItemContainer(ContainerId.ForCharacter(f.Session.CharacterId, type),
            new(type, 0), f.Services.GetRequiredService<IItemDomainStore>(), f.Scope,
            f.Services.GetRequiredService<IItemPlacementRuleService>(),
            f.Services.GetRequiredService<IItemSlotRuleService>());
        f.Services.GetRequiredService<ICharacterContainerDomainService>().Register(f.Session.CharacterId, container);
        return container;
    }
    private static async Task SeedSource(LearningFixture f, int item, InventoryContainerType type, int wire)
    {
        var container = Register(f, type); await using var tx = f.Scope.Begin();
        await container.PutAsync(wire, await f.Services.GetRequiredService<IItemLifecycleDomainService>().CreateAsync(
            ItemSnapshot.FromCore(StackableItemCore.Create(type == InventoryContainerType.Pet ? (byte)7 : (byte)2, item, 2), true), default), default);
        await tx.CommitAsync(default);
    }
    private static void SeedPet(LearningFixture f)
    {
        Register(f, InventoryContainerType.Equipment);
        var core = new byte[82]; core[0] = 5;
        BinaryPrimitives.WriteInt32LittleEndian(core.AsSpan(1), 63001); BinaryPrimitives.WriteInt32LittleEndian(core.AsSpan(5), 101);
        f.Sql($"INSERT INTO items(item_uid,item_core,creature_field04,creature_mode_flag,creature_progress_value,creature_mode1_field0a,creature_mode1_field0b,creature_field_after_value,creature_text,creature_tail_flag) VALUES(101,X'{Convert.ToHexString(core)}',50,0,90,0,0,1,X'80',0); INSERT INTO item_slots(owner_scope,owner_id,list_type,slot_index,kind,id,core_uid,count) VALUES('character',71,3,25,'world',101,101,1);");
    }
    private static int SourceCount(LearningFixture f, InventoryContainerType type, int wire) =>
        f.Scalar($"SELECT count FROM item_slots WHERE owner_id=71 AND list_type={(int)type} AND slot_index={wire}");
    private static void AssertPetIdentityAndGrowth(LearningFixture f) => Assert.Equal(1,
        f.Scalar("SELECT COUNT(*) FROM items WHERE item_uid=101 AND substr(item_core,2,4)=X'19F60000' AND substr(item_core,6,4)=X'65000000' AND creature_progress_value=90 AND creature_field_after_value=1 AND creature_mode_flag=0 AND creature_mode1_field0a=0 AND creature_mode1_field0b=0 AND creature_tail_flag=0"));
    private static async ValueTask<int> PreparedScalar(SqliteDatabaseParticipant participant, string sql)
    {
        await using var command = await participant.CommandAsync(default);
        command.CommandText = sql; return Convert.ToInt32(await command.ExecuteScalarAsync());
    }
    private sealed class AfterFlush(Func<Task> check) : ITransactionParticipant
    {
        public bool CommitCanFail => false;
        public async ValueTask PrepareAsync(CancellationToken ct) => await check();
        public ValueTask CommitAsync(CancellationToken ct) => ValueTask.CompletedTask;
        public ValueTask RollbackAsync(CancellationToken ct) => ValueTask.CompletedTask;
    }
    private sealed class PetRules : ICreatureRuleService
    {
        public ValueTask<CreatureRule?> GetAsync(int template, CancellationToken ct) => ValueTask.FromResult<CreatureRule?>(new(template,20,20,true,[100,200,300],[],null,false));
        public ValueTask<CreatureArtifactExperienceRule?> GetArtifactExperienceAsync(int template, CancellationToken ct) => ValueTask.FromResult<CreatureArtifactExperienceRule?>(null);
        public ValueTask<int> BirthExpirationAsync(int template, DateTimeOffset at, CancellationToken ct) => ValueTask.FromResult(0);
    }
    private sealed class CouponSelection : IQuestClearCouponRuleService
    {
        public ValueTask<IReadOnlyList<int>> SelectAsync(int item, byte level, byte job, byte grow, IReadOnlyList<ClearedQuest> cleared, DateTimeOffset at, CancellationToken ct) => ValueTask.FromResult<IReadOnlyList<int>>([101]);
    }
    private sealed class CouponContent : IQuestContentRuleService
    {
        public ValueTask<QuestDefinition?> FindAsync(int id, CancellationToken ct) => ValueTask.FromResult<QuestDefinition?>(id == 2003 ?
            new(id,"",QuestGrade.Normal,null,null,null,null,null,[],"clear quest","",null,null,null,null,null,null,null,[],[],[],[],ClearedQuestRequirements:[101,999,998]) : null);
        public ValueTask<IReadOnlyList<QuestDefinition>> AllAsync(CancellationToken ct) => ValueTask.FromResult<IReadOnlyList<QuestDefinition>>([]);
        public ValueTask<IReadOnlyList<TrainingQuestAvailability>> TrainingAvailabilityAsync(CancellationToken ct) => ValueTask.FromResult<IReadOnlyList<TrainingQuestAvailability>>([]);
        public QuestParameters Parameters() => throw new NotSupportedException();
    }
}
