using Dfo.Server.Abstractions.Achievements;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Achievements;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Achievements;

public sealed partial class AchievementTitleBookTests
{
    private static void PrepareQuestSettlement(Fixture f)
    {
        f.Sql("INSERT OR IGNORE INTO character_stats(character_id) VALUES(1)");
        f.Services.GetRequiredService<ICharacterContainerDomainService>().Register(Character,
            new PersistentItemContainer(ContainerId.ForCharacter(Character, InventoryContainerType.Main), new(InventoryContainerType.Main, 0),
                f.Services.GetRequiredService<IItemDomainStore>(), f.Services.GetRequiredService<IDomainTransactionAccessor>(),
                f.Services.GetRequiredService<IItemPlacementRuleService>(), f.Services.GetRequiredService<IItemSlotRuleService>()));
    }

    private static async ValueTask HuntQuestReadyAsync(Fixture f)
    {
        PrepareQuestSettlement(f);
        var definition = (await f.Services.GetRequiredService<IQuestContentRuleService>().FindAsync(4303, default))!;
        var log = f.Services.GetRequiredService<ICharacterQuestLogDomainService>().LogOf(new(1), Character);
        Assert.True((await f.WriteAsync(async () => await log.AcceptAsync(definition, default))).Accepted);
        // Each verified enemy death is committed by its own gameplay command.
        for (var i = 0; i < 30; i++)
            await f.WriteAsync(async () => await f.Services.GetRequiredService<IQuestProgressDomainService>().ReportAsync(new(1), Character,
                [new QuestEnemyDefeated(1, DungeonDifficulty.Normal, 100, HuntEnemyTargetTypes.Monster, MonsterRank.Ordinary)], default));
        Assert.Equal(0, Assert.Single(await log.CarriedAsync(default)).TriggerValue);
    }

    [PvfFact]
    public async Task Ready_task_needs_formal_finish_and_grants_one_book_title_across_replay_and_restart()
    {
        await using var f = new Fixture();
        output.WriteLine("Real PVF: task 4303 -> passive unlock 6501 -> title 26596.");
        await f.WriteAsync(async () => await f.Achievements.PrepareAsync(Character, 80, default));
        await HuntQuestReadyAsync(f);
        await f.DrainAsync();
        Assert.Equal(1, (await f.Achievements.GetAsync(Character, default)).Single(x => x.AchievementId == 6501).P1);
        Assert.Empty(f.ItemUids(26596));
        var completion = f.Services.GetRequiredService<IQuestCompletionDomainService>();
        Assert.True((await f.WriteAsync(async () => await completion.FinishAsync(new(1), Character, 4303, ushort.MaxValue, 1, DateTimeOffset.UtcNow, default))).Finished);
        Assert.Empty(f.ItemUids(26596)); // Reward is delivered through the committed quest event.
        await f.DrainAsync();
        var uid = Assert.Single(f.ItemUids(26596));
        Assert.Contains((await f.Titles.GetAsync(Character, default)).SelectMany(x => x.Entries), x => x.ItemId == 26596);
        Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM item_slots WHERE list_type=1 AND core_uid=" + uid));
        Assert.False((await f.WriteAsync(async () => await completion.FinishAsync(new(1), Character, 4303, ushort.MaxValue, 1, DateTimeOffset.UtcNow, default))).Finished);
        await f.SendAsync(new QuestCompleted(Character, 4303));
        await f.DrainAsync();
        await using var restored = new Fixture(f.Path);
        await restored.WriteAsync(async () => await restored.Titles.PrepareAsync(Character, default));
        Assert.Equal(uid, Assert.Single(f.ItemUids(26596)));
    }

    [PvfFact]
    public async Task Rolled_back_task_finish_does_not_clear_or_unlock_or_enqueue_reward()
    {
        await using var f = new Fixture();
        await HuntQuestReadyAsync(f);
        var events = f.Scalar("SELECT COUNT(*) FROM domain_events");
        await using (var tx = f.Scope.Begin())
            Assert.True((await f.Services.GetRequiredService<IQuestCompletionDomainService>().FinishAsync(new(1), Character, 4303, ushort.MaxValue, 1, DateTimeOffset.UtcNow, default)).Finished);
        Assert.Equal(events, f.Scalar("SELECT COUNT(*) FROM domain_events"));
        Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM character_invisible_flags WHERE slot_index=4303"));
        Assert.Single(await f.Services.GetRequiredService<ICharacterQuestLogDomainService>().LogOf(new(1), Character).CarriedAsync(default));
        await f.DrainAsync();
        Assert.Empty(f.ItemUids(26596));
    }

    [PvfFact]
    public async Task Title_reward_is_routed_to_the_book_and_an_unmapped_reward_is_refused()
    {
        await using var f = new Fixture();
        PrepareQuestSettlement(f);
        await using var tx = f.Scope.Begin();
        var character = f.Services.GetRequiredService<ICharacterDomainService>().Get(Character);
        var facts = new QuestCompletionFacts((await character.IdentityAsync(default))!, (await character.ProgressAsync(default))!,
            await f.Services.GetRequiredService<IConsumableStateDomainService>().QuestsAsync(new(1), Character, default), [],
            (await f.Services.GetRequiredService<ICharacterCarriedGoldTable>().FindAsync(Character, default))!, null, null);
        var definition = (await f.Services.GetRequiredService<IQuestContentRuleService>().FindAsync(4303, default))!;
        var rules = f.Services.GetRequiredService<IQuestCompletionEffectRuleService>();
        var mapped = rules.Rewards(definition, facts, ushort.MaxValue, 1);
        Assert.True(mapped.IsValid);
        Assert.DoesNotContain(mapped.Plan!.Items, x => x.ItemId == 26596);
        var unmapped = rules.Rewards(definition with { QuestId = 29999 }, facts, ushort.MaxValue, 1);
        Assert.False(unmapped.IsValid);
        Assert.Equal(QuestRewardRejection.UnmappedTitleReward, unmapped.Rejection);
    }

