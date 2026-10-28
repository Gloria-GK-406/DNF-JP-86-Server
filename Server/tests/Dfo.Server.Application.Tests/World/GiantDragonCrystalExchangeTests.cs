using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Settlement;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.World;

public sealed partial class GiantDragonDungeonTests
{
    [SkippableFact]
    public async Task Crystal_exchange_late_abort_rolls_back_quota_materials_pass_and_quest_then_retry_commits()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        await using var f = await Fixture();
        f.Sql("INSERT INTO character_stats(character_id,carried_gold) VALUES(71,0)");
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            await using var tx = f.Scope.Begin();
            await f.Services.GetRequiredService<IItemDeliveryDomainService>().DeliverAsync(new(1), new(71),
                [new(10094602, 10)], "dragon-crystal-rollback-test", default, ItemDeliveryMode.InventoryOnly);
            await tx.CommitAsync(default);
        });
        var commands = new QuestTestCommands(f.Services);
        Assert.True((await commands.SendAsync(new QuestAcceptTestInput(new(1), new(71), 8557))).Accepted);
        var clock = f.Services.GetRequiredService<TimeProvider>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => TestGameplay.RunAsync(f.Services, async () =>
        {
            await using var tx = f.Scope.Begin();
            var result = await TestEffectFootprints.Run([f.Session], cap =>
                f.Services.GetRequiredService<IQuestCompletionSettlementDomainService>().FinishAsync(new(1), new(71),
                    8557, ushort.MaxValue, 1, clock.GetUtcNow(), cap, default, f.Session));
            Assert.True(result.Finished);
            throw new InvalidOperationException("Abort after quota, materials and ticket have all changed");
        }));
        Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM character_daily_counters WHERE character_id=71 AND counter_key='giant-dragon.quest.8557'"));
        Assert.Equal(1, f.Scalar("SELECT COUNT(*) FROM character_active_quests WHERE character_id=71 AND quest_id=8557"));
        Assert.True(await f.Services.GetRequiredService<IGiantDragonCrystalExchangeDomainService>().CanAcceptAsync(new(71), default));
        var holdings = await f.Services.GetRequiredService<IQuestFactsDomainService>().HoldingsAsync(new(71), default);
        Assert.Equal(10, holdings.Quantities.GetValueOrDefault(10094602));
        Assert.Equal(0, holdings.Quantities.GetValueOrDefault(490000338));
        var finish = new QuestFinishTestInput(new(1), new(71), 8557, ushort.MaxValue, 1,
            checked((uint)clock.GetUtcNow().ToUnixTimeSeconds()), f.Session);
        Assert.True((await commands.SendAsync(finish)).Finished);
        Assert.False((await commands.SendAsync(finish)).Finished);
        Assert.False(await f.Services.GetRequiredService<IGiantDragonCrystalExchangeDomainService>().CanAcceptAsync(new(71), default));
        holdings = await f.Services.GetRequiredService<IQuestFactsDomainService>().HoldingsAsync(new(71), default);
        Assert.Equal(0, holdings.Quantities.GetValueOrDefault(10094602));
        Assert.Equal(1, holdings.Quantities.GetValueOrDefault(490000338));
    }

    [SkippableTheory]
    [InlineData(9, false)]
    [InlineData(10, true)]
    [InlineData(12, true)]
    [InlineData(20, true)]
    public async Task Native_crystal_exchange_consumes_ten_and_grants_one_pass_once_per_day(int held, bool enough)
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        var clock = new DragonRewardClock();
        await using var f = await Fixture(configure: services => services.AddSingleton<TimeProvider>(clock));
        f.Sql("INSERT INTO character_stats(character_id,carried_gold) VALUES(71,0)");
        var definition = await f.Services.GetRequiredService<IQuestContentRuleService>().FindAsync(8557, default);
        Assert.NotNull(definition);
        Assert.Equal(QuestGrade.Daily, definition.Grade);
        Assert.Equal(333, definition.NpcIndex);
        Assert.Equal(333, definition.CompleteNpcIndex);
        Assert.Equal(new SeekingItemRequirement(10094602, 10), Assert.Single(definition.SeekingItems!));
        Assert.Contains(definition.ItemRewards.Fixed, x => x.ItemId == 490000338 && x.Count == 1);

        await TestGameplay.RunAsync(f.Services, async () =>
        {
            await using var tx = f.Scope.Begin();
            await f.Services.GetRequiredService<IItemDeliveryDomainService>().DeliverAsync(new(1), new(71),
                [new(10094602, held)], "dragon-crystal-exchange-test", default, ItemDeliveryMode.InventoryOnly);
            await tx.CommitAsync(default);
        });
        var commands = new QuestTestCommands(f.Services);
        Assert.True((await commands.SendAsync(new QuestAcceptTestInput(new(1), new(71), 8557))).Accepted);
        var at = checked((uint)f.Services.GetRequiredService<TimeProvider>().GetUtcNow().ToUnixTimeSeconds());
        var finish = new QuestFinishTestInput(new(1), new(71), 8557, ushort.MaxValue, 1, at, f.Session);
        Assert.False((await commands.SendAsync(finish with { CompletionCount = 2 })).Finished);
        Assert.Equal(held, Count(10094602));
        var result = await commands.SendAsync(finish);
        Assert.Equal(enough, result.Finished);
        Assert.Equal(enough ? held - 10 : held, Count(10094602));
        Assert.Equal(enough ? 1 : 0, Count(490000338));
        Assert.False((await commands.SendAsync(finish)).Finished);
        Assert.Equal(enough ? 1 : 0, Count(490000338));
        if (enough)
        {
            Assert.False((await commands.SendAsync(new QuestAcceptTestInput(new(1), new(71), 8557))).Accepted);
            Assert.False((await f.Services.GetRequiredService<IQuestQueryDomainService>().QueryAsync(new(1), new(71), 8557, default)).CanAccept);
            clock.Now = clock.Now.AddDays(1);
            Assert.True((await f.Services.GetRequiredService<IQuestQueryDomainService>().QueryAsync(new(1), new(71), 8557, default)).CanAccept);
            Assert.True((await commands.SendAsync(new QuestAcceptTestInput(new(1), new(71), 8557))).Accepted);
            Assert.Equal(1, Count(490000338));
        }

        int Count(int item) => f.Scalar("SELECT COALESCE(SUM(s.count),0) FROM item_slots s JOIN items i ON s.core_uid=i.item_uid "
            + "WHERE s.owner_scope='character' AND s.owner_id=71 AND substr(hex(i.item_core),3,8)='"
            + Convert.ToHexString(BitConverter.GetBytes(item)) + "'");
    }
}
