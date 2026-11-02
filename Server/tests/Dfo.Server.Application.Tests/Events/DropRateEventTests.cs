using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Events;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.DomainEvents;
using Dfo.Server.Application.Events;
using Dfo.Server.Application.Tests.Characters;
using Dfo.Server.Data.Events;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Events;

public sealed class DropRateEventTests
{
    private const int ActivityId = DropRateEventDomainService.ActivityId;
    private static readonly IReadOnlyDictionary<string, int> Configured = new Dictionary<string, int>
    {
        ["monster.equipment"] = 20,
        ["world"] = 5,
    };

    [Fact]
    public void Multipliers_have_one_production_writer()
    {
        var consumers = typeof(ApplicationModule).Assembly.GetTypes()
            .Where(type => !type.IsAbstract && type.GetConstructors()
                .SelectMany(c => c.GetParameters()).Any(p => p.ParameterType == typeof(IDropDrawMultiplierControlDomainService)))
            .ToArray();
        Assert.Equal(typeof(DropRateActivationSubscriber), Assert.Single(consumers));
    }

    [Fact]
    public async Task Period_applies_the_configured_entries_and_its_end_restores_one() =>
        await TestGameplay.RunAsync(async () =>
        {
            await using var f = new LearningFixture();
            var now = DateTimeOffset.UtcNow;
            await Configure(f, 0, now, now.AddHours(1), now.AddHours(2), Configured);
            await Change(f, 1, "enable", now);
            Assert.Equal(1, await Multiplier(f, DropDrawEntry.MonsterEquipment));

            Assert.True(await Reconcile(f, now.AddHours(1)));
            Assert.Equal(20, await Multiplier(f, DropDrawEntry.MonsterEquipment));
            Assert.Equal(5, await Multiplier(f, DropDrawEntry.World));
            Assert.Equal(1, await Multiplier(f, DropDrawEntry.MonsterGold));
            Assert.False(await Reconcile(f, now.AddHours(1)));

            Assert.True(await Reconcile(f, now.AddHours(2)));
            Assert.Equal(1, await Multiplier(f, DropDrawEntry.MonsterEquipment));
            Assert.Equal(1, await Multiplier(f, DropDrawEntry.World));
        });

    [Fact]
    public async Task Changing_a_running_activity_reapplies_and_a_removed_entry_returns_to_one() =>
        await TestGameplay.RunAsync(async () =>
        {
            await using var f = new LearningFixture();
            var now = DateTimeOffset.UtcNow;
            await Configure(f, 0, now, now.AddHours(-1), now.AddHours(1), Configured);
            await Change(f, 1, "enable", now);
            Assert.Equal(20, await Multiplier(f, DropDrawEntry.MonsterEquipment));

            await Configure(f, 2, now, now.AddHours(-1), now.AddHours(1), new Dictionary<string, int> { ["world"] = 100 });
            Assert.Equal(1, await Multiplier(f, DropDrawEntry.MonsterEquipment));
            Assert.Equal(100, await Multiplier(f, DropDrawEntry.World));

            await Change(f, 3, "disable", now);
            Assert.Equal(1, await Multiplier(f, DropDrawEntry.World));
        });

    [Fact]
    public async Task First_reconciliation_restores_one_when_the_activity_is_not_running() =>
        await TestGameplay.RunAsync(async () =>
        {
            await using var f = new LearningFixture();
            await using (var tx = f.Scope.Begin())
            {
                await f.Services.GetRequiredService<IDropDrawMultiplierControlDomainService>().SetAsync(DropDrawEntry.World, 20, default);
                await tx.CommitAsync(default);
            }
            Assert.True(await Reconcile(f, DateTimeOffset.UtcNow));
            Assert.Equal(1, await Multiplier(f, DropDrawEntry.World));
        });

    [Theory]
    [InlineData("world", 0)]
    [InlineData("world", 101)]
    [InlineData("no.such.entry", 2)]
    public async Task Invalid_multipliers_are_refused_and_leave_the_configuration_unchanged(string key, int multiplier) =>
        await TestGameplay.RunAsync(async () =>
        {
            await using var f = new LearningFixture();
            var now = DateTimeOffset.UtcNow;
            await Assert.ThrowsAsync<ArgumentException>(async () =>
                await Configure(f, 0, now, now.AddHours(-1), now.AddHours(1), new Dictionary<string, int> { [key] = multiplier }));
            var entry = await Describe(f, now);
            Assert.Equal(0, entry.Revision);
            Assert.Null(entry.DrawMultipliers);
        });

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    public async Task Multiplier_bounds_are_accepted(int multiplier) =>
        await TestGameplay.RunAsync(async () =>
        {
            await using var f = new LearningFixture();
            var now = DateTimeOffset.UtcNow;
            await Configure(f, 0, now, now.AddHours(-1), now.AddHours(1), new Dictionary<string, int> { ["world"] = multiplier });
            Assert.Equal(multiplier, (await Describe(f, now)).DrawMultipliers!["world"]);
        });

