using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Tests.Inventory;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.CardReward;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.World;

public sealed class FreeGoldCardTests
{
    private sealed class GoldLimit : IGoldCarryLimitSource
    {
        public int BaseLimitFor(int characterLevel) => int.MaxValue;
    }

    private sealed class Rules : IPremiumServiceDefinitionSource
    {
        public IReadOnlyList<int> ReadQuestHelperMinimumDropRates() => new[] { 100, 70, 30 };
        public DoublePotRules ReadDoublePotRules() => new(8, new HashSet<int>());
        public double ReadGrowthExperienceBonusRate() => 0.2;
        public PremiumContractLevels ReadContractLevels() => new(5, 10);
        public int ReadFreeWeaknessDailyLimit() => 10;
        public int ReadDungeonBuffDailyLimit() => 10;
        public int ReadAutoRepairDailyLimit() => 6;
        public int ReadFreeGoldCardDailyLimit() => 10;
    }
    private static ShopFixture Fixture(bool subscribed = true)
    {
        var shop = new ShopFixture(s =>
        {
            s.AddSingleton<IPremiumServiceDefinitionSource>(new Rules());
            s.AddSingleton<IGoldCarryLimitSource>(new GoldLimit());
        });
        if (subscribed) shop.Sql("INSERT INTO account_premiums(account_id,premium_type,end_time) VALUES(1,580,4000000000)");
        return shop;
    }
    private static void Open(ShopFixture shop, int? price = 1300)
    {
        var runs = shop.Services.GetRequiredService<IDungeonRunDomainService>();
        var id = shop.Session.CharacterId;
        if (runs.Find(id) is { } old) runs.End(id, old.RunId);
        var cell = new MazeCell(0, 0);
        var layout = new DungeonLayout(144, 0, 1, 1, cell, cell, 55611, 0,
            new Dictionary<MazeCell, int> { [cell] = 55611 });
        var run = runs.Begin(id, layout, difficulty: 0);
        runs.FixClearRewardCards(id, run.RunId,
            new(ClearRewardCardKind.Free, [new(0, null)], Gold: 0),
            new(ClearRewardCardKind.Paid, [new(0, null)], Gold: 95), price);
        Assert.NotNull(runs.TryOpenClear(id, run.RunId, run.RoomVersion));
        var settlement = Assert.IsType<DungeonSettlementClaim>(runs.TryClaimSettlement(id, run.RunId));
        Assert.NotNull(runs.CommitSettlement(settlement));
        Assert.NotNull(runs.TryOpenCardLayout(id, run.RunId));
    }
    private static ValueTask<SelectCardResult> Flip(ShopFixture shop, ClearRewardCardKind kind = ClearRewardCardKind.Paid) =>
        shop.Services.GetRequiredService<ICommandBus>().SendAsync(new SelectCardCommand(
            shop.Session.AccountId, shop.Session.CharacterId, kind, 0));
    private static int Used(SelectCardResult result) => result.Usage.Single(x => x.SlotIndex == 0).UsedCount;

