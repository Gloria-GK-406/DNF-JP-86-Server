using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Tests.Characters;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;
using Dfo.Server.Persistence.Database;
using Dfo.Server.Protocol.Game.LevelUpTicket;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class LevelUpTicketTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 7, 10, 0, 0, TimeSpan.Zero);
    private static readonly BusinessSlot Slot = new(BusinessInventorySpace.Consumable, 4);
    private const int ItemId = 10006124;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Real_command_and_inventory_persist_one_ticket_or_roll_back_on_level_failure(bool failLevel)
    {
        using var fixture = new CeraShopFixture(configure: services =>
        {
            services.AddSingleton<IStackableActionDefinitionSource>(new ActionSource(1));
            services.AddSingleton<IItemPlacementDefinitionSource>(new OpeningContent());
            services.AddSingleton<IItemUseRuleDefinitionSource>(new FakeItemUseRules((ItemId, ItemUseRules.Potion(0))));
            services.AddSingleton<IQuestContentRuleService>(new QuestContent());
            services.AddSingleton<ICharacterStatDefinitionSource>(new TestCharacterContent());
            services.AddSingleton<ISkillRuleDefinitionSource>(new LearningContent());
        });
        var host = fixture.Host;
        host.Sql("INSERT INTO character_condition(character_id,pc_room_state) VALUES(1,0)");
        await host.Seed(5, ItemId, 10);
        if (failLevel)
            host.Sql("CREATE TRIGGER fail_ticket BEFORE UPDATE OF exp ON characters BEGIN SELECT RAISE(ABORT,'level'); END");
        var legacy=typeof(ProtocolModule).Assembly.GetType("Dfo.Server.Protocol.Game.LevelUpTicket.UseLevelUpTicketCommand");
        if(legacy is not null)
        {
            async Task<LevelUpTicketResult> UseCommand()=>await host.Services.GetRequiredService<ICommandBus>().SendAsync((ICommand<LevelUpTicketResult>)Activator.CreateInstance(legacy,host.OnlineUser,host.Session,5)!);
            if(failLevel) await Assert.ThrowsAsync<SqliteException>(UseCommand);
            else {var result=await UseCommand();Assert.Equal((byte)86,result.Progress.Level);Assert.Equal(9,Assert.Single(result.Inventory.Updates).Core.ToCore().Value);}
        }
        else
        {
            await EffectRequestTransport.SendRaw(host,new(1,0x1A2,0,7),[5,0,0]);
            var response=Assert.Single(host.Sender.Messages.OfType<LevelUpTicketResponse>());
            Assert.Equal(failLevel ? (byte?)99 : null,response.ErrorCode);
            if(!failLevel) Assert.Equal((byte)86,Assert.Single(host.Sender.Messages.OfType<Dfo.Server.Protocol.Game.MonsterDeath.ExperienceNotification>()).Level);
        }
        Assert.Equal(failLevel ? 10 : 9, host.Count(ItemId));
        Assert.Equal(failLevel ? 85 : 86, host.Scalar("SELECT level FROM characters WHERE character_id=1"));
        Assert.Equal(failLevel ? 0 : 2, host.Scalar("SELECT COUNT(*) FROM character_invisible_flags WHERE character_id=1"));
    }

    [Fact]
    public async Task Ticket_reaches_next_level_start_and_clears_only_old_visible_story_stages_without_rewards()
    {
        await TestGameplay.RunAsync(async () =>
        {
            await using var f = Fixture(); await Seed(f, 17);
            f.Sql("INSERT INTO character_active_quests VALUES(71,0,101,9),(71,2,102,9); INSERT INTO character_quest_notify_selections VALUES(71,0,101),(71,1,102)");
            var result = await Use(f);
            Assert.Equal((byte)18, result.Progress.Level);
            Assert.Equal(Scale(f).ExperienceForLevel(18), result.Progress.Experience);
            Assert.Equal(new[] { 101 }, result.CompletedQuestIds.ToArray());
            Assert.Equal(4, f.Scalar("SELECT bonus_sp FROM characters WHERE character_id=71"));
            Assert.Equal(1, f.Scalar("SELECT flag_value FROM character_invisible_flags WHERE character_id=71 AND slot_index=101"));
            Assert.Equal(102, f.Scalar("SELECT quest_id FROM character_active_quests WHERE character_id=71"));
            Assert.Equal(102, f.Scalar("SELECT quest_id FROM character_quest_notify_selections WHERE character_id=71"));
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_quest_or_level_write_failure_rolls_back_the_cost_level_and_quest_log(bool failLevel)
    {
        await TestGameplay.RunAsync(async () =>
        {
            await using var f = Fixture(); await Seed(f, 17);
            f.Sql("INSERT INTO character_active_quests VALUES(71,0,101,9)");
            f.Sql(failLevel ? "CREATE TRIGGER fail_ticket BEFORE UPDATE OF exp ON characters BEGIN SELECT RAISE(ABORT,'level'); END"
                : "CREATE TRIGGER fail_ticket BEFORE INSERT ON character_invisible_flags BEGIN SELECT RAISE(ABORT,'quest'); END");
            await Assert.ThrowsAsync<SqliteException>(() => Use(f));
            Assert.Equal(17, f.Scalar("SELECT level FROM characters WHERE character_id=71"));
            Assert.Equal(5, f.Scalar("SELECT bonus_sp FROM characters WHERE character_id=71"));
            Assert.Equal(1, f.Scalar("SELECT COUNT(*) FROM character_active_quests WHERE character_id=71"));
            Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM character_invisible_flags WHERE character_id=71"));
        });
    }

    [Theory]
    [InlineData(86, false)]
    [InlineData(17, true)]
    public async Task Cap_and_hardcore_restrictions_do_not_consume_or_clear(byte level, bool hardcore)
    {
        await TestGameplay.RunAsync(async () =>
        {
            await using var f = Fixture(); await Seed(f, level);
            if (hardcore) f.Sql("UPDATE character_condition SET is_hardcore_mode=1 WHERE character_id=71");
            await Assert.ThrowsAsync<OpeningRejectedException>(() => Use(f));
            Assert.Equal(level, f.Scalar("SELECT level FROM characters WHERE character_id=71"));
            Assert.Equal(5, f.Scalar("SELECT bonus_sp FROM characters WHERE character_id=71"));
            Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM character_invisible_flags WHERE character_id=71"));
        });
    }

    [Fact]
    public async Task No_old_quests_is_still_a_valid_upgrade_and_multi_level_range_is_capped_at_server_limit()
    {
        await TestGameplay.RunAsync(async () =>
        {
            await using var f = Fixture(increase: 10); await Seed(f, 85);
            f.Sql("INSERT INTO character_invisible_flags VALUES(71,101,1),(71,102,1)");
            var result = await Use(f);
            Assert.Equal((byte)86, result.Progress.Level);
            Assert.Empty(result.CompletedQuestIds);
            Assert.Equal(4, f.Scalar("SELECT bonus_sp FROM characters WHERE character_id=71"));
            Assert.Equal(Scale(f).MaximumExperience, result.Progress.Experience);
        });
    }

    private static CharacterExperienceScale Scale(LearningFixture f) => f.Services.GetRequiredService<IExperienceRuleService>().CharacterScale();
    private static async Task Seed(LearningFixture f, byte level)
    {
        await f.SeedAsync();
        f.Sql($"UPDATE characters SET level={level},exp={Scale(f).ExperienceForLevel(level)} WHERE character_id=71; INSERT INTO character_condition(character_id,pc_room_state) VALUES(71,0)");
    }
    private static async Task<LevelUpTicketResult> Use(LearningFixture f)
    {
        await using var tx = f.Scope.Begin();
        var result = await TestEffectFootprints.Run(f.Session, cap => f.Services.GetRequiredService<ILevelUpTicketDomainService>().UseAsync(f.Session, Slot, At, cap, default));
        await tx.CommitAsync(default); return result;
    }
    private static LearningFixture Fixture(int increase = 1) => new(configure: s =>
    {
        s.AddSingleton<ICharacterStatDefinitionSource>(new TestCharacterContent());
        s.AddSingleton<IStackableActionDefinitionSource>(new ActionSource(increase));
        s.AddSingleton<IItemUseRuleDefinitionSource>(new FakeItemUseRules((ItemId, ItemUseRules.Potion(0))));
        s.AddSingleton<IQuestContentRuleService>(new QuestContent());
        s.AddSingleton<IOpeningInventoryDomainService>(p => new PaymentInventory(p.GetRequiredService<SqliteDatabase>(), p.GetRequiredService<IApplyEffectsDomainService>()));
    });
    private sealed class ActionSource(int increase) : IStackableActionDefinitionSource
    {
        public ValueTask<StackableActionDefinition?> GetAsync(int id, CancellationToken ct) => ValueTask.FromResult<StackableActionDefinition?>(
            new("[level up ticket]", 1, 85, 0, true) { LevelIncrease = increase, BlockedInHardcore = true });
    }
    private sealed class QuestContent : IQuestContentRuleService
    {
        private static QuestDefinition Quest(int id, int stage) => new(id, "", QuestGrade.Epic, null, new(17, 99), null,
            null, null, [], "meet npc", "", true, false, null, null, null, null, null, [], [], [], []) { StoryStageLevel = stage };
        private readonly QuestDefinition[] all = [Quest(101, 17), Quest(102, 19), Quest(103, 17) with { IsEventOnly = true },
            Quest(104, 17) with { IsExposedByNpc = false }, Quest(105, 17) with { StoryStageLevel = null }];
        public ValueTask<IReadOnlyList<QuestDefinition>> AllAsync(CancellationToken ct) => ValueTask.FromResult<IReadOnlyList<QuestDefinition>>(all);
        public ValueTask<QuestDefinition?> FindAsync(int id, CancellationToken ct) => ValueTask.FromResult(all.SingleOrDefault(x => x.QuestId == id));
        public ValueTask<IReadOnlyList<TrainingQuestAvailability>> TrainingAvailabilityAsync(CancellationToken ct) => throw new NotSupportedException();
        public QuestParameters Parameters() => throw new NotSupportedException();
    }
    // A same-database scalar cost; progression, skills and quest writes use production SQLite services.
    private sealed class PaymentInventory(SqliteDatabase db, IApplyEffectsDomainService effects) : IOpeningInventoryDomainService
    {
        public ValueTask<ItemReference> SourceAsync(SelectedSessionIdentity session, BusinessSlot slot, DateTimeOffset at, CancellationToken ct) =>
            ValueTask.FromResult(TestItemReferences.FromCore(slot.Index, StackableItemCore.Create(2, ItemId, 10), ItemWorldClass.Interchangeable));
        public ValueTask<IReadOnlyList<Dfo.Server.Data.Accounts.AccountPremiumEntryState>> ReadPremiumsAsync(SelectedSessionIdentity session, CancellationToken ct) => throw new NotSupportedException();
        public async ValueTask<OpeningEffectsApplication> ApplyAsync(SelectedSessionIdentity s, IReadOnlyList<DomainEffect> plan, DateTimeOffset at, IEffectFootprintRecorder footprint, CancellationToken ct)
        {
            Assert.Equal(1, Assert.Single(plan.OfType<ConsumeFromContainerEffect>()).Count);
            await db.ExecuteAsync("UPDATE characters SET bonus_sp=bonus_sp-1 WHERE character_id=71", _ => { }, ct);
            var applied = await effects.ApplyAsync(new(s.AccountId, s.CharacterId, at, footprint, s), plan.Where(x => x is not ConsumeFromContainerEffect).ToArray(), ct);
            return new(new(s, [], [], [], at), applied);
        }
    }
}
