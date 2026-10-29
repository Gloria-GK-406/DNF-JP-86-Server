using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Application.Tests.Transactions;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.RecoverStamina;
using Dfo.Server.Protocol.Game.Weakness;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class WeaknessGoldRecoveryTests
{
    [Fact]
    public async Task Tower_member_cannot_buy_weakness_recovery()
    {
        _ = TowerPresenceProxy.Create(out var presence);
        using var shop = new ShopFixture(services => presence.Register(services));
        presence.Members.Add(shop.Session.CharacterId);
        var state = await Weaken(shop);

        await Recover(shop);

        Assert.Equal(4, Assert.IsType<RecoverStaminaResponse>(Assert.Single(shop.Sender.Messages)).ErrorCode);
        Assert.Equal(10, state.Value);
        Assert.Equal(100000, shop.Gold);
    }
    private sealed class PremiumRules : IPremiumServiceDefinitionSource
    {
        public IReadOnlyList<int> ReadQuestHelperMinimumDropRates() => new[] { 100, 70, 30 };
        public DoublePotRules ReadDoublePotRules() => new(8, new HashSet<int>());
        public IReadOnlyList<int> ReadGrowthBossEquipmentBonusPercents() => new[] {20, 23, 27, 30};
        public double ReadBlackDiamondExperienceBonusRate() => .05;
        public double ReadGrowthExperienceBonusRate() => 0.2;
        public PremiumContractLevels ReadContractLevels() => new(5, 10);
        public int ReadFreeWeaknessDailyLimit() => 10;
        public int ReadFreeGoldCardDailyLimit() => 10;
        public int ReadDungeonBuffDailyLimit() => 10;
        public int ReadAutoRepairDailyLimit() => 6;
    }

    private static ShopFixture PremiumShop() => new(services =>
        services.AddSingleton<IPremiumServiceDefinitionSource>(new PremiumRules()));

    [Fact]
    public async Task Timestamp_counter_rejects_a_stale_write_instead_of_losing_consumption()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var shop = PremiumShop();
            var scope = (DomainTransactionScope)shop.Services.GetRequiredService<IDomainTransactionAccessor>();
            await using var transaction = scope.Begin();
            var table = shop.Services.GetRequiredService<Dfo.Server.Abstractions.Persistence.IPremiumServiceUsageTable>();
            var id = shop.Session.CharacterId;
            var now = shop.Clock.GetUtcNow();
            Assert.Equal(1, await table.TryWriteAsync(id, "premium_free_weakness_used", null, 1, now, default));
            Assert.Equal(0, await table.TryWriteAsync(id, "premium_free_weakness_used", null, 1, now, default));
            var first = await table.ReadAsync(id, "premium_free_weakness_used", default);
            Assert.Equal(1, await table.TryWriteAsync(id, "premium_free_weakness_used", first, 2, now, default));
            Assert.Equal(0, await table.TryWriteAsync(id, "premium_free_weakness_used", first, 2, now, default));
            Assert.Equal(2, (await table.ReadAsync(id, "premium_free_weakness_used", default))!.UsedCount);
        });
    }

    [Fact]
    public async Task Timestamp_counter_survives_midnight_then_resets_at_six_without_appending_rows()
    {
        using var shop = PremiumShop();
        shop.Sql("INSERT INTO account_premiums(account_id,premium_type,end_time) VALUES(1,585,4000000000)");
        await Weaken(shop);
        await Recover(shop, 1); // The fixture starts at 20:00 Beijing time.
        var usage = shop.Services.GetRequiredService<IPremiumServiceUsageDomainService>();
        Assert.Equal(1, shop.Scalar("SELECT COUNT(*) FROM character_daily_counters WHERE last_consumed_at IS NOT NULL AND period IS NULL"));
        shop.Clock.Advance(TimeSpan.FromHours(9) + TimeSpan.FromMinutes(59));
        Assert.Equal(1, (await usage.ReadAsync(shop.Session.CharacterId, default)).Single(x => x.SlotIndex == 5).UsedCount);
        shop.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(0, (await usage.ReadAsync(shop.Session.CharacterId, default)).Single(x => x.SlotIndex == 5).UsedCount);
        Assert.Equal(1, shop.Scalar("SELECT value FROM character_daily_counters WHERE counter_key='premium_free_weakness_used'"));
        await Weaken(shop);
        await Recover(shop, 1);
        Assert.Equal(1, shop.Scalar("SELECT COUNT(*) FROM character_daily_counters"));
        var row = await shop.Services.GetRequiredService<Dfo.Server.Abstractions.Persistence.IPremiumServiceUsageTable>()
            .ReadAsync(shop.Session.CharacterId, "premium_free_weakness_used", default);
        Assert.Equal(shop.Clock.GetUtcNow(), row!.LastConsumedAt);
        Assert.Equal(1, row.UsedCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(4000000000000L)]
    public async Task Missing_or_future_timestamp_is_not_current_usage(long? timestamp)
    {
        using var shop = PremiumShop();
        shop.Sql($"INSERT INTO character_daily_counters(character_id,counter_key,period,value,last_consumed_at) VALUES(1,'premium_free_weakness_used','2026-09-08',10,{timestamp?.ToString() ?? "NULL"})");
        var usage = shop.Services.GetRequiredService<IPremiumServiceUsageDomainService>();
        Assert.Equal(0, (await usage.ReadAsync(shop.Session.CharacterId, default)).Single(x => x.SlotIndex == 5).UsedCount);
        Assert.Equal(10, shop.Scalar("SELECT value FROM character_daily_counters WHERE counter_key='premium_free_weakness_used'"));
    }

    [Fact]
    public async Task Contract_recovery_is_free_limited_persistent_and_renews_on_the_next_day()
    {
        using var shop = PremiumShop();
        shop.Sql("INSERT INTO account_premiums(account_id,premium_type,end_time) VALUES(1,585,4000000000)");
        for (var i = 1; i <= 10; i++)
        {
            var state = await Weaken(shop);
            await Recover(shop, 1);
            Assert.Equal(100, state.Value);
            Assert.Equal(100000, shop.Gold);
            Assert.Equal(shop.Gold, shop.Sender.Messages.OfType<RecoverStaminaResponse>().Last().Gold);
            Assert.Equal(i, shop.Scalar("SELECT value FROM character_daily_counters WHERE counter_key='premium_free_weakness_used'"));
            Assert.Equal(i, (await shop.Services.GetRequiredService<IPremiumServiceUsageDomainService>()
                .ReadAsync(shop.Session.CharacterId, default)).Single(x => x.SlotIndex == 5).UsedCount);
            Assert.Equal("PremiumServiceStateMessage", shop.Sender.Messages.Last().GetType().Name);
        }
        var exhausted = await Weaken(shop);
        await Recover(shop, 1);
        Assert.Equal(4, Assert.IsType<RecoverStaminaResponse>(shop.Sender.Messages.Last()).ErrorCode);
        Assert.Equal(10, exhausted.Value);
        // Re-selecting does not reset persisted usage.
        TestGameplay.Run(shop.Services, () => shop.Services.GetRequiredService<Dfo.Server.Abstractions.Inventory.ICharacterContainerDomainService>().Clear(shop.Session.CharacterId));
        shop.Select(1);
        await Recover(shop, 1);
        Assert.Equal(10, exhausted.Value);
        shop.Clock.Advance(TimeSpan.FromDays(1));
        Assert.Equal(0, (await shop.Services.GetRequiredService<IPremiumServiceUsageDomainService>()
            .ReadAsync(shop.Session.CharacterId, default)).Single(x => x.SlotIndex == 5).UsedCount);
        await Recover(shop, 1);
        Assert.Equal(100, exhausted.Value);
        Assert.Equal(1, shop.Scalar("SELECT value FROM character_daily_counters WHERE counter_key='premium_free_weakness_used'"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task Missing_or_expired_contract_never_falls_back_to_gold(long expiry)
    {
        using var shop = PremiumShop();
        if (expiry != 0) shop.Sql($"INSERT INTO account_premiums(account_id,premium_type,end_time) VALUES(1,585,{expiry})");
        var state = await Weaken(shop);
        await Recover(shop, 1);
        Assert.Equal(10, state.Value);
        Assert.Equal(100000, shop.Gold);
        Assert.Equal(0, shop.Scalar("SELECT COUNT(*) FROM character_daily_counters"));
        Assert.Equal(4, Assert.IsType<RecoverStaminaResponse>(shop.Sender.Messages.Last()).ErrorCode);
    }

    [Fact]
    public async Task Concurrent_contract_requests_consume_once_and_counter_failure_preserves_weakness()
    {
        using var shop = PremiumShop();
        shop.Sql("INSERT INTO account_premiums(account_id,premium_type,end_time) VALUES(1,585,4000000000)");
        var state = await Weaken(shop);
        await Task.WhenAll(Recover(shop, 1).AsTask(), Recover(shop, 1).AsTask());
        Assert.Equal(100, state.Value);
        Assert.Equal(1, shop.Scalar("SELECT value FROM character_daily_counters WHERE counter_key='premium_free_weakness_used'"));
        Assert.Equal(18, Assert.IsType<RecoverStaminaResponse>(shop.Sender.Messages.Last()).ErrorCode);
        await TestGameplay.RunAsync(shop.Services, async () => await state.SetWeakness());
        shop.Sql("CREATE TRIGGER refuse_usage BEFORE UPDATE ON character_daily_counters BEGIN SELECT RAISE(ABORT,'usage failure'); END;");
        await Recover(shop, 1);
        Assert.Equal(10, state.Value);
        Assert.Equal(100000, shop.Gold);
        Assert.Equal(1, shop.Scalar("SELECT value FROM character_daily_counters WHERE counter_key='premium_free_weakness_used'"));
    }

    [Fact]
    public async Task Daily_projection_refreshes_online_usage_and_drops_replaced_sessions()
    {
        using var shop = PremiumShop();
        shop.Sql("INSERT INTO account_premiums(account_id,premium_type,end_time) VALUES(1,585,4000000000)");
        await Weaken(shop);
        await Recover(shop, 1);
        var old = shop.Session;
        var refresh = ActivatorUtilities.CreateInstance<Dfo.Server.Protocol.Game.StateSynchronization.PremiumDailyRefreshService>(shop.Services);
        shop.Sender.Messages.Clear();
        await refresh.RefreshAsync(default);
        Assert.Equal("PremiumServiceStateMessage", Assert.Single(shop.Sender.Messages).GetType().Name);
        await refresh.RefreshAsync(default);
        Assert.Single(shop.Sender.Messages);
        shop.Clock.Advance(TimeSpan.FromDays(1));
        await refresh.RefreshAsync(default);
        Assert.Equal(2, shop.Sender.Messages.Count);
        var next = await Dfo.Server.Application.Tests.Support.TimerDomainTestDriver.PremiumAsync(shop.Services, shop.Session);
        Assert.Equal(0, next.Usage.Single(x => x.SlotIndex == 5).UsedCount);
        shop.Select(2);
        await refresh.RefreshAsync(default);
        Assert.Equal(3, shop.Sender.Messages.Count);
        var stale = await Dfo.Server.Application.Tests.Support.TimerDomainTestDriver.PremiumAsync(shop.Services, old);
        Assert.False(stale.IsCurrent);
    }

    [Fact]
    public async Task Usage_is_per_character_and_post_increment_failure_rolls_it_back()
    {
        using var shop = PremiumShop();
        shop.Sql("INSERT INTO account_premiums(account_id,premium_type,end_time) VALUES(1,585,4000000000)");
        var state = await Weaken(shop);
        shop.Sql($"INSERT INTO character_daily_counters(character_id,counter_key,period,value,last_consumed_at) VALUES(1,'lottery_double_reward_used',NULL,-1,{shop.Clock.GetUtcNow().ToUnixTimeMilliseconds()})");
        await Recover(shop, 1);
        Assert.Equal(10, state.Value);
        Assert.Equal(0, shop.Scalar("SELECT COUNT(*) FROM character_daily_counters WHERE counter_key='premium_free_weakness_used'"));
        shop.Sql("DELETE FROM character_daily_counters");
        await Recover(shop, 1);
        shop.Select(2);
        shop.Sql($"UPDATE characters SET level=85,exp={shop.ExperienceForLevel(85)} WHERE character_id=2");
        var other = shop.Services.GetRequiredService<IWeaknessDomainService>().For(shop.Session.CharacterId);
        await TestGameplay.RunAsync(shop.Services, async () => await other.SetWeakness());
        await Recover(shop, 1);
        Assert.Equal(100, other.Value);
        Assert.Equal(2, shop.Scalar("SELECT COUNT(*) FROM character_daily_counters WHERE counter_key='premium_free_weakness_used' AND value=1"));
        Assert.Equal(100000, shop.Gold);
    }

    [Fact]
    public async Task Concurrent_requests_charge_only_once()
    {
        using var shop = new ShopFixture();
        var state = await Weaken(shop);
        await Task.WhenAll(Recover(shop).AsTask(), Recover(shop).AsTask());
        Assert.Equal(43470, shop.Gold);
        Assert.Equal(100, state.Value);
        Assert.Equal(new byte[] { 0, 18 }, shop.Sender.Messages.OfType<RecoverStaminaResponse>().Select(item => item.ErrorCode));
    }

    [Theory]
    [InlineData(85, 32, 42711)]
    [InlineData(85, 46, 33918)]
    [InlineData(85, 82, 11306)]
    [InlineData(85, 10, 56530)]
    [InlineData(85, 100, 0)]
    [InlineData(20, 10, 5000)]
    [InlineData(19, 10, 0)]
    [InlineData(255, 10, 61780)]
    [InlineData(99, 0, 68644)]
    public void Client_price_table_matches_samples_and_boundaries(byte level, byte value, int expected) =>
        Assert.Equal(expected, new WeaknessRuleService(new ConfigurationBuilder().Build()).RecoveryGoldCost(level, value));

    [Fact]
    public async Task Paid_recovery_commits_gold_and_weakness_before_projection_and_duplicate_cannot_charge()
    {
        using var shop = new ShopFixture();
        var state = await Weaken(shop);
        Assert.Equal(56530, await state.RecoveryGoldCost());
        shop.Sender.BeforeSend = () => { Assert.Equal(43470, shop.Gold); Assert.Equal(100, state.Value); };
        await Recover(shop);
        Assert.Equal(3, shop.Sender.Messages.Count);
        Assert.Equal(shop.Gold, Assert.IsType<RecoverStaminaResponse>(shop.Sender.Messages.Last()).Gold);
        Assert.Equal(0, Assert.IsType<RecoverStaminaResponse>(shop.Sender.Messages.Last()).ErrorCode);
        await Recover(shop);
        Assert.Equal(18, Assert.IsType<RecoverStaminaResponse>(shop.Sender.Messages.Last()).ErrorCode);
        Assert.Equal(43470, shop.Gold);
        Assert.Equal(0, await state.RecoveryGoldCost());
    }

    [Fact]
    public async Task Insufficient_gold_retains_weakness_and_money()
    {
        using var shop = new ShopFixture();
        var state = await Weaken(shop);
        shop.Sql("UPDATE character_stats SET carried_gold=100 WHERE character_id=1");
        await Recover(shop);
        Assert.Equal(22, Assert.IsType<RecoverStaminaResponse>(Assert.Single(shop.Sender.Messages)).ErrorCode);
        Assert.Equal(10, state.Value);
        Assert.Equal(100, shop.Gold);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(65535)]
    public async Task Unsupported_service_flags_do_not_charge_or_grant_recovery(ushort mode)
    {
        using var shop = new ShopFixture();
        var state = await Weaken(shop);
        await Recover(shop, mode);
        Assert.Equal(4, Assert.IsType<RecoverStaminaResponse>(Assert.Single(shop.Sender.Messages)).ErrorCode);
        Assert.Equal(10, state.Value);
        Assert.Equal(100000, shop.Gold);
    }

    [Fact]
    public async Task Quote_uses_current_recovery_value_instead_of_an_old_dialog_price()
    {
        using var shop = new ShopFixture();
        var state = await Weaken(shop);
        Assert.Equal(56530, await state.RecoveryGoldCost());
        await shop.Services.GetRequiredService<ICommandBus>().SendInternalAsync(new RecoverWeaknessTickCommand(),
            new(GameplayCallSource.Timer, GameplayCallChain.NewRoot(GameplayCallSource.Timer), "timer:WeaknessRecoveryService"));
        Assert.Equal(19, Assert.Single(shop.Sender.Messages.OfType<WeaknessProjectionNotification>()).Value);
        Assert.Equal(19, state.Value);
        Assert.Equal(50877, await state.RecoveryGoldCost());
        await Recover(shop);
        Assert.Equal(49123, shop.Gold);
        Assert.Equal(100, state.Value);
    }

    [Fact]
    public async Task A_database_write_failure_leaves_both_gold_and_weakness_unchanged()
    {
        using var shop = new ShopFixture();
        var state = await Weaken(shop);
        shop.Sql("CREATE TRIGGER refuse_gold BEFORE UPDATE OF carried_gold ON character_stats BEGIN SELECT RAISE(ABORT,'gold failure'); END;");
        await Recover(shop);
        Assert.Equal(4, Assert.IsType<RecoverStaminaResponse>(Assert.Single(shop.Sender.Messages)).ErrorCode);
        Assert.Equal(100000, shop.Gold);
        Assert.Equal(10, state.Value);
    }

    [Fact]
    public async Task Failure_at_final_durable_commit_does_not_publish_memory_recovery()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var shop = new ShopFixture();
            var state = await Weaken(shop);
            var scope = (DomainTransactionScope)shop.Services.GetRequiredService<IDomainTransactionAccessor>();
            await using var transaction = scope.Begin();
            var durable = new RecordingParticipant("database", [], true) { CommitFailure = new IOException("commit failed") };
            scope.Current!.Enlist(durable, () => durable);
            state.Recover();
            Assert.Equal(10, state.Value);
            await Assert.ThrowsAsync<IOException>(() => transaction.CommitAsync(default).AsTask());
            Assert.Equal(10, state.Value);
        });
    }

    [Fact]
    public async Task A_replaced_session_cannot_recover_the_old_character()
    {
        Dfo.Server.Data.World.SelectedSessionIdentity? stale = null;
        using var shop = new ShopFixture(services =>
        {
            var original = services.Last(x => x.ServiceType == typeof(Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService));
            services.AddSingleton<Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService>(provider =>
                StaleSessionQuery.Wrap((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)(original.ImplementationInstance
                    ?? original.ImplementationFactory?.Invoke(provider) ?? ActivatorUtilities.CreateInstance(provider, original.ImplementationType!)), () => stale));
        });
        var state = await Weaken(shop);
        var old = shop.Session;
        shop.Select(2);
        stale = old;
        var result = await shop.Services.GetRequiredService<ICommandBus>().SendAsync(
            new GameRecoverStaminaCommand(new(shop.Connection), new(GameMessageMetadata.Empty, 0)));
        Assert.Equal(4, result.ErrorCode);
        Assert.Equal(10, state.Value);
        Assert.Equal(100000, shop.Scalar("SELECT carried_gold FROM character_stats WHERE character_id=1"));
        Assert.Equal(100000, shop.Scalar("SELECT carried_gold FROM character_stats WHERE character_id=2"));
    }

    public class StaleSessionQuery : System.Reflection.DispatchProxy
    {
        private Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService actual = null!;
        private Func<Dfo.Server.Data.World.SelectedSessionIdentity?> stale = null!;
        internal static Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService Wrap(
            Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService actual, Func<Dfo.Server.Data.World.SelectedSessionIdentity?> stale)
        {
            var proxy = Create<Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService, StaleSessionQuery>();
            ((StaleSessionQuery)proxy).actual = actual; ((StaleSessionQuery)proxy).stale = stale; return proxy;
        }
        protected override object? Invoke(System.Reflection.MethodInfo? method, object?[]? args)
        {
            if (method!.Name == "FindByConnection" && stale() is { } old) return old;
            try { return method.Invoke(actual, args); }
            catch (System.Reflection.TargetInvocationException error) when (error.InnerException is { } cause)
            { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(cause); throw; }
        }
    }

    [Fact]
    public async Task A_selected_session_not_yet_in_world_is_silently_ignored_by_the_recovery_protocol()
    {
        using var shop = new ShopFixture();
        var state = await Weaken(shop);
        await TestGameplay.RunAsync(shop.Services, async () =>
        {
            var sessions = shop.Services.GetRequiredService<Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService>();
            Assert.True(sessions.ReleaseExact(shop.Session));
            Assert.Null(sessions.Select(shop.Session));
            Assert.Equal(shop.Session, sessions.FindByConnection(shop.Connection.ConnectionId));
            Assert.False(sessions.IsInWorld(shop.Session));
        });
        await Recover(shop);
        Assert.Empty(shop.Sender.Messages);
        Assert.Equal(100000, shop.Gold);
        Assert.Equal(10, state.Value);
    }

    private static async Task<IWeakness> Weaken(ShopFixture shop)
    {
        shop.Sql($"UPDATE characters SET level=85,exp={shop.ExperienceForLevel(85)} WHERE character_id=1");
        var state = shop.Services.GetRequiredService<IWeaknessDomainService>().For(shop.Session.CharacterId);
        await TestGameplay.RunAsync(shop.Services, async () => await state.SetWeakness());
        Assert.Equal(10, state.Value);
        return state;
    }

    private static ValueTask Recover(ShopFixture shop, ushort mode = 0) =>
        shop.Services.GetRequiredService<Dfo.Server.Abstractions.Networking.INetworkMessageDispatcher>().DispatchAsync(new(shop.Connection),
            new RecoverStaminaRequest(GameMessageMetadata.Empty, mode), default);
}