    [Fact]
    public async Task Ten_free_flips_then_normal_price_and_reset_at_six()
    {
        using var shop = Fixture();
        for (var i = 1; i <= 11; i++)
        {
            Open(shop);
            var result = await Flip(shop);
            Assert.Equal(SelectCardStatus.Flipped, result.Status);
            Assert.Equal(i <= 10 ? 0 : 1300, result.GoldCharged);
            Assert.Equal(Math.Min(i, 10), Used(result));
        }
        Assert.Equal(100000 + 11 * 95 - 1300, shop.Gold);
        shop.Clock.Advance(TimeSpan.FromHours(9) + TimeSpan.FromMinutes(59));
        Open(shop);
        Assert.Equal(1300, (await Flip(shop)).GoldCharged);
        shop.Clock.Advance(TimeSpan.FromMinutes(1));
        Open(shop);
        var reset = await Flip(shop);
        Assert.Equal(0, reset.GoldCharged);
        Assert.Equal(1, Used(reset));
        Assert.Equal(1, shop.Scalar("SELECT COUNT(*) FROM character_daily_counters WHERE counter_key='premium_free_gold_card_used'"));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Missing_or_expired_subscription_uses_normal_price(bool expired)
    {
        using var shop = Fixture(expired);
        if (expired) shop.Sql("UPDATE account_premiums SET end_time=1");
        Open(shop);
        var result = await Flip(shop);
        Assert.Equal(1300, result.GoldCharged);
        Assert.Equal(0, Used(result));
        Assert.Equal(0, shop.Scalar("SELECT COUNT(*) FROM character_daily_counters"));
    }
    [Fact]
    public async Task Ordinary_free_card_and_duplicate_do_not_consume_quota()
    {
        using var shop = Fixture();
        Open(shop);
        Assert.Equal(0, Used(await Flip(shop, ClearRewardCardKind.Free)));
        var first = await Flip(shop);
        Assert.Equal(1, Used(first));
        Assert.Equal(SelectCardStatus.AlreadyFlipped, (await Flip(shop)).Status);
        Assert.Equal(1, shop.Scalar("SELECT value FROM character_daily_counters WHERE counter_key='premium_free_gold_card_used'"));
        Assert.Equal(100095, shop.Gold);
    }
    [Fact]
    public async Task Award_failure_rolls_back_quota_and_releases_card_for_retry()
    {
        using var shop = Fixture();
        Open(shop);
        shop.Sql("CREATE TRIGGER reject_award BEFORE UPDATE OF carried_gold ON character_stats WHEN NEW.carried_gold > OLD.carried_gold BEGIN SELECT RAISE(ABORT,'reject award'); END;");
        await Assert.ThrowsAsync<SqliteException>(() => Flip(shop).AsTask());
        Assert.Equal(100000, shop.Gold);
        Assert.Equal(0, shop.Scalar("SELECT COUNT(*) FROM character_daily_counters"));
        shop.Sql("DROP TRIGGER reject_award");
        Assert.Equal(1, Used(await Flip(shop)));
        Assert.Equal(100095, shop.Gold);
    }
    [Fact]
    public async Task No_gold_can_use_contract_but_cannot_buy_a_paid_flip_without_it()
    {
        using var shop = Fixture(false);
        shop.Sql("UPDATE character_stats SET carried_gold=0 WHERE character_id=1");
        Open(shop);
        Assert.Equal(SelectCardStatus.NotEnoughGold, (await Flip(shop)).Status);
        Assert.Equal(0, shop.Scalar("SELECT COUNT(*) FROM character_daily_counters"));
        shop.Sql("INSERT INTO account_premiums(account_id,premium_type,end_time) VALUES(1,580,4000000000)");
        var result = await Flip(shop);
        Assert.Equal(SelectCardStatus.Flipped, result.Status);
        Assert.Equal(0, result.GoldCharged);
        Assert.Equal(1, Used(result));
        Assert.Equal(95, shop.Gold);
    }
    [Fact]
    public async Task Disabled_gold_card_is_not_enabled_by_subscription()
    {
        using var shop = Fixture();
        Open(shop, null);
        Assert.Equal(SelectCardStatus.NotPriced, (await Flip(shop)).Status);
        Assert.Equal(0, shop.Scalar("SELECT COUNT(*) FROM character_daily_counters"));
    }
    [Fact]
    public async Task Quota_belongs_to_character_and_concurrent_duplicate_consumes_once()
    {
        using var shop = Fixture();
        Open(shop);
        var results = await Task.WhenAll(Flip(shop).AsTask(), Flip(shop).AsTask());
        Assert.Single(results, x => x.Status == SelectCardStatus.Flipped);
        Assert.Equal(1, shop.Scalar("SELECT value FROM character_daily_counters WHERE character_id=1 AND counter_key='premium_free_gold_card_used'"));
        shop.Select(2);
        Open(shop);
        Assert.Equal(1, Used(await Flip(shop)));
    }
}
