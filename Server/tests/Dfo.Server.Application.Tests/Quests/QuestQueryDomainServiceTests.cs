using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Application.Quests;
using Dfo.Server.Application.Tests.Inventory;
using Dfo.Server.Data.Inventory;
using Microsoft.Extensions.DependencyInjection;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.Quests;

public sealed class QuestQueryDomainServiceTests
{
    [Theory]
    [InlineData(0, 511)]
    [InlineData(999, 1)]
    [InlineData(1000, 0)]
    public async Task Shared_sparse_zero_excludes_legacy_main_copies_and_preserves_visit_channel(int shared, int missing)
    {
        var content = new OrdinaryQuestEffectTests.Rules();
        content.Quest = content.Quest with { ObjectiveTypeCode = "seek n meet npc", SeekingItems = [new(3034, 1000)] };
        var rules = new QuestStateRuleService(content);
        var facts = rules.Holdings([new ItemSnapshot { ItemId = 3034, Count = 5000 }],
            shared == 0 ? [] : [new ItemSnapshot { ItemId = 3034, Count = shared }], 0);
        var stored = new CarriedQuest(0, 2001, 0x200 | 17);
        var result = Assert.Single(await rules.EvaluateAsync([stored], [], facts, default));
        Assert.Equal(0x200 | missing, result.Quest.TriggerValue);
        Assert.False(result.CanComplete); // Visiting the NPC is still outstanding.
        Assert.Equal(shared, Assert.Single(result.Items).Held);
        Assert.Equal(0x200 | 17, stored.TriggerValue);
    }

    [Fact]
    public async Task Gold_and_material_deficits_share_one_rule_without_erasing_other_channels()
    {
        var content = new OrdinaryQuestEffectTests.Rules();
        content.Quest = content.Quest with { SeekingItems = [new(0, 100), new(1000, 10)] };
        var rules = new QuestStateRuleService(content);
        var facts = rules.Holdings([new ItemSnapshot { ItemId = 1000, Count = 9 }], [], 98);
        var result = Assert.Single(await rules.EvaluateAsync([new(0, 2001, 0x40000)], [], facts, default));
        Assert.Equal(0x40000 | 3, result.Quest.TriggerValue);
        Assert.Equal(new long[] { 2, 1 }, result.Items.Select(x => x.Missing));
    }

