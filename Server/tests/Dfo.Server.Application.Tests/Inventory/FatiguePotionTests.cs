using System.Collections.Immutable;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
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

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class FatiguePotionTests
{
    private static readonly DateTimeOffset Morning = new(2026, 9, 29, 9, 0, 0, TimeSpan.FromHours(8));
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = Morning;
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class Content : IStackableActionDefinitionSource
    {
        public StackableActionDefinition Rule = new("[add fatigue]", 1, 85, 0, true)
            { FatigueRecovery = 30, DailyUseLimit = 1, CooldownMilliseconds = 10000 };
        public ValueTask<StackableActionDefinition?> GetAsync(int id, CancellationToken token) => ValueTask.FromResult<StackableActionDefinition?>(Rule);
    }
    // Exercise the real shared SQLite transaction: the test bag writes through the same database participant.
    private sealed class Bag(SqliteDatabase db) : IOpeningInventoryDomainService
    {
        public ValueTask<ContainerSlotItem> SourceAsync(SelectedSessionIdentity session, int slot, DateTimeOffset now, CancellationToken token) =>
            ValueTask.FromResult(new ContainerSlotItem(slot, StackableItemCore.Create(2, slot, 5), ItemWorldClass.Interchangeable));
        public async ValueTask<OpeningInventoryReceipt> SettleAsync(SelectedSessionIdentity session,
            IReadOnlyList<OpeningSlotConsumption> consumed, ImmutableArray<OpeningPreparedGrant> grants,
            DateTimeOffset now, CancellationToken token)
        {
            Assert.Equal(1, Assert.Single(consumed).Count);
            Assert.Empty(grants);
            await db.ExecuteAsync("UPDATE characters SET bonus_sp=bonus_sp-1 WHERE character_id=@id",
                p => p.AddWithValue("@id", session.CharacterId.Value), token);
            return new(session, [], [], [], now);
        }
    }
    private static LearningFixture Fixture(Clock clock, Content content, string? existing = null) => new(existing: existing,
        configure: services =>
        {
            services.AddSingleton<TimeProvider>(clock);
            services.AddSingleton<IStackableActionDefinitionSource>(content);
            services.AddSingleton<IOpeningInventoryDomainService, Bag>();
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
        await f.Services.GetRequiredService<IFatiguePotionDomainService>().UseAsync(f.Session, item, clock.Now, default);
        await tx.CommitAsync(default);
    }

    [Fact]
    public async Task Success_restores_balance_and_consumes_once_relogin_keeps_quota_items_are_independent()
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
    }

    [Fact]
    public async Task Business_day_resets_quota_but_does_not_shorten_24_hour_cooldown()
    {
        var clock = new Clock { Now = new DateTimeOffset(2026, 9, 29, 5, 59, 0, TimeSpan.FromHours(8)) };
        var content = new Content();
        await using var f = Fixture(clock, content);
        await f.SeedAsync(); await Spend(f, 100); await Use(f, clock);
        clock.Now = clock.Now.AddMinutes(2);
        await Spend(f, 100); await Use(f, clock);
        Assert.Equal(1, f.Scalar("SELECT value FROM character_daily_counters WHERE counter_key='consumable_used_2600026'"));

        content.Rule = content.Rule with { DailyUseLimit = -1, CooldownMilliseconds = 86400000 };
        await Use(f, clock, 7456);
        clock.Now = clock.Now.AddHours(23).AddMinutes(59);
        await Assert.ThrowsAsync<OpeningRejectedException>(() => Use(f, clock, 7456));
        clock.Now = clock.Now.AddMinutes(1);
        await Spend(f, 40); await Use(f, clock, 7456);
    }

    [Theory]
    [InlineData("full")]
    [InlineData("expired")]
    [InlineData("level")]
    [InlineData("dungeon")]
    [InlineData("wrong")]
    public async Task Refusal_does_not_consume_or_record(string reason)
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
    }

    [Fact]
    public async Task Three_use_item_allows_exactly_three_successes_and_checks_cooldown()
    {
        var clock = new Clock(); var content = new Content();
        content.Rule = content.Rule with { DailyUseLimit = 3, FatigueRecovery = 50, CooldownMilliseconds = 1000 };
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
    }

    [Fact]
    public async Task Counter_write_failure_rolls_back_consumption_and_recovery()
    {
        var clock = new Clock(); var content = new Content();
        await using var f = Fixture(clock, content);
        await f.SeedAsync(); await Spend(f, 100);
        f.Sql("CREATE TRIGGER reject_potion BEFORE INSERT ON character_daily_counters BEGIN SELECT RAISE(ABORT,'injected'); END;");
        await Assert.ThrowsAnyAsync<Exception>(() => Use(f, clock));
        Assert.Equal(5, f.Scalar("SELECT bonus_sp FROM characters WHERE character_id=71"));
        Assert.Equal(100, f.Scalar("SELECT used FROM character_fatigue WHERE character_id=71"));
    }
}
