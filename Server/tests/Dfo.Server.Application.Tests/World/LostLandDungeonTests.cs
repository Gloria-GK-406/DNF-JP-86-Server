using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.UsageQuotas;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Tests.Characters;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.World;

public sealed class LostLandDungeonTests
{
    [SkippableFact]
    public async Task Acceptance_entries_stay_one_without_consuming_saved_quota_and_disabling_restores_the_real_limit()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        await using var fixture = await Fixture(unlimitedEntries: true);
        var domain = fixture.Services.GetRequiredService<ILostLandDungeonDomainService>();
        var fatigue = fixture.Services.GetRequiredService<ICharacterFatigueDomainService>();
        var quotas = fixture.Services.GetRequiredService<IUsageQuotaDomainService>();
        var period = (await domain.ReadQuotaAsync(new(71), default)).Period;
        await TestGameplay.RunAsync(fixture.Services, async () =>
        {
            await using var transaction = fixture.Scope.Begin();
            await fatigue.SpendAsync(new(71), 100, default);
            await fixture.Services.GetRequiredService<Dfo.Server.Abstractions.DomainEvents.IEventService>()
                .DirectAsync(new LostLandEntryRecorded(new(71), period, null, 2));
            await transaction.CommitAsync(default);
        });
        var originalFatigue = await fatigue.BalanceOfAsync(new(71), default);
        var runs = fixture.Services.DungeonRuns();
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var quota = await domain.ReadQuotaAsync(new(71), default);
            Assert.Equal(1, quota.Maximum);
            Assert.Equal(1, quota.Remaining);
            var projected = await fixture.Services.GetRequiredService<IUsageQuotaSnapshotDomainService>()
                .ReadAsync(new(1), new(71), UsageQuotaFamily.LostLand, default);
            Assert.Equal(quota, projected.LostLand);
            Assert.NotNull((await DungeonEntryRouteDriver.EnterAsync(fixture.Services, Entry())).Entry);
            Assert.Equal(1, (await domain.ReadQuotaAsync(new(71), default)).Remaining);
            Assert.Equal(2, (await quotas.ReadAsync(new(71), UsageQuotaKey.LostLand(period), default))!.UsedCount);
            TestGameplay.Run(fixture.Services, () => { if (runs.Find(new Dfo.Server.Data.Characters.CharacterId(71)) is { } run) runs.End(new(71), run.RunId); });
        }
        Assert.Equal(originalFatigue, await fatigue.BalanceOfAsync(new(71), default));
        await using var normal = new LearningFixture(existing: fixture.Path, useArchive: true, configure: services =>
            services.AddSingleton(new Dfo.Server.Infrastructure.Content.PvfContentOptions { ArchivePath = ContentArchive.Path }));
        var restored = await normal.Services.GetRequiredService<ILostLandDungeonDomainService>().ReadQuotaAsync(new(71), default);
        Assert.Equal(2, restored.Maximum);
        Assert.Equal(2, restored.Used);
        Assert.Equal(0, restored.Remaining);
        Assert.Equal(DungeonEntryFailure.DailyEntryLimitReached,
            (await DungeonEntryRouteDriver.EnterAsync(normal.Services, Entry())).Failure);
    }

    [SkippableTheory]
    [InlineData(null, 600)]
    [InlineData(60, 60)]
    public async Task Probe_limit_is_entry_frozen_and_default_keeps_original_ten_minutes(int? probeSeconds, int expected)
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        await using var fixture = await Fixture(probeSeconds: probeSeconds);
        var entry = await DungeonEntryRouteDriver.EnterAsync(fixture.Services, Entry());
        Assert.NotNull(entry.Entry);
        Assert.Equal(expected, Assert.IsType<DungeonRunMode.LostLand>(entry.Entry.Layout.Mode).Plan.FirstLimitSeconds);
        Assert.Equal(1200, Assert.IsType<DungeonRunMode.LostLand>(entry.Entry.Layout.Mode).Plan.SecondLimitSeconds);
    }

    [SkippableFact]
    public async Task Quota_and_consumed_fatigue_survive_service_recreation_and_reset_together()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        await using var fixture = await Fixture();
        await TestGameplay.RunAsync(fixture.Services, async () =>
        {
            await using var transaction = fixture.Scope.Begin();
            await fixture.Services.GetRequiredService<ICharacterFatigueDomainService>().SpendAsync(new(71), 100, default);
            await transaction.CommitAsync(default);
        });
        Assert.NotNull((await DungeonEntryRouteDriver.EnterAsync(fixture.Services, Entry())).Entry);
        var expected = await fixture.Services.GetRequiredService<ILostLandDungeonDomainService>().ReadQuotaAsync(new(71), default);
        Assert.Equal(1, expected.Remaining);
        await using var recreated = new LearningFixture(existing: fixture.Path, useArchive: true, configure: services =>
            services.AddSingleton(new Dfo.Server.Infrastructure.Content.PvfContentOptions { ArchivePath = ContentArchive.Path }));
        var domain = recreated.Services.GetRequiredService<ILostLandDungeonDomainService>();
        Assert.Equal(expected, await domain.ReadQuotaAsync(new(71), default));
        var nextDay = await domain.ReadQuotaAtAsync(new(71), DateTimeOffset.UtcNow.AddDays(1), default);
        Assert.NotEqual(expected.Period, nextDay.Period);
        Assert.Equal(0, nextDay.ConsumedFatigue);
        Assert.Equal(0, nextDay.Used);
        Assert.Equal(1, nextDay.Remaining);
    }

    [SkippableFact]
    public async Task Native_directed_selection_is_supported_but_unbound_marker_and_wildcard_teleports_are_refused()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        await using var fixture = await Fixture();
        var entry = await DungeonEntryRouteDriver.EnterAsync(fixture.Services,
            Entry() with { Facts = DungeonEntryFacts.None with { Intent = DungeonEntryIntent.Directed(4000) } });
        Assert.NotNull(entry.Entry);
        var layout = entry.Entry.Layout;
        Assert.False(layout.CanTraverse(layout.StartCell, layout.BossCell));
        Assert.False(layout.CanTraverse(layout.StartCell, new(5, 5)));
        var structures = fixture.Services.GetRequiredService<IDungeonStructureRuleService>();
        Assert.Null((await structures.SelectMapAsync(layout, null, layout.BossCell, default)).MapId);
        Assert.NotNull((await structures.SelectMapAsync(layout, new(8, 6), new(1, 6), default)).MapId);
        var runs = fixture.Services.DungeonRuns();
        TestGameplay.Run(fixture.Services, () => Assert.Null(runs.TryOpenClear(new(71),
            entry.Entry.Change!.Run.RunId, entry.Entry.Change.Run.RoomVersion)));
    }

    [SkippableFact]
    public async Task Hundred_consumed_fatigue_grants_one_entry_and_refunds_do_not_revoke_it()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        await using var fixture = await Fixture();
        var domain = fixture.Services.GetRequiredService<ILostLandDungeonDomainService>();
        var fatigue = fixture.Services.GetRequiredService<ICharacterFatigueDomainService>();
        Assert.Equal(1, (await domain.ReadQuotaAsync(new(71), default)).Remaining);
        await ChangeFatigue(99, false);
        Assert.Equal(1, (await domain.ReadQuotaAsync(new(71), default)).Remaining);
        await ChangeFatigue(1, false);
        Assert.Equal(2, (await domain.ReadQuotaAsync(new(71), default)).Remaining);
        await ChangeFatigue(100, true);
        Assert.Equal(2, (await domain.ReadQuotaAsync(new(71), default)).Remaining);
        var runs = fixture.Services.DungeonRuns();
        for (var i = 0; i < 2; i++)
        {
            Assert.NotNull((await DungeonEntryRouteDriver.EnterAsync(fixture.Services, Entry())).Entry);
            TestGameplay.Run(fixture.Services, () => { if (runs.Find(new Dfo.Server.Data.Characters.CharacterId(71)) is { } run) runs.End(new(71), run.RunId); });
        }
        Assert.Equal(DungeonEntryFailure.DailyEntryLimitReached,
            (await DungeonEntryRouteDriver.EnterAsync(fixture.Services, Entry())).Failure);
        Assert.Equal(0, (await domain.ReadQuotaAsync(new(71), default)).Remaining);
        Assert.Equal(1, (await domain.ReadQuotaAsync(new(72), default)).Remaining);
        Assert.Equal(100, (await fatigue.BalanceOfAsync(new(71), default)).ConsumedToday);

        async Task ChangeFatigue(ushort points, bool refund) => await TestGameplay.RunAsync(fixture.Services, async () =>
        {
            await using var tx = fixture.Scope.Begin();
            if (refund) await fatigue.RefundAsync(new(71), points, default);
            else await fatigue.SpendAsync(new(71), points, default);
            await tx.CommitAsync(default);
        });
    }

    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Refused_modes_and_levels_leave_quota_untouched(bool unlimitedEntries)
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        await using var fixture = await Fixture(24, unlimitedEntries: unlimitedEntries);
        Assert.Equal(DungeonEntryFailure.LevelTooLow,
            (await DungeonEntryRouteDriver.EnterAsync(fixture.Services, Entry())).Failure);
        foreach (var entry in new[]
        {
            Entry() with { PracticeMode = 1 }, Entry() with { RequestMode = 1 },
            Entry() with { Facts = DungeonEntryFacts.None with { Difficulty = (Dfo.Server.Data.World.DungeonDifficulty)1 } },
            Entry() with { Participants = [new(new(1), new(71), 0, true), new(new(2), new(72), 1, false)] },
        })
            Assert.Equal(DungeonEntryFailure.MissingPrerequisite,
                (await DungeonEntryRouteDriver.EnterAsync(fixture.Services, entry)).Failure);
        Assert.Equal(1, (await fixture.Services.GetRequiredService<ILostLandDungeonDomainService>().ReadQuotaAsync(new(71), default)).Remaining);
    }

    [SkippableFact]
    public async Task Entry_rollback_keeps_quota_and_run_in_the_same_transaction()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        await using var fixture = await Fixture();
        var domain = fixture.Services.GetRequiredService<ILostLandDungeonDomainService>();
        await TestGameplay.RunAsync(fixture.Services, async () =>
        {
            await using var transaction = fixture.Scope.Begin();
            Assert.NotNull((await domain.EnterAsync(Entry(), default)).Entry);
            // Disposing without commit simulates a later failure in the entry work unit.
        });
        Assert.Equal(1, (await domain.ReadQuotaAsync(new(71), default)).Remaining);
        TestGameplay.Run(fixture.Services, () => Assert.Null(fixture.Services.DungeonRuns()
            .Find(new Dfo.Server.Data.Characters.CharacterId(71))));
    }

    [SkippableFact]
    public async Task Native_selection_enters_exploration_without_fatigue_or_an_ordinary_boss_clear()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        await using var fixture = await Fixture();
        var fatigue = fixture.Services.GetRequiredService<ICharacterFatigueDomainService>();
        var before = await fatigue.BalanceOfAsync(new(71), default);
        var result = await DungeonEntryRouteDriver.EnterAsync(fixture.Services, Entry());
        Assert.True(result.Entry is not null, $"{result.Failure}: {result.Detail}");
        Assert.Equal(new MazeCell(9, 6), result.Entry.Layout.StartCell);
        Assert.Equal(11200, result.Entry.Layout.StartRoomMapId);
        Assert.False(result.Entry.Layout.ConsumesFatigue);
        Assert.Equal(before, await fatigue.BalanceOfAsync(new(71), default));
        Assert.DoesNotContain(result.Entry.Layout.Cells.Keys,
            cell => result.Entry.Layout.KindOf(cell) == DungeonRoomKind.Boss);
    }

    private static DungeonEntryContext Entry() => new(4000, DungeonEntryFacts.None, 0, 0,
        [new(new(1), new(71), 0, true)]);

    private static async Task<LearningFixture> Fixture(byte level = 70, int? probeSeconds = null, bool unlimitedEntries = false)
    {
        var fixture = new LearningFixture(useArchive: true, configure: services =>
        {
            services.AddSingleton(new LostLandRuntimePolicy(probeSeconds, unlimitedEntries));
            services.AddSingleton(new Dfo.Server.Infrastructure.Content.PvfContentOptions { ArchivePath = ContentArchive.Path });
            new Dfo.Server.Protocol.ProtocolModule().ConfigureServices(services,
                new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        });
        await fixture.SeedAsync();
        await TestGameplay.RunAsync(fixture.Services, async () =>
        {
            await using var transaction = fixture.Scope.Begin();
            await fixture.Services.GetRequiredService<ICharacterDomainService>().Get(new(71)).SetLevelAsync(level, default);
            await transaction.CommitAsync(default);
        });
        fixture.Services.GetRequiredService<IIndependentDropRuleService>().Initialize();
        fixture.Services.GetRequiredService<IExtraDropRuleService>().Initialize();
        fixture.Services.GetRequiredService<IMonsterDeclaredDropRuleService>().Initialize();
        fixture.Services.GetRequiredService<IWorldDropRuleService>().Initialize();
        return fixture;
    }
}
