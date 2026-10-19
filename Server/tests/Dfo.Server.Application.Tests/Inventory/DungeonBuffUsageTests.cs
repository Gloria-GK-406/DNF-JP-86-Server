using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Networking;
using System.Buffers.Binary;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class DungeonBuffUsageTests
{
    private sealed class Rules : IPremiumServiceDefinitionSource
    {
        public IReadOnlyList<int> ReadQuestHelperMinimumDropRates() => new[] { 100, 70, 30 };
        public DoublePotRules ReadDoublePotRules() => new(8, new HashSet<int>());
        public IReadOnlyList<int> ReadGrowthBossEquipmentBonusPercents() => new[] {20, 23, 27, 30};
        public double ReadBlackDiamondExperienceBonusRate() => .05;
        public double ReadGrowthExperienceBonusRate() => 0.2;
        public PremiumContractLevels ReadContractLevels() => new(5, 10);
        public int ReadDungeonBuffDailyLimit() => 10;
        public int ReadFreeWeaknessDailyLimit() => 10;
        public int ReadAutoRepairDailyLimit() => 6;
        public int ReadFreeGoldCardDailyLimit() => 10;
    }
    private static ShopFixture Fixture(bool subscribed = true)
    {
        var shop = new ShopFixture(s => s.AddSingleton<IPremiumServiceDefinitionSource>(new Rules()));
        if (subscribed) shop.Sql("INSERT INTO account_premiums(account_id,premium_type,end_time) VALUES(1,583,4000000000)");
        return shop;
    }
    private static DungeonRunId Enter(ShopFixture shop)
    {
        var runs = shop.Services.GetRequiredService<IDungeonRunDomainService>();
        var id = shop.Session.CharacterId;
        if (runs.Find(id) is { } old) runs.End(id, old.RunId);
        var cell = new MazeCell(0, 0);
        return shop.InTransaction(() => runs.Begin(id, new DungeonLayout(144, 0, 1, 1, cell, cell, 55611, 0,
            new Dictionary<MazeCell, DungeonCellDefinition> { [cell] = DungeonCellDefinition.Fixed(55611)}), difficulty: 0)).RunId;
    }
    private static async Task<int> Report(ShopFixture shop)
    {
        await shop.Services.GetRequiredService<PremiumServiceUsageReportHandler>().HandleAsync(
            new(shop.Connection), new PremiumServiceUsageReport(GameMessageMetadata.Empty, 3), default);
        var snapshot = await shop.Services.GetRequiredService<ICommandBus>().SendAsync(new ReadPremiumSnapshotCommand(shop.Session));
        return snapshot.Usage.Single(x => x.SlotIndex == 3).UsedCount;
    }

    [Fact]
    public async Task Ten_runs_consume_ten_uses_repeated_reports_are_free_and_next_day_resets()
    {
        using var shop = Fixture();
        Assert.Equal(0, await Report(shop)); // Town does not consume.
        for (var i = 1; i <= 11; i++)
        {
            Enter(shop);
            var results = await Task.WhenAll(Report(shop), Report(shop));
            Assert.All(results, used => Assert.Equal(Math.Min(i, 10), used));
        }
        Assert.Equal(100000, shop.Gold);
        shop.Clock.Advance(TimeSpan.FromDays(1));
        Enter(shop);
        Assert.Equal(1, await Report(shop));
        // Midnight does not make the same run chargeable again.
        shop.Clock.Advance(TimeSpan.FromDays(1));
        Assert.Equal(0, await Report(shop));
    }

    [Fact]
    public async Task No_subscription_expired_subscription_and_stale_run_do_not_consume()
    {
        using var shop = Fixture(false);
        var old = Enter(shop);
        Assert.Equal(0, await Report(shop));
        shop.Sql("INSERT INTO account_premiums(account_id,premium_type,end_time) VALUES(1,583,1)");
        Assert.Equal(0, await Report(shop));
        shop.Sql("UPDATE account_premiums SET end_time=4000000000");
        Enter(shop);
        var result = await shop.Services.GetRequiredService<ICommandBus>().SendAsync(new ConsumeDungeonBuffCommand(shop.Session, old));
        Assert.Equal(0, result.Usage.Single(x => x.SlotIndex == 3).UsedCount);
        Assert.Equal(1, await Report(shop));
        shop.Select(2);
        Enter(shop);
        Assert.Equal(1, await Report(shop)); // Character-scoped quota.
    }

    [Fact]
    public async Task Failure_after_counter_write_rolls_back_counter_and_run_marker()
    {
        using var shop = Fixture();
        var run = Enter(shop);
        shop.Sql($"INSERT INTO character_daily_counters(character_id,counter_key,period,value,last_consumed_at) VALUES(1,'lottery_double_reward_used',NULL,-1,{shop.Clock.GetUtcNow().ToUnixTimeMilliseconds()})");
        await Assert.ThrowsAsync<InvalidDataException>(() => shop.Services.GetRequiredService<ICommandBus>()
            .SendAsync(new ConsumeDungeonBuffCommand(shop.Session, run)).AsTask());
        Assert.Equal(0, shop.Scalar("SELECT COUNT(*) FROM character_daily_counters WHERE counter_key='premium_dungeon_buff_used'"));
        Assert.False(shop.Services.GetRequiredService<IDungeonRunDomainService>().HasUsedDungeonBuff(shop.Session.CharacterId, run));
        shop.Sql("DELETE FROM character_daily_counters WHERE counter_key='lottery_double_reward_used'");
        Assert.Equal(1, await Report(shop));
    }

    [Fact]
    public async Task Same_day_expiry_and_renewal_refresh_without_sending_every_tick()
    {
        using var shop = Fixture(false);
        var end = shop.Clock.GetUtcNow().AddMinutes(1).ToUnixTimeSeconds();
        shop.Sql($"INSERT INTO account_premiums(account_id,premium_type,end_time) VALUES(1,583,{end})");
        var refresh = ActivatorUtilities.CreateInstance<PremiumDailyRefreshService>(shop.Services);
        await refresh.RefreshAsync(default);
        await refresh.RefreshAsync(default);
        Assert.Single(shop.Sender.Messages);
        shop.Clock.Advance(TimeSpan.FromMinutes(1));
        await refresh.RefreshAsync(default);
        Assert.Equal(2, shop.Sender.Messages.Count);
        await refresh.RefreshAsync(default);
        Assert.Equal(2, shop.Sender.Messages.Count);
        shop.Sql("UPDATE account_premiums SET end_time=4000000000");
        await refresh.RefreshAsync(default);
        Assert.Equal(3, shop.Sender.Messages.Count);
    }

    [Fact]
    public async Task Rolling_default_black_diamond_does_not_refresh_devil_snapshot_every_minute()
    {
        using var shop = Fixture(false);
        var premium = shop.Services.GetRequiredService<IPremiumServiceDomainService>().For(shop.Session.AccountId);
        Assert.True(await premium.IsActiveAsync(PremiumServiceType.BlackDiamond, default));
        var refresh = ActivatorUtilities.CreateInstance<PremiumDailyRefreshService>(shop.Services);
        await refresh.RefreshAsync(default);
        Assert.Single(shop.Sender.Messages);
        shop.Clock.Advance(TimeSpan.FromMinutes(1));
        await refresh.RefreshAsync(default);
        Assert.Single(shop.Sender.Messages);
    }

    [Dfo.Server.Testing.PvfFact]
    public void A21_archive_declares_ten_buff_uses()
    {
        using var shop = new ShopFixture();
        Assert.Equal(10, shop.Services.GetRequiredService<IPremiumServiceRuleService>().DungeonBuffDailyLimit);
    }

    [Theory]
    [InlineData(false, 97)]
    [InlineData(false, 84)]
    [InlineData(true, 97)]
    [InlineData(true, 84)]
    public async Task Contract_expiry_sends_one_zero_notice_and_rejects_qualification(bool expiredBeforeFirstTick, int type)
    {
        using var shop = Fixture(false);
        var now = shop.Clock.GetUtcNow();
        shop.Sql($"INSERT INTO account_premiums(account_id,premium_type,end_time) VALUES(1,{type},{now.AddMinutes(expiredBeforeFirstTick ? -1 : 1).ToUnixTimeSeconds()})");
        var refresh = ActivatorUtilities.CreateInstance<PremiumDailyRefreshService>(shop.Services);
        await refresh.RefreshAsync(default);
        shop.Clock.Advance(TimeSpan.FromMinutes(1));
        await refresh.RefreshAsync(default);
        await refresh.RefreshAsync(default);
        var notice = Assert.Single(shop.Sender.Messages, x => x.GetType().Name == "PremiumGrantedMessage");
        var registration = shop.Services.GetServices<ServerMessageEncoderRegistration>().Single(x => x.MessageType == notice.GetType());
        var frame = Assert.Single(((IServerMessageEncoder)shop.Services.GetRequiredService(registration.EncoderType)).Encode(notice));
        Assert.Equal(0x0042, BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1, 2)));
        Assert.Equal(type, frame[17]);
        Assert.Equal(0, BinaryPrimitives.ReadInt64LittleEndian(frame.AsSpan(18, 8)));
        Assert.False(await shop.Services.GetRequiredService<IPremiumServiceDomainService>().For(shop.Session.AccountId)
            .IsActiveAsync((PremiumServiceType)type, default));
        // A renewal can expire again; the old expiry notice must not suppress the new transition.
        shop.Sql($"UPDATE account_premiums SET end_time={shop.Clock.GetUtcNow().AddMinutes(1).ToUnixTimeSeconds()}");
        await refresh.RefreshAsync(default);
        shop.Clock.Advance(TimeSpan.FromMinutes(1));
        await refresh.RefreshAsync(default);
        Assert.Equal(2, shop.Sender.Messages.Count(x => x.GetType().Name == "PremiumGrantedMessage"));
    }
}
