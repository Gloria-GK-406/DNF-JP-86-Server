using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Inventory;
using System.Buffers.Binary;
using Dfo.Server.Protocol.Game.LotteryOpening;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class LotteryOpeningTests
{
    private sealed class Fixed : ILotteryResultSelector
    {
        public int Calls;
        public Action? BeforeRoll;
        public OpeningReward Roll(int id)
        { Calls++; BeforeRoll?.Invoke(); return new(id switch { 7110 => 20002, 7120 => 10402, 7150 => 31353, _ => throw new InvalidOperationException() }, 1); }
    }
    private static CeraShopFixture Fixture(Fixed? selector = null)
    {
        var shop = new CeraShopFixture(useArchive: true,
            configure: s => s.AddSingleton<ILotteryResultSelector>(selector ?? new Fixed()));
        shop.Host.Services.GetRequiredService<ILotteryDefinitionCache>().InitializeAsync().GetAwaiter().GetResult();
        return shop;
    }
    private static Task<LotteryOpeningResult> Command(CeraShopFixture shop, ushort mode = 0, int slot = 65) =>
        shop.Host.Services.GetRequiredService<ICommandBus>().SendAsync(new LotteryOpeningCommand(shop.Host.OnlineUser, shop.Host.Session, slot, mode)).AsTask();

    private static ValueTask Network(CeraShopFixture shop, ushort sequence, ushort mode = 0) =>
        EffectRequestTransport.Send(shop.Host, new LotteryOpeningRequest(new(1, 0x001B, 0, sequence), mode, 65));

    [Dfo.Server.Testing.PvfTheory]
    [InlineData(7110, 20002)]
    [InlineData(7120, 10402)]
    [InlineData(7150, 31353)]
    public async Task Ordinary_request_commits_one_reward_and_fresh_request_opens_next_pot(int pot, int reward)
    {
        var selector = new Fixed(); using var shop = Fixture(selector); await shop.Host.Seed(65, pot, 3);
        await Network(shop, 10);
        var result = Assert.Single(shop.Host.Sender.Messages.OfType<LotteryOpeningResponse>()).Result!;
        Assert.Equal(reward, Assert.Single(result.Receipt.Rewards).ItemId);
        Assert.Equal(2, shop.Host.Count(pot)); Assert.Equal(1, shop.Host.Count(reward));
        await Network(shop, 10); Assert.Equal(1, selector.Calls);
        Assert.Single(shop.Host.Sender.Batches, EffectRequestTransport.IsStateReplay);
        Assert.Equal(3, Assert.Single(shop.Host.Sender.Batches, batch => batch.Messages.Any(message => message is LotteryOpeningResponse)).Messages.Length);
        await Network(shop, 11); Assert.Equal(2, selector.Calls);
        Assert.Equal(1, shop.Host.Count(pot)); Assert.Equal(2, shop.Host.Count(reward));
        Assert.Equal(100000, shop.Host.Gold);
    }

    [Dfo.Server.Testing.PvfFact]
    public async Task Unsupported_mode_or_missing_source_does_not_consume()
    {
        using var shop = Fixture(); await shop.Host.Seed(65, 7110, 3);
        await Assert.ThrowsAsync<OpeningRejectedException>(() => Command(shop, mode: 1));
        await Assert.ThrowsAsync<OpeningRejectedException>(() => Command(shop, slot: 66));
        Assert.Equal(3, shop.Host.Count(7110));
    }

    [Dfo.Server.Testing.PvfFact]
    public async Task Failure_rolls_back_and_same_sequence_can_retry()
    {
        using var shop = Fixture(); await shop.Host.Seed(65, 7110, 3);
        shop.Host.Sql("CREATE TRIGGER reject_reward BEFORE INSERT ON items BEGIN SELECT RAISE(ABORT,'reward blocked'); END");
        await Assert.ThrowsAnyAsync<Exception>(() => Command(shop));
        Assert.Equal(3, shop.Host.Count(7110)); Assert.Equal(0, shop.Host.Count(20002));
        shop.Host.Sql("DROP TRIGGER reject_reward"); await Command(shop);
        Assert.Equal(2, shop.Host.Count(7110));
    }

    [Dfo.Server.Testing.PvfFact]
    public async Task Full_equipment_band_refuses_without_consuming_pot()
    {
        using var shop = Fixture(); await shop.Host.Seed(65, 7110, 3);
        for (var slot = 9; slot <= 64; slot++) await shop.Host.Seed(slot, 20002, 1, 1);
        await Assert.ThrowsAsync<OpeningRejectedException>(() => Command(shop));
        Assert.Equal(3, shop.Host.Count(7110));
        Assert.Equal(0, shop.Host.Scalar("SELECT COUNT(*) FROM mailbox_messages"));
    }

    [Dfo.Server.Testing.PvfFact]
    public async Task Handler_sends_full_native_result_before_absolute_inventory()
    {
        using var shop = Fixture(); await shop.Host.Seed(65, 7110, 1);
        await Network(shop, 10);
        var result = Assert.IsType<LotteryOpeningResponse>(shop.Host.Sender.Messages[0]);
        Assert.NotNull(result.Result); Assert.Equal(0, shop.Host.Count(7110));
        var frame = shop.Encode(result);
        Assert.Equal(77, frame.Length); // 15-byte frame + 62-byte A21 success body.
        Assert.Equal(4, BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(15 + 37)));
        Assert.Equal(8, BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(15 + 45)));
        Assert.All(frame.Skip(15 + 57), value => Assert.Equal((byte)0, value));
        await Network(shop, 10);
        Assert.Single(shop.Host.Sender.Messages.OfType<LotteryOpeningResponse>());
        Assert.Single(shop.Host.Sender.Batches, EffectRequestTransport.IsStateReplay);
        Assert.Equal(4, Assert.Single(shop.Host.Sender.Batches, batch => batch.Messages.Any(message => message is LotteryOpeningResponse)).Messages.Length);
        Assert.Equal(1, shop.Host.Count(20002));
    }

    [Dfo.Server.Testing.PvfFact]
    public async Task Concurrent_same_sequence_grants_only_once()
    {
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var selector = new Fixed { BeforeRoll = () => { entered.Set(); if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException(); } };
        using var shop = Fixture(selector); await shop.Host.Seed(65, 7110, 3);
        var first = Task.Run(async () => await Network(shop, 10)); Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        var duplicate = Network(shop, 10).AsTask();
        try { Assert.False(duplicate.IsCompleted); Assert.Equal(1, selector.Calls); Assert.Equal(3, shop.Host.Count(7110)); }
        finally { release.Set(); }
        await Task.WhenAll(first, duplicate);
        Assert.Equal(2, shop.Host.Count(7110)); Assert.Equal(1, shop.Host.Count(20002));
        Assert.Equal(1, selector.Calls); Assert.Single(shop.Host.Sender.Batches, EffectRequestTransport.IsStateReplay);
        Assert.Equal(3, Assert.Single(shop.Host.Sender.Batches, batch => batch.Messages.Any(message => message is LotteryOpeningResponse)).Messages.Length);
    }

    [Dfo.Server.Testing.PvfFact]
    public async Task Gold_and_pot_roll_back_together_and_insufficient_gold_is_rejected()
    {
        using var shop = new CeraShopFixture(useArchive: true, configure: services =>
        {
            services.AddSingleton<ILotteryResultSelector>(new Fixed());
            var source = services.Last(x => x.ServiceType == typeof(IPackageOpeningDefinitionSource));
            services.AddSingleton<IPackageOpeningDefinitionSource>(provider => new CostSource(
                (IPackageOpeningDefinitionSource)(source.ImplementationInstance
                    ?? source.ImplementationFactory?.Invoke(provider)
                    ?? ActivatorUtilities.CreateInstance(provider, source.ImplementationType!))));
        });
        await shop.Host.Services.GetRequiredService<ILotteryDefinitionCache>().InitializeAsync();
        await shop.Host.Seed(65, 7110, 3);
        shop.Host.Sql("CREATE TRIGGER reject_reward BEFORE INSERT ON items BEGIN SELECT RAISE(ABORT,'reward blocked'); END");
        await Assert.ThrowsAnyAsync<Exception>(() => Command(shop));
        Assert.Equal(100000, shop.Host.Gold); Assert.Equal(3, shop.Host.Count(7110));
        shop.Host.Sql("DROP TRIGGER reject_reward"); await Command(shop);
        Assert.Equal(40000, shop.Host.Gold); Assert.Equal(2, shop.Host.Count(7110));
        await Assert.ThrowsAsync<OpeningRejectedException>(() => Command(shop));
        Assert.Equal(40000, shop.Host.Gold);
    }

    [Dfo.Server.Testing.PvfFact]
    public async Task Source_id_outside_pot_cache_throws_without_consumption()
    {
        using var shop = Fixture();
        await shop.Host.Seed(65, 201, 3);
        var error = await Assert.ThrowsAsync<OpeningRejectedException>(() => Command(shop));
        Assert.Contains("201", error.Message);
        Assert.Contains("cache", error.Message);
        Assert.Equal(3, shop.Host.Count(201));
        Assert.Equal(100000, shop.Host.Gold);
    }

    [Dfo.Server.Testing.PvfFact]
    public async Task Production_selector_opens_without_fixed_configuration()
    {
        using var shop = new CeraShopFixture(useArchive: true); await shop.Host.Seed(65, 7110, 1);
        await shop.Host.Services.GetRequiredService<ILotteryDefinitionCache>().InitializeAsync();
        var result = await Command(shop);
        var reward = Assert.Single(result.Receipt.Rewards);
        Assert.Contains(shop.Host.Services.GetRequiredService<ILotteryDefinitionCache>().GetRequired(7110).Pools[0].Choices,
            row => row.Weight > 0 && row.Reward.ItemId == reward.ItemId);
        Assert.Equal(0, shop.Host.Count(7110));
    }
    private static void Subscribe(CeraShopFixture shop) =>
        shop.Host.Sql("INSERT INTO account_premiums(account_id,premium_type,end_time) VALUES(1,581,4000000000)");
    private static int Used(CeraShopFixture shop) => shop.Host.Scalar(
        "SELECT COALESCE(MAX(value),0) FROM character_daily_counters WHERE counter_key='lottery_double_reward_used'");

    [Dfo.Server.Testing.PvfFact]
    public async Task Double_draws_once_grants_two_distinct_instances_and_commits_before_native_reply_and_snapshot()
    {
        var selector = new Fixed(); using var shop = Fixture(selector); Subscribe(shop);
        await shop.Host.Seed(65, 7110, 3);
        shop.Host.Sender.BeforeSend = () => { Assert.Equal(1, Used(shop)); Assert.Equal(2, shop.Host.Count(20002)); };
        await Network(shop, 0, 1);
        var response = Assert.IsType<LotteryOpeningResponse>(shop.Host.Sender.Messages[0]);
        var receipt = response.Result!.Receipt;
        Assert.Equal(2, receipt.Rewards.Length);
        Assert.Equal(2, receipt.Rewards.Select(x => x.Slot).Distinct().Count());
        var cores = receipt.Updates.Where(x => x.Core.ItemId == 20002).ToArray();
        Assert.Equal(2, cores.Select(x => x.Core.ToCore().Value).Distinct().Count());
        Assert.Equal(1, selector.Calls); Assert.Equal(2, shop.Host.Count(7110));
        var native = shop.Encode(response);
        Assert.Equal(77, native.Length);
        Assert.Equal(2, BinaryPrimitives.ReadInt32LittleEndian(native.AsSpan(24)));
        var snapshot = shop.Encode(shop.Host.Sender.Messages.Last());
        Assert.Equal(92, snapshot.Length);
        Assert.Equal(1, snapshot[15 + 3 + 10 + 9]);
        await Network(shop, 0, 1);
        Assert.Single(shop.Host.Sender.Messages.OfType<LotteryOpeningResponse>());
        Assert.Single(shop.Host.Sender.Batches, EffectRequestTransport.IsStateReplay);
        Assert.Equal(4, Assert.Single(shop.Host.Sender.Batches, batch => batch.Messages.Any(message => message is LotteryOpeningResponse)).Messages.Length);
        Assert.Equal(1, Used(shop));
    }

    [Dfo.Server.Testing.PvfFact]
    public async Task Eighth_double_succeeds_ninth_stops_with_73_then_next_business_day_resets()
    {
        using var shop = Fixture(); Subscribe(shop); await shop.Host.Seed(65, 7110, 10);
        for (ushort seq = 1; seq <= 8; seq++) await Command(shop, mode: 1);
        await Network(shop, 0, 1);
        var response = Assert.IsType<LotteryOpeningResponse>(shop.Host.Sender.Messages.Last());
        Assert.Equal(new byte[] { 0, 0x73 }, shop.Encode(response).Skip(15));
        Assert.Equal(8, Used(shop)); Assert.Equal(2, shop.Host.Count(7110)); Assert.Equal(16, shop.Host.Count(20002));
        shop.Host.Clock.Advance(TimeSpan.FromDays(1));
        await Command(shop, mode: 1); Assert.Equal(1, Used(shop));
    }

    [Dfo.Server.Testing.PvfFact]
    public async Task One_free_slot_rejects_double_and_rolls_back_pot_gold_and_quota()
    {
        using var shop = Fixture(); Subscribe(shop); await shop.Host.Seed(65, 7110, 2);
        for (var slot = 9; slot < 40; slot++) await shop.Host.Seed(slot, 20002, 1, 1);
        var error = await Assert.ThrowsAsync<OpeningRejectedException>(() => Command(shop, mode: 1));
        Assert.Equal(OpeningRejection.NoRoom, error.Reason);
        Assert.Equal(0, Used(shop)); Assert.Equal(2, shop.Host.Count(7110)); Assert.Equal(100000, shop.Host.Gold);
        await Command(shop, mode: 0); Assert.Equal(1, shop.Host.Count(7110));
        Assert.Equal(0, shop.Host.Scalar("SELECT COUNT(*) FROM mailbox_messages"));
    }

    [Dfo.Server.Testing.PvfFact]
    public async Task Full_double_reward_rejects_without_consuming_source_or_quota()
    {
        var selector = new Fixed(); using var shop = Fixture(selector); Subscribe(shop);
        await shop.Host.Seed(65, 7110, 3);
        for (var slot = 9; slot <= 64; slot++) await shop.Host.Seed(slot, 20002, 1, 1);
        var error = await Assert.ThrowsAsync<OpeningRejectedException>(() => Command(shop, mode: 1));
        Assert.Equal(OpeningRejection.NoRoom, error.Reason);
        Assert.Equal(0, Used(shop)); Assert.Equal(3, shop.Host.Count(7110));
        Assert.Equal(100000, shop.Host.Gold);
        Assert.Equal(0, shop.Host.Scalar("SELECT COUNT(*) FROM mailbox_messages"));
    }

    [Dfo.Server.Testing.PvfFact]
    public async Task Storage_failure_rolls_back_double_quota_and_retry_does_not_duplicate_rewards()
    {
        using var shop = Fixture(); Subscribe(shop); await shop.Host.Seed(65, 7110, 3);
        shop.Host.Sql("CREATE TRIGGER reject_reward BEFORE INSERT ON items BEGIN SELECT RAISE(ABORT,'reward blocked'); END");
        await Assert.ThrowsAnyAsync<Exception>(() => Command(shop, mode: 1));
        Assert.Equal(0, Used(shop)); Assert.Equal(3, shop.Host.Count(7110));
        shop.Host.Sql("DROP TRIGGER reject_reward"); await Command(shop, mode: 1);
        Assert.Equal(1, Used(shop)); Assert.Equal(2, shop.Host.Count(20002));
    }

    [Dfo.Server.Testing.PvfFact]
    public async Task Expired_subscription_and_ineligible_pot_do_not_consume()
    {
        using var shop = Fixture(); Subscribe(shop); await shop.Host.Seed(65, 7110, 3);
        shop.Host.Sql("UPDATE account_premiums SET end_time=1");
        var expired = await Assert.ThrowsAsync<OpeningRejectedException>(() => Command(shop, mode: 1));
        Assert.Equal(OpeningRejection.PremiumUnavailable, expired.Reason);
        shop.Host.Sql("UPDATE account_premiums SET end_time=4000000000");
        var rules = shop.Host.Services.GetRequiredService<Dfo.Server.Abstractions.Accounts.IPremiumServiceRuleService>();
        Assert.Equal(8, rules.DoublePotDailyLimit);
        Assert.True(rules.SupportsDoublePot(7110)); Assert.True(rules.SupportsDoublePot(7120)); Assert.True(rules.SupportsDoublePot(7150));
        var other = 10087911;
        Assert.False(rules.SupportsDoublePot(other));
        await shop.Host.Seed(66, other, 1);
        await Assert.ThrowsAsync<OpeningRejectedException>(() => Command(shop, mode: 1, slot: 66));
        Assert.Equal(0, Used(shop)); Assert.Equal(1, shop.Host.Count(other)); Assert.Equal(3, shop.Host.Count(7110));
    }

    private static int TotalUsed(CeraShopFixture shop) => shop.Host.Scalar(
        "SELECT COALESCE(MAX(value),0) FROM character_daily_counters WHERE counter_key='lottery_double_reward_total_used'");

    private static void SeedUsage(CeraShopFixture shop, int total, int round = 8)
    {
        var timestamp = shop.Host.Clock.GetUtcNow().ToUnixTimeMilliseconds();
        shop.Host.Sql($"INSERT INTO character_daily_counters(character_id,counter_key,value,last_consumed_at) VALUES(1,'lottery_double_reward_used',{round},{timestamp}),(1,'lottery_double_reward_total_used',{total},{timestamp})");
    }

    [Dfo.Server.Testing.PvfTheory]
    [InlineData(0, 0)]
    [InlineData(8, 0)]
    [InlineData(22, 0)]
    [InlineData(24, 2)]
    [InlineData(29, 7)]
    [InlineData(30, 8)]
    [InlineData(31, 8)]
    public async Task Purchase_recalculates_round_projection_without_erasing_total(int total, int projected)
    {
        using var shop = Fixture(); Subscribe(shop); SeedUsage(shop, total);
        var result = await shop.Buy(CeraShopTestContent.Entry(100539));
        Assert.NotNull(result.Receipt);
        Assert.Equal(projected, Used(shop)); Assert.Equal(total, TotalUsed(shop));
        Assert.Equal(projected, result.Receipt!.PremiumUsageSlots.Single(x => x.SlotIndex == 1).UsedCount);
        await shop.Buy(CeraShopTestContent.Entry(100539));
        Assert.Equal(projected, Used(shop)); Assert.Equal(total, TotalUsed(shop));
    }

    [Dfo.Server.Testing.PvfFact]
    public async Task Final_round_grants_six_then_renewal_cannot_bypass_thirty_and_next_day_resets_both()
    {
        using var shop = Fixture(); Subscribe(shop); SeedUsage(shop, 24);
        await shop.Host.Seed(65, 7110, 10);
        await shop.Buy(CeraShopTestContent.Entry(100539));
        for (ushort seq = 1; seq <= 6; seq++) await Command(shop, mode: 1);
        Assert.Equal(8, Used(shop)); Assert.Equal(30, TotalUsed(shop));
        await shop.Buy(CeraShopTestContent.Entry(100539));
        await Assert.ThrowsAsync<OpeningRejectedException>(() => Command(shop, mode: 1));
        // Even a stale or corrupted round projection cannot bypass the cumulative guard.
        shop.Host.Sql("UPDATE character_daily_counters SET value=0 WHERE counter_key='lottery_double_reward_used'");
        await Assert.ThrowsAsync<OpeningRejectedException>(() => Command(shop, mode: 1));
        await Command(shop, mode: 0);
        Assert.Equal(30, TotalUsed(shop));
        shop.Host.Clock.Advance(TimeSpan.FromDays(1));
        await Command(shop, mode: 1);
        Assert.Equal(1, Used(shop)); Assert.Equal(1, TotalUsed(shop));
    }

    [Dfo.Server.Testing.PvfFact]
    public async Task First_renewal_preserves_legacy_usage_and_failed_purchase_rolls_everything_back()
    {
        using var shop = Fixture(); Subscribe(shop);
        var timestamp = shop.Host.Clock.GetUtcNow().ToUnixTimeMilliseconds();
        shop.Host.Sql($"INSERT INTO character_daily_counters(character_id,counter_key,value,last_consumed_at) VALUES(1,'lottery_double_reward_used',8,{timestamp})");
        shop.Host.Sql("CREATE TRIGGER reject_round BEFORE UPDATE ON character_daily_counters WHEN NEW.counter_key='lottery_double_reward_used' BEGIN SELECT RAISE(ABORT,'blocked'); END");
        await Assert.ThrowsAnyAsync<Exception>(() => shop.Buy(CeraShopTestContent.Entry(100539)));
        Assert.Equal(100000, shop.Cera); Assert.Equal(8, Used(shop)); Assert.Equal(0, TotalUsed(shop));
        Assert.Equal(0, shop.Host.Scalar("SELECT COUNT(*) FROM account_premiums WHERE end_time<>4000000000"));
        shop.Host.Sql("DROP TRIGGER reject_round");
        await shop.Buy(CeraShopTestContent.Entry(100539));
        Assert.Equal(0, Used(shop)); Assert.Equal(8, TotalUsed(shop));
    }

    [Dfo.Server.Testing.PvfFact]
    public async Task Cumulative_write_failure_rolls_back_round_pot_and_reward()
    {
        using var shop = Fixture(); Subscribe(shop); await shop.Host.Seed(65, 7110, 2);
        shop.Host.Sql("CREATE TRIGGER reject_total BEFORE INSERT ON character_daily_counters WHEN NEW.counter_key='lottery_double_reward_total_used' BEGIN SELECT RAISE(ABORT,'blocked'); END");
        await Assert.ThrowsAnyAsync<Exception>(() => Command(shop, mode: 1));
        Assert.Equal(0, Used(shop)); Assert.Equal(0, TotalUsed(shop));
        Assert.Equal(2, shop.Host.Count(7110)); Assert.Equal(0, shop.Host.Count(20002));
        shop.Host.Sql("DROP TRIGGER reject_total");
        await Command(shop, mode: 1);
        Assert.Equal(1, Used(shop)); Assert.Equal(1, TotalUsed(shop));
    }

    private sealed class CostSource(IPackageOpeningDefinitionSource inner) : IPackageOpeningDefinitionSource
    {
        public async ValueTask<OpeningPackageDefinition?> GetAsync(int id, CancellationToken token)
        {
            var definition = await inner.GetAsync(id, token);
            return definition is null ? null : definition with { GoldCost = 60000 };
        }
        public ValueTask<OpeningRewardItemDefinition?> GetRewardAsync(int id, CancellationToken token) => inner.GetRewardAsync(id, token);
    }

}
