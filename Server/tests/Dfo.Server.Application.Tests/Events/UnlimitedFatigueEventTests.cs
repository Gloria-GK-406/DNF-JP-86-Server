using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Events;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.DomainEvents;
using Dfo.Server.Application.Events;
using Dfo.Server.Application.Scheduling;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Events;
using Dfo.Server.Application.Tests.Characters;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Events;

public sealed class UnlimitedFatigueEventTests
{
    private const int ActivityId = 1_000_230;

    [Fact]
    public async Task Scheduled_failure_rolls_back_one_activity_continues_the_scan_and_retries_on_the_next_run()
    {
        var clock = new Clock { Now = DateTimeOffset.UtcNow };
        var observed = new Observed { FailActivation = true };
        await using var f = new LearningFixture(configure: s =>
        {
            s.AddSingleton<TimeProvider>(clock);
            s.AddSingleton(observed);
            s.AddRuntimeEventSubscriber<ManagedEventActivationChanged, Observer>();
            s.AddSingleton<IManagedEventDomainService>(new LaterActivity());
        });
        await TestGameplay.RunAsync(async () =>
        {
            await Configure(f, clock.Now, clock.Now.AddHours(1), clock.Now.AddHours(2));
            await Change(f, 1, "enable", clock.Now);
        });
        clock.Now = clock.Now.AddHours(1);
        await Assert.ThrowsAsync<AggregateException>(async () => await Scheduled(f, clock.Now));
        Assert.False(await Suspended(f));
        var lifecycle = f.Services.GetRequiredService<IManagedEventLifecycleDomainService>();
        Assert.False((await lifecycle.AppliedAsync(ActivityId, default))!.Active);
        Assert.True((await lifecycle.AppliedAsync(LaterActivity.Id, default))!.Active);
        observed.FailActivation = false;
        await Scheduled(f, clock.Now);
        Assert.True(await Suspended(f));
    }

    private static async Task Scheduled(LearningFixture f, DateTimeOffset now)
    {
        var state = new ScheduledTaskRunState();
        state.Reset(now);
        await f.Services.GetRequiredService<ICommandBus>().SendInternalAsync(
            new ScheduledGameplayTaskCommand("activity.reconciliation", state, 1),
            new(GameplayCallSource.Timer, GameplayCallChain.NewRoot(GameplayCallSource.Timer), "timer:unlimited-fatigue-test"), default);
    }

    private sealed class LaterActivity : IManagedEventDomainService
    {
        internal const int Id = 1_999_999;
        public int EventId => Id;
        public ValueTask<bool> IsActiveAsync(DateTimeOffset now, CancellationToken ct) => ValueTask.FromResult(true);
        public ValueTask<ManagedEventEntry> DescribeAsync(DateTimeOffset now, CancellationToken ct) => ValueTask.FromResult(
            new ManagedEventEntry(Id, "probe", 1, "initial", true, "active", "probe", "probe", "", "", "",
                null, null, true, null, null, null));
        public ValueTask<EventInfo[]> GetEventInfoEntriesAsync(AccountId account, DateTimeOffset now, CancellationToken ct) => ValueTask.FromResult<EventInfo[]>([]);
        public ValueTask ChangeAsync(EventChange change, string actor, DateTimeOffset now, CancellationToken ct) => throw new NotSupportedException();
    }

    [Fact]
    public void Fatigue_policy_has_one_production_writer_and_no_gm_control_consumer()
    {
        var consumers = typeof(ApplicationModule).Assembly.GetTypes()
            .Where(type => !type.IsAbstract && type.GetConstructors()
                .SelectMany(c => c.GetParameters()).Any(p => p.ParameterType == typeof(IFatigueConsumptionControlDomainService)))
            .ToArray();
        Assert.Equal(typeof(UnlimitedFatigueActivationSubscriber), Assert.Single(consumers));
    }

    [Fact]
    public async Task Zero_balance_is_admissible_only_while_the_activity_effect_is_applied()
    {
        await TestGameplay.RunAsync(async () =>
        {
            await using var f = new LearningFixture();
            await f.SeedAsync();
            var fatigue = f.Services.GetRequiredService<ICharacterFatigueDomainService>();
            await using (var tx = f.Scope.Begin())
            {
                await fatigue.SpendAsync(f.Session.CharacterId, ushort.MaxValue, default);
                await tx.CommitAsync(default);
            }
            Assert.False(await fatigue.CanEnterDungeonAsync(f.Session.CharacterId, default));
            var now = DateTimeOffset.UtcNow;
            await Configure(f, now, now.AddHours(-1), now.AddHours(1));
            await Change(f, 1, "enable", now);
            Assert.True(await fatigue.CanEnterDungeonAsync(f.Session.CharacterId, default));
            var table = f.Services.GetRequiredService<ICharacterFatigueTable>();
            var row = await table.ReadAsync(f.Session.CharacterId, default);
            await using (var tx = f.Scope.Begin())
            {
                await fatigue.SpendAsync(f.Session.CharacterId, ushort.MaxValue, default);
                await tx.CommitAsync(default);
            }
            Assert.Equal(row, await table.ReadAsync(f.Session.CharacterId, default));
            await Change(f, 2, "disable", now);
            Assert.False(await fatigue.CanEnterDungeonAsync(f.Session.CharacterId, default));
        });
    }

