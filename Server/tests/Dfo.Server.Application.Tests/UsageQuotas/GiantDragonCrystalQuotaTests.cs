using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.UsageQuotas;
using Dfo.Server.Application.Tests.Characters;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.UsageQuotas;

public sealed class GiantDragonCrystalQuotaTests
{
    [Fact]
    public async Task Existing_claim_is_preserved_and_next_period_claim_rolls_back_then_commits_through_quota_owner()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var clock = new Clock();
            await using var f = new LearningFixture(configure: s => s.AddSingleton<TimeProvider>(clock));
            await f.SeedAsync();
            var reset = f.Services.GetRequiredService<IDailyResetRuleService>();
            var previousPeriod = reset.PeriodStart(clock.Now);
            f.Sql($"INSERT INTO character_daily_counters(character_id,counter_key,period,value) VALUES(71,'giant-dragon.quest.8557','{previousPeriod.ToUnixTimeSeconds()}',1)");
            var exchange = f.Services.GetRequiredService<IGiantDragonCrystalExchangeDomainService>();
            var quotas = f.Services.GetRequiredService<IUsageQuotaDomainService>();
            Assert.False(await exchange.CanAcceptAsync(new(71), default));
            Assert.Equal(1, (await quotas.ReadAsync(new(71), UsageQuotaKey.GiantDragonCrystalExchange(previousPeriod), default))!.UsedCount);
            clock.Now = clock.Now.AddDays(1);
            var period = reset.PeriodStart(clock.Now);
            Assert.True(await exchange.CanAcceptAsync(new(71), default));
            await using (var tx = f.Scope.Begin())
            {
                Assert.True(await exchange.TryCompleteAsync(new(71), default));
                Assert.False(await exchange.TryCompleteAsync(new(71), default));
            }
            Assert.Equal(previousPeriod.ToUnixTimeSeconds(), f.Scalar("SELECT CAST(period AS INTEGER) FROM character_daily_counters WHERE counter_key='giant-dragon.quest.8557'"));
            Assert.True(await exchange.CanAcceptAsync(new(71), default));
            await using (var tx = f.Scope.Begin())
            {
                Assert.True(await exchange.TryCompleteAsync(new(71), default));
                await tx.CommitAsync(default);
            }
            Assert.False(await exchange.CanAcceptAsync(new(71), default));
            Assert.Equal(period.ToUnixTimeSeconds(), f.Scalar("SELECT CAST(period AS INTEGER) FROM character_daily_counters WHERE counter_key='giant-dragon.quest.8557'"));
            Assert.Equal(1, f.Scalar("SELECT COUNT(*) FROM character_daily_counters WHERE counter_key='giant-dragon.quest.8557'"));
        });
    }

    [Fact]
    public async Task Claim_fact_requires_writable_transaction_and_rejects_a_stale_expected_value()
    {
        await TestGameplay.RunAsync(async () =>
        {
            await using var f = new LearningFixture();
            await f.SeedAsync();
            var events = f.Services.GetRequiredService<IEventService>();
            var fact = new GiantDragonCrystalExchangeRecorded(new(71), new Clock().Now, null);
            await Assert.ThrowsAsync<InvalidOperationException>(() => events.DirectAsync(fact).AsTask());
            await using (var tx = f.Scope.Begin(readOnly: true))
                await Assert.ThrowsAsync<InvalidOperationException>(() => events.DirectAsync(fact).AsTask());
            await using (var tx = f.Scope.Begin())
            {
                await events.DirectAsync(fact);
                await Assert.ThrowsAsync<InvalidOperationException>(() => events.DirectAsync(fact).AsTask());
            }
            Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM character_daily_counters WHERE counter_key='giant-dragon.quest.8557'"));
            Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM domain_events"));
        });
    }

    [Fact]
    public async Task Storage_CAS_cannot_overwrite_this_period_with_a_stale_null_claim()
    {
        await TestGameplay.RunAsync(async () =>
        {
            await using var f = new LearningFixture();
            await f.SeedAsync();
            f.Sql("INSERT INTO character_daily_counters(character_id,counter_key,period,value) VALUES(71,'giant-dragon.quest.8557',NULL,1)");
            var store = f.Services.GetRequiredService<IUsageQuotaStore>();
            var key = UsageQuotaKey.GiantDragonCrystalExchange(new Clock().Now);
            var write = new UsageQuotaWrite(new(71), key, null, new(1, null));
            await using var tx = f.Scope.Begin();
            Assert.True(await store.CompareExchangeAsync(write, default));
            Assert.False(await store.CompareExchangeAsync(write, default));
            Assert.False(await store.CompareExchangeAsync(write with { Expected = new(0, null) }, default));
            await tx.CommitAsync(default);
            Assert.Equal(new UsageQuotaValue(1, null), await store.ReadAsync(new(71), key, default));
        });
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 28, 12, 0, 0, TimeSpan.FromHours(8));
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
