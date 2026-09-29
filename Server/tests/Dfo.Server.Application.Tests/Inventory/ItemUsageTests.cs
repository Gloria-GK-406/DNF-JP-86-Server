using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Application.Tests.Characters;
using Dfo.Server.Data.Characters;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class ItemUsageTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 9, 29, 5, 59, 0, TimeSpan.FromHours(8));
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class Rules : IItemUsageRuleSource
    {
        public ValueTask<ItemUsageRule?> GetAsync(int id, CancellationToken token) =>
            ValueTask.FromResult<ItemUsageRule?>(id switch
            {
                1 or 2 => new(1, 1000, true),
                3 => new(-1, 86400000, true),
                4 => new(1000, 0, true),
                _ => null
            });
    }
    private static LearningFixture Fixture(Clock clock, string? existing = null) => new(existing: existing,
        configure: s => { s.AddSingleton<TimeProvider>(clock); s.AddSingleton<IItemUsageRuleSource>(new Rules()); });
    private static async Task Use(LearningFixture f, CharacterId id, int item)
    {
        await using var tx = f.Scope.Begin();
        await f.Services.GetRequiredService<IItemUsageDomainService>().RecordUseAsync(id, item, default);
        await tx.CommitAsync(default);
    }
    [Fact]
    public async Task Generic_records_are_independent_durable_and_roll_over_without_erasing_cooldown()
    {
        var clock = new Clock();
        await using var f = Fixture(clock); await f.SeedAsync();
        var usage = f.Services.GetRequiredService<IItemUsageDomainService>();
        var id = f.Session.CharacterId;
        Assert.Equal(1, (await usage.GetAsync(id, 1, default)).Remaining);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await usage.RecordUseAsync(id, 1, default));
        await Use(f, id, 1); await Use(f, id, 2); await Use(f, new(72), 1); await Use(f, id, 3);
        Assert.Equal(ItemUsageRejection.DailyLimitReached,
            (await Assert.ThrowsAsync<ItemUsageRejectedException>(() => Use(f, id, 1))).Reason);
        Assert.Equal(2, (await usage.GetSnapshotAsync(id, default)).Count); // unlimited item is not a quota projection
        clock.Now = clock.Now.AddMinutes(2);
        var snapshot = await usage.GetSnapshotAsync(id, default);
        Assert.All(snapshot, s => { Assert.Equal(0, s.UsedCount); Assert.Equal(1, s.Remaining); });
        var cooldown = await usage.GetAsync(id, 3, default);
        Assert.Null(cooldown.DailyLimit); Assert.NotNull(cooldown.CooldownUntil);
        Assert.Equal(ItemUsageRejection.CoolingDown,
            (await Assert.ThrowsAsync<ItemUsageRejectedException>(() => Use(f, id, 3))).Reason);
        await Use(f, id, 1);
        await using var reload = Fixture(clock, f.Path);
        Assert.Equal(1, (await reload.Services.GetRequiredService<IItemUsageDomainService>().GetAsync(id, 1, default)).UsedCount);
        // Expired quota reads did not rewrite the original full timestamp used for elapsed cooldown.
        Assert.Equal(new DateTimeOffset(2026, 9, 29, 5, 59, 0, TimeSpan.FromHours(8)),
            (await f.Services.GetRequiredService<IConsumableUsageTable>().ReadAsync(id, 3, default))!.LastUsedAt);
    }
    [Fact]
    public async Task Rollback_leaves_no_usage_and_domain_count_is_not_limited_to_wire_byte()
    {
        var clock = new Clock(); await using var f = Fixture(clock); await f.SeedAsync();
        var usage = f.Services.GetRequiredService<IItemUsageDomainService>();
        await using (var tx = f.Scope.Begin()) { await usage.RecordUseAsync(f.Session.CharacterId, 1, default); }
        Assert.Empty(await usage.GetSnapshotAsync(f.Session.CharacterId, default));
        await using (var tx = f.Scope.Begin())
        {
            await f.Services.GetRequiredService<IConsumableUsageTable>().TryWriteAsync(f.Session.CharacterId, 4, null, 300, clock.Now, default);
            await tx.CommitAsync(default);
        }
        Assert.Equal(300, (await usage.GetAsync(f.Session.CharacterId, 4, default)).UsedCount);
        await Assert.ThrowsAsync<InvalidDataException>(async () => await usage.GetAsync(f.Session.CharacterId, 999, default));
    }
}
