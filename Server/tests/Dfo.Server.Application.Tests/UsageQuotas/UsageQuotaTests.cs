using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.UsageQuotas;
using Dfo.Server.Application.DomainEvents;
using Dfo.Server.Application.Tests.Characters;
using Microsoft.Extensions.DependencyInjection;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.UsageQuotas;

public sealed class UsageQuotaTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 10, 12, 0, 0, TimeSpan.FromHours(8));
    private static readonly DateOnly Day = new(2026, 10, 10);

    [Fact]
    public async Task Direct_quota_subscriber_joins_business_transaction_and_a_later_subscriber_failure_rolls_back_all_rows_and_refresh_events()
    {
        await TestGameplay.RunAsync(async () =>
        {
            await using var f = new LearningFixture(configure: services =>
                services.AddRuntimeEventSubscriber<ConsumableUsageRecorded, FailingSubscriber>());
            await f.SeedAsync();
            var events = f.Services.GetRequiredService<IEventService>();
            var quotas = f.Services.GetRequiredService<IUsageQuotaDomainService>();
            await using (var tx = f.Scope.Begin())
            {
                await Assert.ThrowsAsync<IOException>(() => events.DirectAsync(
                    new ConsumableUsageRecorded(new(71), 3330, null, new(1, At))).AsTask());
                // The first subscriber really changed the shared database transaction.
                Assert.Equal(1, (await quotas.ReadAsync(new(71), UsageQuotaKey.Consumable(3330), default))!.UsedCount);
            }
            Assert.Null(await quotas.ReadAsync(new(71), UsageQuotaKey.Consumable(3330), default));
            Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM domain_events"));
        });
    }

    [Fact]
    public async Task All_three_families_preserve_original_storage_rows_and_reload_without_migration_or_replay_deductions()
    {
        await TestGameplay.RunAsync(async () =>
        {
            await using var f = new LearningFixture(); await f.SeedAsync();
            var consumables = f.Services.GetRequiredService<IConsumableUsageTable>();
            var premiums = f.Services.GetRequiredService<IPremiumServiceUsageTable>();
            var mirror = f.Services.GetRequiredService<IMirrorAradQuestDomainStore>();
            await using (var tx = f.Scope.Begin())
            {
                await consumables.TryWriteAsync(new(71), 3330, null, 300, At.AddDays(-1), default);
                await premiums.TryWriteAsync(new(71), "premium_free_gold_card_used", null, 4, At, default);
                await mirror.WriteQuotaAsync(new(1), new(71), Day, new(Entries: 6, Clears: 2, DailyRewardClaimed: true), default);
                await tx.CommitAsync(default);
            }
            var events = f.Services.GetRequiredService<IEventService>();
            await using (var tx = f.Scope.Begin())
            {
                await events.DirectAsync(new ConsumableUsageRecorded(new(71), 3330, new(300, At.AddDays(-1)), new(1, At)));
                await events.DirectAsync(new PremiumUsageRecorded(new(71), [new("premium_free_gold_card_used", new(4, At), 5, At)]));
                await events.DirectAsync(new MirrorEntryRecorded([new(new(1), new(71), Day, 6, 7)]));
                await tx.CommitAsync(default);
            }
            Assert.Equal(1, (await consumables.ReadAsync(new(71), 3330, default))!.UsedCount);
            Assert.Equal(At, (await consumables.ReadAsync(new(71), 3330, default))!.LastUsedAt);
            Assert.Equal(5, (await premiums.ReadAsync(new(71), "premium_free_gold_card_used", default))!.UsedCount);
            Assert.Equal(new MirrorDailyQuota(7, 2, true), await mirror.ReadQuotaAsync(new(71), Day, default));
            Assert.Equal(3, f.Scalar("SELECT COUNT(*) FROM domain_events WHERE event_type='usage-quota.changed'"));
            Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM domain_events WHERE event_type<>'usage-quota.changed'"));
            await using var reload = new LearningFixture(existing: f.Path);
            var quotas = reload.Services.GetRequiredService<IUsageQuotaDomainService>();
            Assert.Equal(new MirrorDailyQuota(7, 2, true), await quotas.ReadMirrorAsync(new(71), Day, default));
            Assert.Equal(new MirrorDailyQuota(), await quotas.ReadMirrorAsync(new(71), Day.AddDays(1), default));
            Assert.Equal(1, (await quotas.ReadAsync(new(71), UsageQuotaKey.Consumable(3330), default))!.UsedCount);
            Assert.Equal(5, (await quotas.ReadAsync(new(71), UsageQuotaKey.Premium("premium_free_gold_card_used"), default))!.UsedCount);
            Assert.Equal(1, f.Scalar("SELECT COUNT(*) FROM character_mirror_daily"));
        });
    }

    [Fact]
    public async Task Stale_multi_counter_batch_is_refused_before_any_sibling_is_changed()
    {
        await TestGameplay.RunAsync(async () =>
        {
            await using var f = new LearningFixture(); await f.SeedAsync();
            var events = f.Services.GetRequiredService<IEventService>();
            await using (var tx = f.Scope.Begin())
            {
                await events.DirectAsync(new PremiumUsageRecorded(new(71), [new("lottery_double_reward_total_used", null, 5, At)]));
                await tx.CommitAsync(default);
            }
            await using (var tx = f.Scope.Begin())
            {
                await Assert.ThrowsAsync<InvalidOperationException>(() => events.DirectAsync(new PremiumUsageRecorded(new(71), [
                    new("lottery_double_reward_used", null, 1, At), new("lottery_double_reward_total_used", null, 1, At)])).AsTask());
                Assert.Null(await f.Services.GetRequiredService<IUsageQuotaDomainService>()
                    .ReadAsync(new(71), UsageQuotaKey.Premium("lottery_double_reward_used"), default));
            }
            Assert.Equal(5, (await f.Services.GetRequiredService<IPremiumServiceUsageTable>()
                .ReadAsync(new(71), "lottery_double_reward_total_used", default))!.UsedCount);
            Assert.Equal(1, f.Scalar("SELECT COUNT(*) FROM domain_events"));
        });
    }

    [Fact]
    public async Task Quota_facts_reject_missing_or_readonly_transactions_and_invalid_batches_without_mutation()
    {
        await TestGameplay.RunAsync(async () =>
        {
            await using var f = new LearningFixture(); await f.SeedAsync();
            var events = f.Services.GetRequiredService<IEventService>();
            var fact = new ConsumableUsageRecorded(new(71), 3330, null, new(1, At));
            await Assert.ThrowsAsync<InvalidOperationException>(() => events.DirectAsync(fact).AsTask());
            await using (var tx = f.Scope.Begin(readOnly: true))
                await Assert.ThrowsAsync<InvalidOperationException>(() => events.DirectAsync(fact).AsTask());
            await using (var tx = f.Scope.Begin())
            {
                await Assert.ThrowsAsync<InvalidDataException>(() => events.DirectAsync(new PremiumUsageRecorded(new(71), [
                    new("premium_free_gold_card_used", null, 1, At), new("premium_free_gold_card_used", null, 2, At)])).AsTask());
                await Assert.ThrowsAsync<InvalidDataException>(() => events.DirectAsync(new PremiumUsageRecorded(new(71), [
                    new("premium_free_gold_card_used", null, 256, At)])).AsTask());
            }
            Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM character_daily_counters"));
            Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM domain_events"));
        });
    }

    [Fact]
    public async Task Quota_refresh_is_postcommit_coalesced_and_a_failed_projection_never_rolls_back_business_counts()
    {
        var clock = new FixedClock { Now = At };
        var bus = new QuotaBus();
        await using var f = new LearningFixture(configure: services =>
        {
            services.AddSingleton<TimeProvider>(clock);
            services.AddSingleton<INotificationBus>(bus);
        });
        await f.SeedAsync();
        bus.Read = async () =>
        {
            Assert.Null(f.Services.GetRequiredService<IDomainTransactionAccessor>().Current);
            return (await f.Services.GetRequiredService<IUsageQuotaSnapshotDomainService>()
                .ReadAsync(new(1), new(71), UsageQuotaFamily.MirrorDungeon, default)).Mirror!.Entries;
        };
        var events = f.Services.GetRequiredService<IEventService>();
        var dispatcher = f.Services.GetRequiredService<DomainEventDispatcher>();
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            await using (var tx = f.Scope.Begin())
            {
                await events.DirectAsync(new MirrorEntryRecorded([new(new(1), new(71), Day, 0, 1)]));
                await events.DirectAsync(new MirrorEntryRecorded([new(new(1), new(71), Day, 1, 2)]));
                Assert.Empty(bus.Entries);
                await Assert.ThrowsAsync<InvalidOperationException>(() => dispatcher.DrainOnceAsync().AsTask());
                await tx.CommitAsync(default);
            }
        });
        Assert.Empty(bus.Entries);
        Assert.Equal(2, await dispatcher.DrainOnceAsync());
        Assert.Equal(new[] { 2 }, bus.Entries);
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            await using (var tx = f.Scope.Begin())
                await events.DirectAsync(new MirrorEntryRecorded([new(new(1), new(71), Day, 2, 3)]));
        });
        Assert.Equal(0, await dispatcher.DrainOnceAsync());
        Assert.Equal(new[] { 2 }, bus.Entries);
        bus.Fail = true;
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            await using (var tx = f.Scope.Begin())
            {
                await events.DirectAsync(new MirrorEntryRecorded([new(new(1), new(71), Day, 2, 3)]));
                await tx.CommitAsync(default);
            }
        });
        Assert.Equal(1, await dispatcher.DrainOnceAsync());
        Assert.Equal(new[] { 2, 3 }, bus.Entries);
        Assert.Equal(3, (await f.Services.GetRequiredService<IUsageQuotaDomainService>().ReadMirrorAsync(new(71), Day, default)).Entries);
        Assert.Equal(0, await dispatcher.DrainOnceAsync());
    }

    [Fact]
    public async Task Mirror_snapshot_turns_at_the_game_reset_and_does_not_write_a_reset_or_emit_an_event()
    {
        await TestGameplay.RunAsync(async () =>
        {
            // 05:59 on the 11th is still the game day of the 10th; midnight is not a boundary.
            var clock = new FixedClock { Now = new(2026, 10, 11, 5, 59, 0, TimeSpan.FromHours(8)) };
            await using var f = new LearningFixture(configure: services => services.AddSingleton<TimeProvider>(clock));
            await f.SeedAsync();
            await using (var tx = f.Scope.Begin())
            {
                await f.Services.GetRequiredService<IMirrorAradQuestDomainStore>().WriteQuotaAsync(new(1), new(71), Day, new(6, 2, true), default);
                await tx.CommitAsync(default);
            }
            var snapshots = f.Services.GetRequiredService<IUsageQuotaSnapshotDomainService>();
            Assert.Equal(new MirrorDailyQuota(6, 2, true), (await snapshots.ReadAsync(new(1), new(71), UsageQuotaFamily.MirrorDungeon, default)).Mirror);
            clock.Now = clock.Now.AddMinutes(2);
            Assert.Equal(new MirrorDailyQuota(), (await snapshots.ReadAsync(new(1), new(71), UsageQuotaFamily.MirrorDungeon, default)).Mirror);
            Assert.Equal(1, f.Scalar("SELECT COUNT(*) FROM character_mirror_daily"));
            Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM domain_events"));
        });
    }

    private sealed class FixedClock : TimeProvider
    {
        public DateTimeOffset Now;
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class QuotaBus : INotificationBus
    {
        public Func<Task<int>> Read = null!;
        public List<int> Entries = [];
        public bool Fail;
        public async ValueTask<NotificationDelivery> PublishAsync<T>(NotificationAudience audience, T notification, CancellationToken cancellationToken)
            where T : class, INotification<T>
        {
            var view = Assert.IsType<UsageQuotaViewChanged>(notification);
            Assert.Equal(UsageQuotaFamily.MirrorDungeon, view.Family);
            Assert.Equal(view.CharacterId, Assert.Single(Assert.IsType<NotificationAudience.TheseCharacters>(audience).CharacterIds));
            Entries.Add(await Read());
            if (Fail) throw new IOException("transport unavailable");
            return new([new(view.CharacterId, NotificationOutcome.Registered)]);
        }
    }

    private sealed class FailingSubscriber : IDomainEventSubscriber<ConsumableUsageRecorded>
    {
        public ValueTask HandleAsync(DomainEventEnvelope<ConsumableUsageRecorded> message, CancellationToken ct) =>
            throw new IOException("A subsequent business consumer failed.");
    }
}
