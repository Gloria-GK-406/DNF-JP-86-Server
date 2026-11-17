using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Events;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Tests.Characters;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Events;

[Trait("Category", "Unit")]
public sealed class GameplayParameterBoundaryTests
{
    [Fact]
    public async Task New_spending_changes_at_period_boundaries_and_refunds_keep_the_actual_ledger()
    {
        var clock = new ParameterClock();
        await using var f = new LearningFixture(configure: s => s.AddSingleton<TimeProvider>(clock));
        await TestGameplay.RunAsync(async () =>
        {
            await f.SeedAsync();
            var fatigue = f.Services.GetRequiredService<ICharacterFatigueDomainService>();
            var start = clock.Now.AddMinutes(1);
            var end = start.AddMinutes(1);
            await ConfigureAndEnable(f, 1, clock.Now, start, end);
            async Task Spend(ushort amount)
            {
                await using var tx = f.Scope.Begin();
                await fatigue.SpendAsync(f.Session.CharacterId, amount, default);
                await tx.CommitAsync(default);
            }
            await Spend(7);
            clock.Now = start;
            await Spend(11);
            Assert.Equal(7, (await fatigue.BalanceOfAsync(f.Session.CharacterId, default)).Used);
            clock.Now = end;
            await Spend(3);
            await using (var tx = f.Scope.Begin())
            {
                await fatigue.RefundAsync(f.Session.CharacterId, 2, default);
                await tx.CommitAsync(default);
            }
            var balance = await fatigue.BalanceOfAsync(f.Session.CharacterId, default);
            Assert.Equal(8, balance.Used);
            Assert.Equal(10, balance.ConsumedToday);
        });
    }

    [Fact]
    public async Task Neutral_queries_observe_the_callers_transaction_and_do_not_cache_rolled_back_configuration()
    {
        var clock = new ParameterClock();
        await using var f = new LearningFixture(configure: s => s.AddSingleton<TimeProvider>(clock));
        await TestGameplay.RunAsync(async () =>
        {
            var fatigue = f.Services.GetRequiredService<IFatigueConsumptionQuery>();
            var drops = f.Services.GetRequiredService<IDropDrawMultiplierDomainService>();
            await using (var tx = f.Scope.Begin())
            {
                var events = f.Services.GetRequiredService<IEventManagementDomainService>();
                foreach (var id in new[] { 1, 7 })
                {
                    await events.ChangeAsync(new(id, 0, "configure", "测试", "测试", "", "", "",
                        clock.Now.AddMinutes(-1), clock.Now.AddMinutes(1),
                        id == 7 ? new Dictionary<string, int> { ["world"] = 3 } : null), "test", clock.Now, default);
                    await events.ChangeAsync(new(id, 1, "enable"), "test", clock.Now, default);
                }
                Assert.False(await fatigue.ShouldConsumeAsync(default));
                Assert.Equal(3, (await drops.CurrentAsync(default)).For(DropDrawEntry.World));
            }
            Assert.True(await fatigue.ShouldConsumeAsync(default));
            Assert.Equal(1, (await drops.CurrentAsync(default)).For(DropDrawEntry.World));
        });
    }

    [Fact]
    public async Task Fatigue_admission_and_spending_follow_the_period_without_reconciliation()
    {
        var clock = new ParameterClock();
        await using var f = new LearningFixture(configure: s => s.AddSingleton<TimeProvider>(clock));
        await TestGameplay.RunAsync(async () =>
        {
            await f.SeedAsync();
            var fatigue = f.Services.GetRequiredService<ICharacterFatigueDomainService>();
            await using (var tx = f.Scope.Begin())
            {
                await fatigue.SpendAsync(f.Session.CharacterId, ushort.MaxValue, default);
                await tx.CommitAsync(default);
            }
            var start = clock.Now.AddMinutes(1);
            var end = start.AddMinutes(1);
            await ConfigureAndEnable(f, 1, clock.Now, start, end);
            Assert.False(await fatigue.CanEnterDungeonAsync(f.Session.CharacterId, default));
            clock.Now = start;
            Assert.True(await fatigue.CanEnterDungeonAsync(f.Session.CharacterId, default));
            var before = await fatigue.BalanceOfAsync(f.Session.CharacterId, default);
            await using (var tx = f.Scope.Begin())
            {
                await fatigue.SpendAsync(f.Session.CharacterId, 1, default);
                await tx.CommitAsync(default);
            }
            Assert.Equal(before, await fatigue.BalanceOfAsync(f.Session.CharacterId, default));
            clock.Now = end;
            Assert.False(await fatigue.CanEnterDungeonAsync(f.Session.CharacterId, default));
        });
    }

    [Fact]
    public async Task Draw_multipliers_follow_the_period_without_reconciliation_and_old_results_stay_frozen()
    {
        var clock = new ParameterClock();
        await using var f = new LearningFixture(configure: s => s.AddSingleton<TimeProvider>(clock));
        await TestGameplay.RunAsync(async () =>
        {
            var start = clock.Now.AddMinutes(1);
            var end = start.AddMinutes(1);
            await ConfigureAndEnable(f, 7, clock.Now, start, end, new Dictionary<string, int> { ["world"] = 3 });
            var query = f.Services.GetRequiredService<IDropDrawMultiplierDomainService>();
            Assert.Equal(1, (await query.CurrentAsync(default)).For(DropDrawEntry.World));
            clock.Now = start;
            var frozen = await query.CurrentAsync(default);
            Assert.Equal(3, frozen.For(DropDrawEntry.World));
            Assert.Equal(1, frozen.For(DropDrawEntry.MonsterGold));
            clock.Now = end;
            Assert.Equal(1, (await query.CurrentAsync(default)).For(DropDrawEntry.World));
            Assert.Equal(3, frozen.For(DropDrawEntry.World));
        });
    }

    private static async Task ConfigureAndEnable(LearningFixture f, int id, DateTimeOffset now,
        DateTimeOffset start, DateTimeOffset end, IReadOnlyDictionary<string, int>? multipliers = null)
    {
        await using var tx = f.Scope.Begin();
        var events = f.Services.GetRequiredService<IEventManagementDomainService>();
        await events.ChangeAsync(new(id, 0, "configure", "测试活动", "测试", "", "", "", start, end,
            multipliers), "test", now, default);
        await events.ChangeAsync(new(id, 1, "enable"), "test", now, default);
        await tx.CommitAsync(default);
    }

    private sealed class ParameterClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2030, 1, 2, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