    [Fact]
    public async Task Clock_boundaries_reconcile_once_and_live_reconfiguration_reapplies_the_revision()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var observed = new Observed();
            await using var f = new LearningFixture(configure: s =>
            {
                s.AddSingleton(observed);
                s.AddRuntimeEventSubscriber<ManagedEventActivationChanged, Observer>();
            });
            var start = DateTimeOffset.UtcNow.AddHours(1);
            var end = start.AddHours(1);
            await Configure(f, start.AddHours(-1), start, end);
            await Change(f, 1, "enable", start.AddTicks(-1));
            Assert.False(await Suspended(f));
            observed.Changes.Clear();
            Assert.True(await Reconcile(f, start));
            Assert.True(await Suspended(f));
            Assert.False(await Reconcile(f, start));
            Assert.Single(observed.Changes);
            await using (var tx = f.Scope.Begin())
            {
                await f.Services.GetRequiredService<IEventManagementDomainService>().ChangeAsync(new(ActivityId, 2, "configure",
                    "无限疲劳", "免疲劳", "调整排期", "", "", start, end.AddHours(1)), "test", start, default);
                await tx.CommitAsync(default);
            }
            Assert.Equal(3, observed.Changes.Last().ConfigurationRevision);
            Assert.True(observed.Changes.Last().Active);
            Assert.False(await Reconcile(f, end));
            Assert.True(await Reconcile(f, end.AddHours(1)));
            Assert.False(await Suspended(f));
            Assert.False(observed.Changes.Last().Active);
            Assert.Equal(3, observed.Changes.Last().ConfigurationRevision);
            Assert.False(await Reconcile(f, end.AddHours(1)));
            var entry = Assert.Single((await f.Services.GetRequiredService<IEventManagementDomainService>()
                .ListAsync(end.AddHours(1), default)).Entries, e => e.EventId == ActivityId);
            Assert.Equal(entry.Revision, entry.AppliedRevision);
            Assert.False(entry.AppliedActive);
        });
    }

    [Fact]
    public async Task Subscriber_failure_rolls_back_configuration_policy_and_watermark_then_retry_succeeds()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var observed = new Observed { FailActivation = true };
            await using var f = new LearningFixture(configure: s =>
            {
                s.AddSingleton(observed);
                s.AddRuntimeEventSubscriber<ManagedEventActivationChanged, Observer>();
            });
            var now = DateTimeOffset.UtcNow;
            await Configure(f, now, now.AddHours(-1), now.AddHours(1));
            await Assert.ThrowsAsync<InvalidOperationException>(() => Change(f, 1, "enable", now));
            Assert.False(await Suspended(f));
            var events = f.Services.GetRequiredService<IEventManagementDomainService>();
            var unchanged = Assert.Single((await events.ListAsync(now, default)).Entries, e => e.EventId == ActivityId);
            Assert.Equal(1, unchanged.Revision);
            Assert.False(unchanged.Enabled);
            Assert.False(unchanged.AppliedActive);
            Assert.Equal(1, unchanged.AppliedRevision);
            observed.FailActivation = false;
            await Change(f, 1, "enable", now);
            Assert.True(await Suspended(f));
        });
    }

    [Fact]
    public async Task Persisted_effect_is_restored_or_expired_by_the_real_startup_command()
    {
        var clock = new Clock { Now = DateTimeOffset.UtcNow };
        await using var f = new LearningFixture(configure: s => s.AddSingleton<TimeProvider>(clock));
        await TestGameplay.RunAsync(async () =>
        {
            await Configure(f, clock.Now, clock.Now.AddHours(-1), clock.Now.AddHours(1));
            await Change(f, 1, "enable", clock.Now);
        });
        await using (var reopened = new LearningFixture(existing: f.Path, configure: s => s.AddSingleton<TimeProvider>(clock)))
        {
            await Initialize(reopened);
            Assert.True(await Suspended(reopened));
        }
        clock.Now = clock.Now.AddHours(2);
        await using var expired = new LearningFixture(existing: f.Path, configure: s => s.AddSingleton<TimeProvider>(clock));
        Assert.True(await Suspended(expired));
        await Initialize(expired);
        Assert.False(await Suspended(expired));
    }

    [Fact]
    public async Task Offline_whole_period_does_not_replay_activation_and_no_unverified_wire_identity_is_sent()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var observed = new Observed();
            await using var f = new LearningFixture(configure: s =>
            {
                s.AddSingleton(observed);
                s.AddRuntimeEventSubscriber<ManagedEventActivationChanged, Observer>();
            });
            var now = DateTimeOffset.UtcNow;
            await Configure(f, now, now.AddHours(1), now.AddHours(2));
            await Change(f, 1, "enable", now);
            observed.Changes.Clear();
            Assert.False(await Reconcile(f, now.AddHours(3)));
            Assert.Empty(observed.Changes);
            var owner = Assert.Single(f.Services.GetServices<IManagedEventDomainService>(), e => e.EventId == ActivityId);
            Assert.Empty(await owner.GetEventInfoEntriesAsync(new(1), now.AddMinutes(90), default));
        });
    }

    private static async Task Configure(LearningFixture f, DateTimeOffset now, DateTimeOffset start, DateTimeOffset end)
    {
        await using var tx = f.Scope.Begin();
        await f.Services.GetRequiredService<IEventManagementDomainService>().ChangeAsync(new(ActivityId, 0, "configure",
            "无限疲劳", "免疲劳", "测试", "", "", start, end), "test", now, default);
        await tx.CommitAsync(default);
    }

    private static async Task Change(LearningFixture f, long revision, string action, DateTimeOffset now)
    {
        await using var tx = f.Scope.Begin();
        await f.Services.GetRequiredService<IEventManagementDomainService>().ChangeAsync(new(ActivityId, revision, action), "test", now, default);
        await tx.CommitAsync(default);
    }

    private static async Task<bool> Reconcile(LearningFixture f, DateTimeOffset now)
    {
        await using var tx = f.Scope.Begin();
        var result = await f.Services.GetRequiredService<IManagedEventLifecycleDomainService>().ReconcileAsync(ActivityId, now, default);
        await tx.CommitAsync(default);
        return result;
    }

    private static ValueTask<bool> Suspended(LearningFixture f) =>
        f.Services.GetRequiredService<IFatigueConsumptionControlDomainService>().IsSuspendedAsync(default);

    private static ValueTask<bool> Initialize(LearningFixture f) => f.Services.GetRequiredService<ICommandBus>()
        .SendInternalAsync(new InitializeManagedEventsCommand(), new(GameplayCallSource.StartupRecovery,
            GameplayCallChain.NewRoot(GameplayCallSource.StartupRecovery), "startup:unlimited-fatigue-test"), default);

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; }
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Observed
    {
        public bool FailActivation { get; set; }
        public List<ManagedEventActivationChanged> Changes { get; } = [];
    }

    private sealed class Observer(Observed observed) : IDomainEventSubscriber<ManagedEventActivationChanged>
    {
        public ValueTask HandleAsync(DomainEventEnvelope<ManagedEventActivationChanged> message, CancellationToken ct)
        {
            if (message.Payload.EventId != ActivityId) return ValueTask.CompletedTask;
            if (message.Payload.Active && observed.FailActivation) throw new InvalidOperationException("Injected activation failure.");
            observed.Changes.Add(message.Payload);
            return ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task Activity_preserves_real_usage_and_disabling_restores_spending()
    {
        await TestGameplay.RunAsync(async () =>
        {
            await using var f = new LearningFixture();
            await f.SeedAsync();
            var fatigue = f.Services.GetRequiredService<ICharacterFatigueDomainService>();
            var events = f.Services.GetRequiredService<IEventManagementDomainService>();
            var now = DateTimeOffset.UtcNow;
            await using (var tx = f.Scope.Begin())
            {
                await fatigue.SpendAsync(f.Session.CharacterId, 7, default);
                await events.ChangeAsync(new(ActivityId, 0, "configure", "无限疲劳", "免疲劳", "测试", "", "",
                    now.AddHours(-1), now.AddHours(1)), "test", now, default);
                await events.ChangeAsync(new(ActivityId, 1, "enable"), "test", now, default);
                await tx.CommitAsync(default);
            }
            var before = await fatigue.BalanceOfAsync(f.Session.CharacterId, default);
            await using (var tx = f.Scope.Begin())
            {
                await fatigue.SpendAsync(f.Session.CharacterId, 1, default);
                await fatigue.SpendAsync(f.Session.CharacterId, 10, default);
                await tx.CommitAsync(default);
            }
            Assert.Equal(before, await fatigue.BalanceOfAsync(f.Session.CharacterId, default));
            await using (var tx = f.Scope.Begin())
            {
                await events.ChangeAsync(new(ActivityId, 2, "disable"), "test", now, default);
                await fatigue.SpendAsync(f.Session.CharacterId, 1, default);
                await tx.CommitAsync(default);
            }
            Assert.Equal(before with { Used = (ushort)(before.Used + 1), ConsumedToday = before.ConsumedToday + 1 },
                await fatigue.BalanceOfAsync(f.Session.CharacterId, default));
        });
    }
}