    [PvfTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Obsolete_task_counters_and_stale_completion_events_cannot_unlock_a_task_title(bool badDependent)
    {
        await using var f = new Fixture();
        await f.WriteAsync(async () => await f.Achievements.PrepareAsync(Character, 80, default));
        f.Sql("INSERT INTO character_achievement_complete(character_id,sort_order,achievement_id,p1,p2,p3,p4) VALUES(1,999,4303,0,0,0,0)");
        if (badDependent) f.Sql("UPDATE character_achievement_complete SET p1=0 WHERE achievement_id=6501");
        await f.WriteAsync(async () => await f.Services.GetRequiredService<TitleBookDomainService>().HandleAsync(
            new DomainEventEnvelope<AchievementCompleted>(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid(), new(Character, 6501)), default));
        await f.WriteAsync(async () => await f.Titles.PrepareAsync(Character, default));
        Assert.Empty(f.ItemUids(26596));
        await f.WriteAsync(async () => await f.Achievements.PrepareAsync(Character, 80, default));
        await f.WriteAsync(async () => await f.Achievements.UnlockSatisfiedAchievementsAsync(Character, default));
        await f.DrainAsync();
        Assert.Equal(1, (await f.Achievements.GetAsync(Character, default)).Single(x => x.AchievementId == 6501).P1);
        Assert.DoesNotContain(await f.Achievements.GetAsync(Character, default), x => x.AchievementId == 4303);
        Assert.Equal(1, f.Scalar("SELECT COUNT(*) FROM character_achievement_complete WHERE achievement_id=4303"));
        Assert.Empty(f.ItemUids(26596));
    }

    [PvfFact]
    public async Task Login_recovers_a_real_finished_task_and_preserves_previously_activated_titles()
    {
        await using var f = new Fixture();
        f.Sql("INSERT INTO character_invisible_flags(character_id,slot_index,flag_value) VALUES(1,4303,1)");
        await f.WriteAsync(async () => await f.Achievements.PrepareAsync(Character, 80, default));
        await f.DrainAsync();
        var uid = Assert.Single(f.ItemUids(26596));
        f.Sql("DELETE FROM character_invisible_flags WHERE slot_index=4303");
        await f.WriteAsync(async () => await f.Achievements.PrepareAsync(Character, 80, default));
        await f.WriteAsync(async () => await f.Titles.PrepareAsync(Character, default));
        await f.DrainAsync();
        Assert.Equal(uid, Assert.Single(f.ItemUids(26596)));
        Assert.Equal(0, (await f.Achievements.GetAsync(Character, default)).Single(x => x.AchievementId == 6501).P1);
    }

    [PvfFact]
    public async Task Task_acceptance_owns_hunt_progress_instead_of_a_default_passive_counter()
    {
        await using var f = new Fixture();
        var rules = f.Services.GetRequiredService<IAchievementRuleService>();
        Assert.DoesNotContain(rules.Catalog(), x => x.Id == 4303);
        Assert.Contains(rules.Catalog(), x => x.Id == 6501);
        await f.WriteAsync(async () => await f.Achievements.PrepareAsync(Character, 80, default));
        var log = f.Services.GetRequiredService<ICharacterQuestLogDomainService>().LogOf(new(1), Character);
        var progress = f.Services.GetRequiredService<IQuestProgressDomainService>();
        var death = new QuestEnemyDefeated(1, DungeonDifficulty.Normal, 100, HuntEnemyTargetTypes.Monster, MonsterRank.Ordinary);
        await f.WriteAsync(async () => await progress.ReportAsync(new(1), Character, [death], default));
        Assert.Empty(await log.CarriedAsync(default));
        Assert.DoesNotContain(await f.Achievements.GetAsync(Character, default), x => x.AchievementId == 4303);

        var definition = (await f.Services.GetRequiredService<IQuestContentRuleService>().FindAsync(4303, default))!;
        var acceptance = await f.WriteAsync(async () => await log.AcceptAsync(definition, default));
        Assert.True(acceptance.Accepted);
        Assert.Equal(30, Assert.Single(await log.CarriedAsync(default)).TriggerValue);
        await f.WriteAsync(async () => await progress.ReportAsync(new(1), Character,
            [death with { Rank = MonsterRank.Boss }], default));
        Assert.Equal(30, Assert.Single(await log.CarriedAsync(default)).TriggerValue);
        await f.WriteAsync(async () => await progress.ReportAsync(new(1), Character, [death], default));
        Assert.Equal(29, Assert.Single(await log.CarriedAsync(default)).TriggerValue);
        Assert.True(await f.WriteAsync(async () => await log.GiveUpAsync(4303, default)));
        await f.WriteAsync(async () => await progress.ReportAsync(new(1), Character, [death], default));
        Assert.Empty(await log.CarriedAsync(default));
        Assert.True((await f.WriteAsync(async () => await log.AcceptAsync(definition, default))).Accepted);
        Assert.Equal(30, Assert.Single(await log.CarriedAsync(default)).TriggerValue);
    }
}