    [Fact]
    public async Task A_configure_needs_multipliers_and_other_activities_refuse_them() =>
        await TestGameplay.RunAsync(async () =>
        {
            await using var f = new LearningFixture();
            var now = DateTimeOffset.UtcNow;
            await Assert.ThrowsAsync<ArgumentException>(async () => await Configure(f, 0, now, now.AddHours(-1), now.AddHours(1), null));
            await using var tx = f.Scope.Begin();
            await Assert.ThrowsAsync<ArgumentException>(async () => await f.Services.GetRequiredService<IEventManagementDomainService>()
                .ChangeAsync(new(UnlimitedFatigueEventDomainService.ActivityId, 0, "configure", "t", "s", "", "", "", now, now.AddHours(1), Configured), "test", now, default));
        });

    [Fact]
    public async Task Failure_rolls_back_the_configuration_and_the_multipliers() =>
        await TestGameplay.RunAsync(async () =>
        {
            var failing = new Failing();
            await using var f = new LearningFixture(configure: s =>
            {
                s.AddSingleton(failing);
                s.AddRuntimeEventSubscriber<ManagedEventActivationChanged, FailingObserver>();
            });
            var now = DateTimeOffset.UtcNow;
            await Configure(f, 0, now, now.AddHours(-1), now.AddHours(1), Configured);
            failing.Armed = true;
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await Change(f, 1, "enable", now));
            Assert.False((await Describe(f, now)).Enabled);
            Assert.Equal(1, await Multiplier(f, DropDrawEntry.MonsterEquipment));

            failing.Armed = false;
            await Change(f, 1, "enable", now);
            Assert.Equal(20, await Multiplier(f, DropDrawEntry.MonsterEquipment));
        });

    [Fact]
    public async Task Player_information_lists_the_activity_only_while_it_runs() =>
        await TestGameplay.RunAsync(async () =>
        {
            await using var f = new LearningFixture();
            var now = DateTimeOffset.UtcNow;
            await Configure(f, 0, now, now.AddHours(1), now.AddHours(2), Configured);
            await Change(f, 1, "enable", now);
            var owner = Assert.Single(f.Services.GetServices<IManagedEventDomainService>(), e => e.EventId == ActivityId);
            Assert.Empty(await owner.GetEventInfoEntriesAsync(new(1), now, default));
            var listed = Assert.Single(await owner.GetEventInfoEntriesAsync(new(1), now.AddMinutes(90), default));
            Assert.Equal(ActivityId, listed.EventId);
            Assert.True(listed.HasDetail);
            Assert.Equal("爆率最高 20 倍。测试", listed.Description);

            await Configure(f, 2, now, now.AddHours(-1), now.AddHours(1), DropDrawEntries.All.ToDictionary(DropDrawEntries.KeyOf, _ => 3));
            Assert.Equal("爆率 3 倍。测试", Assert.Single(await owner.GetEventInfoEntriesAsync(new(1), now, default)).Description);

            await Configure(f, 3, now, now.AddHours(-1), now.AddHours(1), new Dictionary<string, int>());
            Assert.Equal("测试", Assert.Single(await owner.GetEventInfoEntriesAsync(new(1), now, default)).Description);
        });

    private static async Task Configure(LearningFixture f, long revision, DateTimeOffset now, DateTimeOffset start, DateTimeOffset end,
        IReadOnlyDictionary<string, int>? multipliers)
    {
        await using var tx = f.Scope.Begin();
        await f.Services.GetRequiredService<IEventManagementDomainService>().ChangeAsync(new(ActivityId, revision, "configure",
            "爆率", "爆率", "测试", "", "", start, end, multipliers), "test", now, default);
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

    private static async Task<int> Multiplier(LearningFixture f, DropDrawEntry entry) =>
        (await f.Services.GetRequiredService<IDropDrawMultiplierDomainService>().CurrentAsync(default)).For(entry);

    private static async Task<ManagedEventEntry> Describe(LearningFixture f, DateTimeOffset now) =>
        await Assert.Single(f.Services.GetServices<IManagedEventDomainService>(), e => e.EventId == ActivityId).DescribeAsync(now, default);

    private sealed class Failing { public bool Armed { get; set; } }

    private sealed class FailingObserver(Failing failing) : IDomainEventSubscriber<ManagedEventActivationChanged>
    {
        public ValueTask HandleAsync(DomainEventEnvelope<ManagedEventActivationChanged> message, CancellationToken ct) =>
            failing.Armed ? throw new InvalidOperationException("subscriber failed") : ValueTask.CompletedTask;
    }
}