    [Fact]
    public async Task Queries_are_read_only_and_agree_with_completion_preview_and_progress_refresh()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var content = new OrdinaryQuestEffectTests.Rules();
            content.Quest = content.Quest with { ObjectiveTypeCode = "seeking", SeekingItems = [new(3034, 1000), new(1000, 10)] };
            content.Others.Add(content.Quest with { QuestId = 2002, Grade = QuestGrade.Epic, SeekingItems = [] });
            using var shop = new ShopFixture(s => s.AddSingleton<IQuestContentRuleService>(content));
            await shop.SeedShared(3034, 1000);
            await shop.Seed(121, 1000, 10, 3);
            shop.Sql("INSERT INTO character_active_quests(character_id,slot,quest_id,trigger_value) VALUES(1,0,2001,1); INSERT INTO character_invisible_flags(character_id,slot_index,flag_value) VALUES(1,2001,1),(1,2002,1)");
            var queries = shop.Services.GetRequiredService<IQuestQueryDomainService>();
            var status = await queries.QueryAsync(new(1), new(1), 2001, default);
            Assert.Equal(QuestLifecycle.Accepted, status.Lifecycle); // Current run wins over historical clear.
            Assert.True(status.Accepted!.CanComplete);
            Assert.False(status.CanAccept);
            var accepted = Assert.Single(await queries.ReadAcceptedAsync(new(1), new(1), default));
            Assert.Equal(status.Accepted.Quest, accepted.Quest);
            Assert.Equal(QuestLifecycle.Completed, (await queries.QueryAsync(new(1), new(1), 2002, default)).Lifecycle);
            Assert.False((await queries.QueryAsync(new(1), new(1), 99999, default)).IsKnown);
            Assert.DoesNotContain((await queries.ReadAvailableAsync(new(1), new(1), default)).Quests, x => x.QuestId is 2001 or 2002);
            Assert.Equal(1, shop.Scalar("SELECT trigger_value FROM character_active_quests WHERE quest_id=2001"));
            Assert.Equal(1000, shop.SharedCount(3034));
            Assert.Equal(10, shop.Count(1000));
            Assert.Equal(100000, shop.Scalar("SELECT carried_gold FROM character_stats WHERE character_id=1"));
            await using var transaction = shop.Services.GetRequiredService<Dfo.Server.Application.Transactions.DomainTransactionScope>().Begin();
            var stored = await shop.Services.GetRequiredService<IConsumableStateDomainService>().QuestsAsync(new(1), new(1), default);
            var facts = await shop.Services.GetRequiredService<IQuestFactsDomainService>().HoldingsAsync(new(1), default);
            var effective = await shop.Services.GetRequiredService<IQuestStateRuleService>().EvaluateAsync(
                stored.Carried.Select(x => new CarriedQuest(x.Slot, x.QuestId, x.TriggerValue)).ToArray(),
                stored.Cleared.Select(x => new ClearedQuest(x.QuestId, x.Mark)).ToArray(), facts, default);
            Assert.Equal(accepted.Quest, Assert.Single(effective).Quest);
            var containers = shop.Services.GetRequiredService<ICharacterContainerDomainService>();
            var preview = await shop.Services.GetRequiredService<IQuestCompletionEffectRuleService>().EffectiveAsync(stored,
                (await containers.GetContainer(new(1), InventoryContainerType.Main).GetAllAsync(default)).Select(x => x.Snapshot),
                100000, default, (await containers.GetContainer(new(1), InventoryContainerType.SharedMaterial).GetAllAsync(default)).Select(x => x.Snapshot));
            Assert.Equal(accepted.Quest, Assert.Single(preview));
            Assert.Empty(await shop.Services.GetRequiredService<IQuestMaterialRequirementDomainService>().GetOutstandingMaterialIdsAsync(new(1), new(1), default));
            var refreshed = await shop.Services.GetRequiredService<ISeekingQuestProgressDomainService>().SynchronizeAsync(new(1), new(1), default);
            Assert.Equal(accepted.Quest, Assert.Single(refreshed!.Quests));
            // Uncommitted derived writes do not alter the database or assets.
            Assert.Equal(1, shop.Scalar("SELECT trigger_value FROM character_active_quests WHERE quest_id=2001"));
            Assert.Equal(1000, shop.SharedCount(3034));
        });
    }

    [Fact]
    public async Task Availability_is_quest_owned_and_keeps_repeatable_and_special_categories()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var content = new OrdinaryQuestEffectTests.Rules();
            content.Others.AddRange(new[]
            {
                content.Quest with { QuestId = 2002, Grade = QuestGrade.Daily },
                content.Quest with { QuestId = 2003, Grade = QuestGrade.DailyRandom },
                content.Quest with { QuestId = 2004, Grade = QuestGrade.Circle },
                content.Quest with { QuestId = 2005, Grade = QuestGrade.Crack },
                content.Quest with { QuestId = 2006, Grade = QuestGrade.Title },
                content.Quest with { QuestId = 2007, RequiredLevel = new(50, 99) },
            });
            using var shop = new ShopFixture(s => s.AddSingleton<IQuestContentRuleService>(content));
            shop.Sql("INSERT INTO character_invisible_flags(character_id,slot_index,flag_value) VALUES(1,2002,1),(1,2003,1)");
            var queries = shop.Services.GetRequiredService<IQuestQueryDomainService>();
            Assert.Equal(new[] { 2001, 2002 }, (await queries.ReadAvailableAsync(new(1), new(1), default)).Quests.Select(x => x.QuestId));
            var repeat = await queries.QueryAsync(new(1), new(1), 2002, default);
            Assert.Equal(QuestLifecycle.Completed, repeat.Lifecycle);
            Assert.True(repeat.CanAccept);
            shop.SetLevel(60, 1);
            Assert.Contains((await queries.ReadAvailableAsync(new(1), new(1), default)).Quests, x => x.QuestId == 2007);
            await Assert.ThrowsAsync<InvalidDataException>(async () => await queries.QueryAsync(new(2), new(1), 2001, default));
        });
    }
}
