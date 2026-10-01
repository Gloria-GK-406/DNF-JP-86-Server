using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Tests.Characters;
using Dfo.Server.Application.Tests.Inventory;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;
using Dfo.Server.Persistence.Database;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Quests;

public sealed class QuestClearCouponDomainServiceTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly BusinessSlot Slot = new(BusinessInventorySpace.Consumable, 4);

    [Fact]
    public async Task Coupon_clears_carried_and_uncarried_removes_tracking_holes_and_updates_packed_dependents_without_rewards()
    {
        await using var f = Fixture(); await f.SeedAsync(); SeedQuests(f);
        var result = await Use(f);
        Assert.Equal([101, 102], result.CompletedQuestIds);
        Assert.Equal(4, f.Scalar("SELECT bonus_sp FROM characters WHERE character_id=71"));
        Assert.Equal(2, f.Scalar("SELECT flag_value FROM character_invisible_flags WHERE character_id=71 AND slot_index=103"));
        Assert.Equal(1, f.Scalar("SELECT COUNT(*) FROM character_active_quests WHERE character_id=71"));
        Assert.Equal((0x05040303 & ~0x1FF) | 1, f.Scalar("SELECT trigger_value FROM character_active_quests WHERE character_id=71 AND quest_id=2003"));
        Assert.Equal(1, f.Scalar("SELECT COUNT(*) FROM character_quest_notify_selections WHERE character_id=71"));
        Assert.Equal(9, f.Scalar("SELECT slot_index FROM character_quest_notify_selections WHERE character_id=71"));
        Assert.Equal(0, f.Scalar("SELECT exp FROM characters WHERE character_id=71"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Coupon_failure_restores_cost_all_quest_sections_and_dependent_progress(bool failCost)
    {
        await using var f = Fixture(); await f.SeedAsync(); SeedQuests(f);
        f.Sql(failCost
            ? "CREATE TRIGGER fail_cost BEFORE UPDATE OF bonus_sp ON characters BEGIN SELECT RAISE(ABORT,'cost'); END"
            : "CREATE TRIGGER fail_clear BEFORE INSERT ON character_invisible_flags BEGIN SELECT RAISE(ABORT,'clear'); END");
        await Assert.ThrowsAsync<SqliteException>(()=>Use(f));
        Assert.Equal(5, f.Scalar("SELECT bonus_sp FROM characters WHERE character_id=71"));
        Assert.Equal(2, f.Scalar("SELECT COUNT(*) FROM character_active_quests WHERE character_id=71"));
        Assert.Equal(0x05040303, f.Scalar("SELECT trigger_value FROM character_active_quests WHERE character_id=71 AND quest_id=2003"));
        Assert.Equal(3, f.Scalar("SELECT COUNT(*) FROM character_quest_notify_selections WHERE character_id=71"));
        Assert.Equal(1, f.Scalar("SELECT COUNT(*) FROM character_invisible_flags WHERE character_id=71"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task No_eligible_or_already_cleared_selection_cannot_consume(bool already)
    {
        await using var f = Fixture(already ? [101] : []); await f.SeedAsync();
        if (already) f.Sql("INSERT INTO character_invisible_flags VALUES(71,101,2)");
        await Assert.ThrowsAsync<OpeningRejectedException>(()=>Use(f));
        Assert.Equal(5, f.Scalar("SELECT bonus_sp FROM characters WHERE character_id=71"));
    }

    [Fact]
    public async Task Another_item_does_not_change_quest_state_or_consume()
    {
        await using var f = Fixture(sourceId: 8474); await f.SeedAsync(); SeedQuests(f);
        await Assert.ThrowsAsync<OpeningRejectedException>(()=>Use(f));
        Assert.Equal(5, f.Scalar("SELECT bonus_sp FROM characters WHERE character_id=71"));
        Assert.Equal(2, f.Scalar("SELECT COUNT(*) FROM character_active_quests WHERE character_id=71"));
    }

    private static async Task<QuestClearCouponResult> Use(LearningFixture f)
    {
        await using var tx = f.Scope.Begin();
        var result = await f.Services.GetRequiredService<IQuestClearCouponDomainService>().UseAsync(f.Session,Slot,At,default);
        await tx.CommitAsync(default); return result;
    }
    private static LearningFixture Fixture(int[]? selected = null, int sourceId = 10_006_007) => new(configure: s =>
    {
        s.AddSingleton<IQuestClearCouponRuleService>(new Selection(selected ?? [101,102,101,103]));
        s.AddSingleton<IQuestContentRuleService>(new Content());
        s.AddSingleton<IItemUseRuleDefinitionSource>(new FakeItemUseRules((10_006_007, ItemUseRules.Potion(0))));
        s.AddSingleton<IOpeningInventoryDomainService>(p => new PaymentInventory(p.GetRequiredService<SqliteDatabase>(),
            p.GetRequiredService<IApplyEffectsDomainService>(),sourceId));
    });
    private static void SeedQuests(LearningFixture f) => f.Sql("""
        INSERT INTO character_active_quests VALUES(71,0,101,9),(71,2,2003,0x05040303);
        INSERT INTO character_quest_notify_selections VALUES(71,2,101),(71,7,101),(71,9,2003);
        INSERT INTO character_invisible_flags VALUES(71,103,2);
        """);
    private sealed class Selection(int[] ids) : IQuestClearCouponRuleService
    {
        public ValueTask<IReadOnlyList<int>> SelectAsync(int itemId, byte level, byte job, byte grow,
            IReadOnlyList<ClearedQuest> cleared, DateTimeOffset now, CancellationToken ct) => ValueTask.FromResult<IReadOnlyList<int>>(ids);
    }
    private sealed class Content : IQuestContentRuleService
    {
        public ValueTask<QuestDefinition?> FindAsync(int id, CancellationToken ct) => ValueTask.FromResult<QuestDefinition?>(
            id==2003 ? new QuestDefinition(id, "", QuestGrade.Normal, null, null, null, null, null, [], "clear quest", "",
                null, null, null, null, null, null, null, [], [], [], [], ClearedQuestRequirements:[101,102,999]) : null);
        public ValueTask<IReadOnlyList<QuestDefinition>> AllAsync(CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<IReadOnlyList<TrainingQuestAvailability>> TrainingAvailabilityAsync(CancellationToken ct) => throw new NotSupportedException();
        public QuestParameters Parameters() => throw new NotSupportedException();
    }
    // Payment is a same-database scalar stand-in; quest state and its effects are production SQLite.
    private sealed class PaymentInventory(SqliteDatabase db,IApplyEffectsDomainService effects,int itemId) : IOpeningInventoryDomainService
    {
        public ValueTask<ContainerSlotItem> SourceAsync(SelectedSessionIdentity s,BusinessSlot slot,DateTimeOffset at,CancellationToken ct) =>
            ValueTask.FromResult(new ContainerSlotItem(slot.Index,StackableItemCore.Create(2,itemId,2),ItemWorldClass.Interchangeable));
        public ValueTask<IReadOnlyList<Dfo.Server.Data.Accounts.AccountPremiumEntryState>> ReadPremiumsAsync(SelectedSessionIdentity s,CancellationToken ct) => throw new NotSupportedException();
        public async ValueTask<OpeningEffectsApplication> ApplyAsync(SelectedSessionIdentity s,IReadOnlyList<DomainEffect> plan,DateTimeOffset at,CancellationToken ct)
        {
            Assert.Equal(1,Assert.Single(plan.OfType<ConsumeFromContainerEffect>()).Count);
            await db.ExecuteAsync("UPDATE characters SET bonus_sp=bonus_sp-1 WHERE character_id=71", _=>{},ct);
            var applied = await effects.ApplyAsync(new(s.AccountId,s.CharacterId,at,s),plan.Where(x=>x is not ConsumeFromContainerEffect).ToArray(),ct);
            return new(new(s,[],[],[],at),applied);
        }
    }
}
