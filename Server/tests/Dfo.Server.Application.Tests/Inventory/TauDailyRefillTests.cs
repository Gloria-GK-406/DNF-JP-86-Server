using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Inventory;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed partial class ItemDeliveryTests
{
    private sealed class SixOClock : IDailyResetRuleService
    {
        public DateTimeOffset PeriodStart(DateTimeOffset at)
        {
            var local = at.ToOffset(TimeSpan.FromHours(8));
            var start = new DateTimeOffset(local.Date.AddHours(6), TimeSpan.FromHours(8));
            return local < start ? start.AddDays(-1) : start;
        }
        public bool IsCurrentPeriod(DateTimeOffset recordedAt, DateTimeOffset now) => recordedAt >= PeriodStart(now) && recordedAt <= now;
    }
    private sealed class RefillContent : IItemPlacementDefinitionSource
    {
        public ValueTask<ItemPlacement?> GetAsync(int id, CancellationToken ct) => id == 9006
            ? ValueTask.FromResult<ItemPlacement?>(new(2, 1, new(BusinessInventorySpace.Consumable, 0, 55)))
            : new Content().GetAsync(id, ct);
    }
    private sealed class MixedDays : IDailyRefillRuleService
    {
        private readonly DailyRefillRuleService rules = new(new SixOClock());
        public bool UsesIndependentPeriod(DailyRefillRule rule) => rule.ItemId == 9001;
        public string PeriodOf(DateTimeOffset at) => rules.PeriodOf(at);
        public string PeriodOf(DailyRefillRule rule, DateTimeOffset at) => rules.PeriodOf(UsesIndependentPeriod(rule) ? rule with { ItemId = 690001556 } : rule, at);
        public bool IsDue(string? claimed, string period) => rules.IsDue(claimed, period);
        public bool IsDue(DailyRefillRule rule, string? claimed, string period) => rules.IsDue(UsesIndependentPeriod(rule) ? rule with { ItemId = 690001556 } : rule, claimed, period);
        public int GrantFor(DailyRefillRule rule, int count) => rules.GrantFor(rule, count);
    }

    [Fact]
    public async Task Per_item_game_day_survives_same_calendar_day_attempt_and_rollback()
    {
        using var f = new ShopFixture(s =>
        {
            s.AddSingleton<IItemPlacementDefinitionSource>(new RefillContent());
            s.AddSingleton<IDailyRefillRuleService>(new MixedDays());
        });
        var refill = f.Services.GetRequiredService<IDailyRefillDomainService>();
        var rule = new DailyRefillRule(9001, 1, DateTimeOffset.MaxValue, DailyRefillMode.Add, 10, 3,
            new(BusinessInventorySpace.Material, 0, 0));
        var before = new DateTimeOffset(2026, 10, 15, 5, 59, 0, TimeSpan.FromHours(8));
        foreach (var at in new[] { before, before.AddMinutes(1), before.AddMinutes(2) })
        {
            await using var tx = Scope(f).Begin();
            await refill.ApplyDailyRefillAsync(new(1), new(1), [rule], at, default);
            await tx.CommitAsync(default);
        }
        Assert.Equal(2, f.Count(9001));
        await using (var tx = Scope(f).Begin())
            await refill.ApplyDailyRefillAsync(new(1), new(1), [rule], before.AddDays(1).AddMinutes(1), default);
        Assert.Equal(2, f.Count(9001));
        await using (var tx = Scope(f).Begin())
        {
            await refill.ApplyDailyRefillAsync(new(1), new(1), [rule], before.AddDays(3), default);
            await tx.CommitAsync(default);
        }
        Assert.Equal(3, f.Count(9001)); // Missed offline periods are not accumulated.
    }

    [Fact]
    public void Only_tau_ticket_uses_game_day_and_legacy_midnight_claim_does_not_reissue()
    {
        var rules = new DailyRefillRuleService(new SixOClock());
        var tau = new DailyRefillRule(690001556, 1, DateTimeOffset.MaxValue, DailyRefillMode.Add, 10, 4, new(BusinessInventorySpace.Quest, 0, 55));
        var at = new DateTimeOffset(2026, 10, 15, 5, 59, 0, TimeSpan.FromHours(8));
        Assert.Equal("2026-10-14", rules.PeriodOf(tau, at));
        Assert.Equal("2026-10-15", rules.PeriodOf(tau, at.AddMinutes(1)));
        Assert.Equal("2026-10-15", rules.PeriodOf(tau with { ItemId = 9001 }, at));
        Assert.False(rules.IsDue(tau, "2026-10-15", rules.PeriodOf(tau, at)));
        Assert.False(rules.IsDue(tau, "2026-10-15", rules.PeriodOf(tau, at.AddMinutes(1))));
    }
    [Fact]
    public async Task Tau_only_online_refill_does_not_claim_ordinary_batch_and_new_ordinary_rules_wait_until_tomorrow()
    {
        using var f = new ShopFixture(s =>
        {
            s.AddSingleton<IItemPlacementDefinitionSource>(new RefillContent());
            s.AddSingleton<IDailyRefillRuleService>(new MixedDays());
        });
        var refill = f.Services.GetRequiredService<IDailyRefillDomainService>();
        var tau = new DailyRefillRule(9001, 1, DateTimeOffset.MaxValue, DailyRefillMode.Add, 10, 3,
            new(BusinessInventorySpace.Material, 0, 0));
        var ordinary = tau with { ItemId = 9005, Destination = new(BusinessInventorySpace.Consumable, 0, 55) };
        var addedLater = ordinary with { ItemId = 9006 };
        var at = new DateTimeOffset(2026, 10, 15, 6, 0, 0, TimeSpan.FromHours(8));
        async Task Apply(params DailyRefillRule[] rules)
        {
            await using var tx = Scope(f).Begin();
            await refill.ApplyDailyRefillAsync(new(1), new(1), rules, at, default);
            await tx.CommitAsync(default);
        }
        await using (var tx = Scope(f).Begin())
        {
            await refill.ApplyIndependentRefillAsync(new(1), new(1), [tau], at, default);
            await tx.CommitAsync(default);
        }
        await Apply(tau, ordinary);
        Assert.Equal(1, f.Count(9005));
        await Apply(tau, ordinary, addedLater);
        Assert.Equal(0, f.Count(9006));
        Assert.Equal(1, f.Count(9001));
        at = at.AddDays(1);
        await Apply(tau, ordinary, addedLater);
        Assert.Equal(1, f.Count(9006));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task First_login_claims_ordinary_batch_even_when_configuration_is_empty_or_tau_only(bool tauOnly)
    {
        using var f = new ShopFixture(s =>
        {
            s.AddSingleton<IItemPlacementDefinitionSource>(new RefillContent());
            s.AddSingleton<IDailyRefillRuleService>(new MixedDays());
        });
        var refill = f.Services.GetRequiredService<IDailyRefillDomainService>();
        var tau = new DailyRefillRule(9001, 1, DateTimeOffset.MaxValue, DailyRefillMode.Add, 10, 3,
            new(BusinessInventorySpace.Material, 0, 0));
        var ordinary = tau with { ItemId = 9005, Destination = new(BusinessInventorySpace.Consumable, 0, 55) };
        var at = f.Clock.GetUtcNow();
        await using (var tx = Scope(f).Begin())
        {
            await refill.ApplyDailyRefillAsync(new(1), new(1), tauOnly ? [tau] : [], at, default);
            await tx.CommitAsync(default);
        }
        await using (var tx = Scope(f).Begin())
        {
            await refill.ApplyDailyRefillAsync(new(1), new(1), [tau, ordinary], at, default);
            await tx.CommitAsync(default);
        }
        Assert.Equal(0, f.Count(9005));
    }

}
