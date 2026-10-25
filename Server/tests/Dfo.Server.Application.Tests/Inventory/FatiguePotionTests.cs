using Dfo.Server.Application.Tests.Support;
using System.Collections.Immutable;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Tests.Characters;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;
using Dfo.Server.Persistence.Database;
using Microsoft.Extensions.DependencyInjection;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class FatiguePotionTests
{
    [Fact]
    public async Task Actual_fatigue_handler_outer_exclusive_borrows_effect_lifecycle_and_commits_once()
    {
        var content = new Content();
        using var shop = new ShopFixture(services =>
        {
            services.AddSingleton<IStackableActionDefinitionSource>(content);
            services.AddSingleton<IItemUsageRuleSource>(content);
            services.AddSingleton<IItemUseRuleDefinitionSource>(new FakeItemUseRules((2600026, ItemUseRules.Potion(0))));
        });
        await shop.Seed(65, 2600026, 2);
        await TestGameplay.RunAsync(shop.Services, async () =>
        {
            await using var tx = shop.Services.GetRequiredService<Dfo.Server.Application.Transactions.DomainTransactionScope>().Begin();
            await shop.Services.GetRequiredService<ICharacterFatigueDomainService>().SpendAsync(shop.Session.CharacterId, 50, default);
            await tx.CommitAsync(default);
        });
        var request = new Dfo.Server.Protocol.Game.QuestClearCoupon.QuestClearCouponRequest(
            new(1, 0x0204, 0, 1), 65, 0, 0, 58, 0, 0, true);
        await EffectRequestTransport.Send(shop, request);
        Assert.Equal(1, shop.Count(2600026));
        Assert.Contains(shop.Sender.Messages.OfType<Dfo.Server.Protocol.Game.QuestClearCoupon.QuestClearCouponResponse>(), response => response.Succeeded);
    }

    private static readonly DateTimeOffset Morning = new(2026, 9, 29, 9, 0, 0, TimeSpan.FromHours(8));
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = Morning;
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class Content : IStackableActionDefinitionSource, IItemUsageRuleSource
    {
        public StackableActionDefinition Rule = new("[add fatigue]", 1, 85, 0, true)
            { FatigueRecovery = 30 };
        public ItemUsageRule Usage = new(1, 10000, true);
        ValueTask<ItemUsageRule?> IItemUsageRuleSource.GetAsync(int id, CancellationToken token) =>
            ValueTask.FromResult<ItemUsageRule?>(Usage);
        public ValueTask<StackableActionDefinition?> GetAsync(int id, CancellationToken token) => ValueTask.FromResult<StackableActionDefinition?>(Rule);
    }
    // Exercise the real shared SQLite transaction: the test bag writes through the same database participant.
    private sealed class Bag(SqliteDatabase db, IApplyEffectsDomainService executor) : IOpeningInventoryDomainService
    {
        public ValueTask<IReadOnlyList<Dfo.Server.Data.Accounts.AccountPremiumEntryState>> ReadPremiumsAsync(SelectedSessionIdentity session, CancellationToken ct) => throw new NotSupportedException();
        public async ValueTask<OpeningEffectsApplication> ApplyAsync(SelectedSessionIdentity session, IReadOnlyList<DomainEffect> plan, DateTimeOffset at, IEffectFootprintRecorder footprint, CancellationToken ct)
        {
            Assert.Equal(1, Assert.Single(plan.OfType<ConsumeFromContainerEffect>()).Count);
            await db.ExecuteAsync("UPDATE characters SET bonus_sp=bonus_sp-1 WHERE character_id=@id", p => p.AddWithValue("@id", session.CharacterId.Value), ct);
            var applied = await executor.ApplyAsync(new(session.AccountId, session.CharacterId, at, footprint, session), plan.Where(x => x is not ConsumeFromContainerEffect).ToArray(), ct);
            return new(new(session, [], [], [], at), applied);
        }
        public ValueTask<ItemReference> SourceAsync(SelectedSessionIdentity session, BusinessSlot slot, DateTimeOffset now, CancellationToken token) =>
            ValueTask.FromResult(TestItemReferences.FromCore(slot.Index,
                StackableItemCore.Create(2, slot.Index switch { 0 => 2600026, 1 => 10000541, 2 => 7456, 3 => 416, _ => throw new InvalidOperationException() }, 5),
                ItemWorldClass.Interchangeable));

    }
    private static LearningFixture Fixture(Clock clock, Content content, string? existing = null) => new(existing: existing,
        configure: services =>
        {
            services.AddSingleton<TimeProvider>(clock);
            services.AddSingleton<IStackableActionDefinitionSource>(content);
            services.AddSingleton<IItemUsageRuleSource>(content);
            services.AddSingleton<IOpeningInventoryDomainService, Bag>();
            services.AddSingleton<IItemUseRuleDefinitionSource>(new FakeItemUseRules((2600026, ItemUseRules.Potion(0)), (10000541, ItemUseRules.Potion(0)), (7456, ItemUseRules.Potion(0)), (416, ItemUseRules.Potion(0))));
        });
    private static async Task Spend(LearningFixture f, ushort amount)
    {
        await using var tx = f.Scope.Begin();
        await f.Services.GetRequiredService<ICharacterFatigueDomainService>().SpendAsync(f.Session.CharacterId, amount, default);
        await tx.CommitAsync(default);
    }
    private static async Task Use(LearningFixture f, Clock clock, int item = 2600026)
    {
        await using var tx = f.Scope.Begin();
        var slot = item switch { 2600026 => 65, 10000541 => 66, 7456 => 67, 416 => 68, _ => throw new InvalidOperationException() };
        await TestEffectFootprints.Run(f.Session, cap => f.Services.GetRequiredService<IFatiguePotionDomainService>().UseAsync(f.Session,
            new BusinessSlot(BusinessInventorySpace.Consumable, slot - 65), clock.Now, cap, default));
        await tx.CommitAsync(default);
    }

    [Fact]
    public async Task Success_restores_balance_and_consumes_once_relogin_keeps_quota_items_are_independent()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var clock = new Clock(); var content = new Content();
            await using var f = Fixture(clock, content);
            await f.SeedAsync(); await Spend(f, 100);
            await Use(f, clock);
            var balance = await f.Services.GetRequiredService<ICharacterFatigueDomainService>().BalanceOfAsync(f.Session.CharacterId, default);
            Assert.Equal(70, balance.Used); Assert.Equal(100, balance.ConsumedToday);
            Assert.Equal(4, f.Scalar("SELECT bonus_sp FROM characters WHERE character_id=71"));
            clock.Now = clock.Now.AddSeconds(11);
            await Assert.ThrowsAsync<OpeningRejectedException>(() => Use(f, clock));
            await Use(f, clock, 10000541);
            Assert.Equal(2, f.Scalar("SELECT COUNT(*) FROM character_daily_counters WHERE counter_key LIKE 'consumable_used_%'"));
            await using var reload = Fixture(clock, content, f.Path);
            var row = await reload.Services.GetRequiredService<IConsumableUsageTable>().ReadAsync(f.Session.CharacterId, 2600026, default);
            Assert.Equal(1, row!.UsedCount); Assert.Equal(Morning, row.LastUsedAt);
            Assert.Null(await reload.Services.GetRequiredService<IConsumableUsageTable>().ReadAsync(new(72), 2600026, default));
        });
    }

    [Fact]
    public async Task Business_day_resets_quota_but_does_not_shorten_24_hour_cooldown()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var clock = new Clock { Now = new DateTimeOffset(2026, 9, 29, 5, 59, 0, TimeSpan.FromHours(8)) };
            var content = new Content();
            await using var f = Fixture(clock, content);
            await f.SeedAsync(); await Spend(f, 100); await Use(f, clock);
            clock.Now = clock.Now.AddMinutes(2);
            await Spend(f, 100); await Use(f, clock);
            Assert.Equal(1, f.Scalar("SELECT value FROM character_daily_counters WHERE counter_key='consumable_used_2600026'"));

            content.Usage = new(-1, 86400000, true);
            await Use(f, clock, 7456);
            clock.Now = clock.Now.AddHours(23).AddMinutes(59);
            await Assert.ThrowsAsync<OpeningRejectedException>(() => Use(f, clock, 7456));
            clock.Now = clock.Now.AddMinutes(1);
            await Spend(f, 40); await Use(f, clock, 7456);
        });
    }

    [Theory]
    [InlineData("full")]
    [InlineData("expired")]
    [InlineData("level")]
    [InlineData("dungeon")]
    [InlineData("wrong")]
    public async Task Refusal_does_not_consume_or_record(string reason)
    {
        await TestGameplay.RunAsync(async () =>
        {
            var clock = new Clock(); var content = new Content();
            await using var f = Fixture(clock, content);
            await f.SeedAsync();
            if (reason != "full") await Spend(f, 100);
            if (reason == "expired") content.Rule = content.Rule with { ExpiresAtUnixSeconds = clock.Now.ToUnixTimeSeconds() };
            if (reason == "level") content.Rule = content.Rule with { MaximumLevel = 20 };
            if (reason == "wrong") content.Rule = content.Rule with { Action = "[quest clear]" };
            if (reason == "dungeon")
            {
                var towns = f.Services.GetRequiredService<ITownAreaDomainService>();
                towns.LeaveExact(f.Session.CharacterId, towns.Find(f.Session.CharacterId)!.Lease, TownAreaDepartureReason.DungeonEntry);
            }
            await Assert.ThrowsAsync<OpeningRejectedException>(() => Use(f, clock));
            Assert.Equal(5, f.Scalar("SELECT bonus_sp FROM characters WHERE character_id=71"));
            Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM character_daily_counters WHERE counter_key LIKE 'consumable_used_%'"));
        });
    }

    [Fact]
    public async Task Three_use_item_allows_exactly_three_successes_and_checks_cooldown()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var clock = new Clock(); var content = new Content();
            content.Rule = content.Rule with { FatigueRecovery = 50 };
            content.Usage = new(3, 1000, true);
            await using var f = Fixture(clock, content);
            await f.SeedAsync(); await Spend(f, 188);
            await Use(f, clock, 416);
            await Assert.ThrowsAsync<OpeningRejectedException>(() => Use(f, clock, 416));
            for (var i = 0; i < 2; i++) { clock.Now = clock.Now.AddSeconds(1); await Use(f, clock, 416); }
            await Spend(f, 100); // Ensure the fourth request fails on quota, not lack of recovery space.
            clock.Now = clock.Now.AddSeconds(1);
            await Assert.ThrowsAsync<OpeningRejectedException>(() => Use(f, clock, 416));
            Assert.Equal(3, f.Scalar("SELECT value FROM character_daily_counters WHERE counter_key='consumable_used_416'"));
            Assert.Equal(2, f.Scalar("SELECT bonus_sp FROM characters WHERE character_id=71"));
        });
    }

    [Fact]
    public async Task Counter_write_failure_rolls_back_consumption_and_recovery()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var clock = new Clock(); var content = new Content();
            await using var f = Fixture(clock, content);
            await f.SeedAsync(); await Spend(f, 100);
            f.Sql("CREATE TRIGGER reject_potion BEFORE INSERT ON character_daily_counters BEGIN SELECT RAISE(ABORT,'injected'); END;");
            await Assert.ThrowsAnyAsync<Exception>(() => Use(f, clock));
            Assert.Equal(5, f.Scalar("SELECT bonus_sp FROM characters WHERE character_id=71"));
            Assert.Equal(100, f.Scalar("SELECT used FROM character_fatigue WHERE character_id=71"));
        });
    }
}
