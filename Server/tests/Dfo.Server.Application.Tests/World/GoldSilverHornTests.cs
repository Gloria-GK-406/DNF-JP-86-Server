using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.UsageQuotas;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;
using Dfo.Server.Abstractions.Events;
using Dfo.Server.Application.Tests.Characters;
using Dfo.Server.Infrastructure.Content;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.World;

public sealed class GoldSilverHornTests
{
    [Fact]
    public async Task Activity_projects_4123_without_requiring_a_companion_event()
    {
        await using var f = new LearningFixture();
        await f.SeedAsync();
        var snapshot = await f.Services.GetRequiredService<IEventPlayerProjectionDomainService>().ReadNowAsync(f.Session, default);
        Assert.True(Assert.Single(snapshot.ScheduledDungeons, x => x.DungeonId == 4123).Open);
    }

    [Fact]
    public async Task Quota_is_transactional_persistent_character_scoped_and_uses_the_business_reset()
    {
        var clock = new Clock { Now = new(2026, 10, 19, 5, 59, 0, TimeSpan.FromHours(8)) };
        await using var f = new LearningFixture(configure: s => Configure(s, clock));
        await f.SeedAsync();
        var events = f.Services.GetRequiredService<IEventService>();
        var domain = f.Services.GetRequiredService<IGoldSilverHornDomainService>();
        var period = (await domain.ReadQuotaAsync(new(71), default)).Period;
        Assert.Equal(new DateOnly(2026, 10, 18), period);
        await using (var tx = f.Scope.Begin())
            await events.DirectAsync(new GoldSilverHornEntryRecorded(new(71), period, null, 1));
        Assert.Equal(0, (await domain.ReadQuotaAsync(new(71), default)).Used);
        await using (var tx = f.Scope.Begin())
        {
            await events.DirectAsync(new GoldSilverHornEntryRecorded(new(71), period, null, 2));
            await tx.CommitAsync(default);
        }
        await using (var tx = f.Scope.Begin())
            await Assert.ThrowsAsync<InvalidOperationException>(() => events.DirectAsync(
                new GoldSilverHornEntryRecorded(new(71), period, null, 3)).AsTask());
        Assert.Equal(2, (await domain.ReadQuotaAsync(new(71), default)).Used);
        Assert.Equal(0, (await domain.ReadQuotaAsync(new(72), default)).Used);
        await using var reload = new LearningFixture(existing: f.Path, configure: s => Configure(s, clock));
        Assert.Equal(2, (await reload.Services.GetRequiredService<IGoldSilverHornDomainService>().ReadQuotaAsync(new(71), default)).Used);
        clock.Now = clock.Now.AddMinutes(1);
        var refreshed = await domain.ReadQuotaAsync(new(71), default);
        Assert.Equal(new DateOnly(2026, 10, 19), refreshed.Period);
        Assert.Equal(0, refreshed.Used); Assert.Equal(3, refreshed.Remaining);
        Assert.Equal(1, f.Scalar("SELECT COUNT(*) FROM character_daily_counters"));
    }

    [SkippableFact]
    public async Task Three_successful_entries_charge_once_and_refusals_and_rollback_do_not_spend_quota_or_fatigue()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        await using var f = await Fixture();
        var domain = f.Services.GetRequiredService<IGoldSilverHornDomainService>();
        var runs = f.Services.GetRequiredService<IDungeonRunDomainService>();
        var fatigue = f.Services.GetRequiredService<ICharacterFatigueDomainService>();
        var character = new CharacterId(71);
        await using (var tx = f.Scope.Begin())
        {
            await fatigue.SpendAsync(character, (await fatigue.BalanceOfAsync(character, default)).Remaining, default);
            await tx.CommitAsync(default);
        }
        var usedFatigue = (await fatigue.BalanceOfAsync(character, default)).Used;
        await using (var tx = f.Scope.Begin())
            Assert.NotNull((await domain.EnterAsync(Entry(), default)).Entry);
        Assert.Null(runs.Find(character));
        Assert.Equal(0, (await domain.ReadQuotaAsync(character, default)).Used);
        await using (var tx = f.Scope.Begin())
        {
            Assert.Equal(DungeonEntryFailure.MissingPrerequisite, (await domain.EnterAsync(Entry() with { PracticeMode = 1 }, default)).Failure);
            Assert.Equal(DungeonEntryFailure.MissingPrerequisite, (await domain.EnterAsync(Entry() with { RequestMode = 1 }, default)).Failure);
            Assert.Equal(DungeonEntryFailure.MissingPrerequisite, (await domain.EnterAsync(Entry() with {
                Participants = [.. Entry().Participants, new(new(2), new(72), 1, false)] }, default)).Failure);
            Assert.Equal(DungeonEntryFailure.DifficultyNotUnlocked, (await domain.EnterAsync(Entry() with { Facts = DungeonEntryFacts.None }, default)).Failure);
            await f.Services.GetRequiredService<ICharacterDomainService>().Get(character).SetLevelAsync(49, default);
            Assert.Equal(DungeonEntryFailure.LevelTooLow, (await domain.EnterAsync(Entry(), default)).Failure);
            await f.Services.GetRequiredService<ICharacterDomainService>().Get(character).SetLevelAsync(50, default);
            await tx.CommitAsync(default);
        }
        for (var count = 1; count <= 3; count++)
        {
            await using (var tx = f.Scope.Begin())
            {
                Assert.NotNull((await domain.EnterAsync(Entry(), default)).Entry);
                await tx.CommitAsync(default);
            }
            await using (var tx = f.Scope.Begin())
                Assert.Equal(DungeonEntryFailure.AlreadyInDungeon, (await domain.EnterAsync(Entry(), default)).Failure);
            Assert.Equal(count, (await domain.ReadQuotaAsync(character, default)).Used);
            var run = runs.Find(character)!;
            Assert.False(run.Layout.ConsumesFatigue);
            Assert.False(run.Layout.AppliesFailureWeakness);
            Assert.Equal(0, run.Layout.ExperienceWeight);
            await using (var tx = f.Scope.Begin())
            {
                Assert.True(await runs.EndAsync(character, run.RunId));
                await tx.CommitAsync(default);
            }
        }
        await using (var tx = f.Scope.Begin())
            Assert.Equal(DungeonEntryFailure.DailyEntryLimitReached, (await domain.EnterAsync(Entry(), default)).Failure);
        Assert.Equal(usedFatigue, (await fatigue.BalanceOfAsync(character, default)).Used);
        Assert.Equal(100, f.Services.GetRequiredService<IWeaknessDomainService>().For(character).Value);
        await using (var tx = f.Scope.Begin())
        {
            await f.Services.GetRequiredService<IEventManagementDomainService>().ChangeAsync(new(2332, 0, "disable"), "test", DateTimeOffset.UtcNow, default);
            Assert.Equal(DungeonEntryFailure.OutsideOpeningHours, (await domain.EnterAsync(Entry(), default)).Failure);
            await tx.CommitAsync(default);
        }
        var closed = await f.Services.GetRequiredService<IEventPlayerProjectionDomainService>().ReadNowAsync(f.Session, default);
        Assert.False(Assert.Single(closed.ScheduledDungeons, x => x.DungeonId == 4123).Open);
        Assert.True(Assert.Single(closed.ScheduledDungeons, x => x.DungeonId == 3557).Open);
    }

    [SkippableTheory]
    [InlineData(0, 3)]
    [InlineData(1, 2)]
    [InlineData(3, 0)]
    public async Task Full_character_refresh_overrides_legacy_4123_defaults_with_authoritative_remaining(int used, int remaining)
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        await using var f = await Fixture();
        f.Sql("INSERT INTO character_condition(character_id,pc_room_state) VALUES(71,0); INSERT OR IGNORE INTO character_stats(character_id) VALUES(71); INSERT INTO character_pvp(character_id) VALUES(71); INSERT INTO character_settings(character_id) VALUES(71); INSERT INTO character_expert_job(character_id) VALUES(71)");
        f.Sql("INSERT INTO character_dimensions(character_id,sort_order,dim_key,val1,val2) VALUES(71,0,4123,7,3),(71,1,4123,9,99)");
        var period = (await f.Services.GetRequiredService<IGoldSilverHornDomainService>().ReadQuotaAsync(new(71), default)).Period;
        await using (var tx = f.Scope.Begin())
        {
            await f.Services.GetRequiredService<IEventService>().DirectAsync(new GoldSilverHornEntryRecorded(new(71), period, null, used));
            await tx.CommitAsync(default);
        }
        var state = await f.Services.GetRequiredService<ICharacterEntryStateDomainService>().LoadAsync(new(1), new(71), default);
        var row = Assert.Single(state.Public.Extras.Dimensions, x => x.Key == 4123);
        Assert.Equal(remaining, row.Value2); Assert.Equal(7, row.Value1);
        Assert.Equal(2, f.Scalar("SELECT COUNT(*) FROM character_dimensions WHERE dim_key=4123"));
        Assert.Equal(102, f.Scalar("SELECT SUM(val2) FROM character_dimensions WHERE dim_key=4123"));
    }

    [SkippableFact]
    public async Task Factory_failure_after_quota_charge_rolls_back_the_counter_and_run()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        await using var f = new LearningFixture(useArchive: true, configure: s =>
        {
            s.AddSingleton(new PvfContentOptions { ArchivePath = ContentArchive.Path });
            s.AddSingleton<IDungeonCellFactory>(new FailingFactory());
        });
        await f.SeedAsync();
        var domain = f.Services.GetRequiredService<IGoldSilverHornDomainService>();
        await using (var tx = f.Scope.Begin())
            await Assert.ThrowsAsync<InvalidDataException>(() => domain.EnterAsync(Entry(), default).AsTask());
        Assert.Equal(0, (await domain.ReadQuotaAsync(new(71), default)).Used);
        Assert.Null(f.Services.GetRequiredService<IDungeonRunDomainService>().Find(new CharacterId(71)));
    }
    private sealed class FailingFactory : IDungeonCellFactory
    {
        public ValueTask<DungeonCellSeed> CreateAsync(DungeonCellCreationContext context, CancellationToken ct) =>
            throw new InvalidDataException("Injected room construction failure");
    }

    [SkippableFact]
    public async Task All_six_native_maps_materialize_and_all_three_boss_candidates_are_reachable()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        await using var f = await Fixture();
        var rules = f.Services.GetRequiredService<IGoldSilverHornRuleService>();
        var definition = await rules.DefinitionAsync(default);
        Assert.Equal(50, definition.MinimumRequiredLevel); Assert.Equal(3, rules.MaximumEntries);
        var structures = f.Services.GetRequiredService<IDungeonStructureRuleService>();
        var layout = (await structures.ResolveAsync(4123, Entry().Facts, 17, default))!;
        Assert.Equal(4, layout.Cells.Count);
        var actors = new Dictionary<int, int[]>();
        foreach (var (cell, room) in layout.Cells)
        foreach (var map in room.MapCandidates.Concat(room.BossMapCandidates).Distinct())
        {
            var seed = await f.Services.GetRequiredService<IDungeonCellFactory>().CreateAsync(
                new(DungeonRunId.New(), layout, cell, map, DungeonDifficulty.Tier2, DateTimeOffset.UtcNow, Entry().Participants), default);
            Assert.True(seed.IsComplete);
            actors[map] = seed.Actors.Select(x => x.Actor.Code).ToArray();
        }
        Assert.Equal(6, actors.Count);
        Assert.Contains(110255, actors[25606]); Assert.DoesNotContain(110256, actors[25606]);
        Assert.Contains(110256, actors[25607]); Assert.DoesNotContain(110255, actors[25607]);
        Assert.Contains(110255, actors[25608]); Assert.Contains(110256, actors[25608]);
        var choices = new HashSet<int>();
        for (var seed = 0; seed < 128; seed++)
        {
            var candidate = (await structures.ResolveAsync(4123, Entry().Facts, seed, default))!;
            var selection = await structures.SelectMapAsync(candidate, new MazeCell(1, 1), candidate.BossCell, default);
            Assert.NotNull(selection.MapId); choices.Add(selection.MapId.Value);
        }
        Assert.Equal(new[] { 25606, 25607, 25608 }, choices.Order().ToArray());
    }

    private static DungeonEntryContext Entry() => new(4123,
        DungeonEntryFacts.None with { Difficulty = DungeonDifficulty.Tier2 }, 0, 0, [new(new(1), new(71), 0, true)]);
    private static async Task<LearningFixture> Fixture()
    {
        var f = new LearningFixture(useArchive: true, configure: s =>
            s.AddSingleton(new PvfContentOptions { ArchivePath = ContentArchive.Path }));
        await f.SeedAsync();
        f.Services.GetRequiredService<IIndependentDropRuleService>().Initialize();
        f.Services.GetRequiredService<IExtraDropRuleService>().Initialize();
        f.Services.GetRequiredService<IMonsterDeclaredDropRuleService>().Initialize();
        f.Services.GetRequiredService<IWorldDropRuleService>().Initialize();
        return f;
    }
    private static void Configure(IServiceCollection services, Clock clock)
    {
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton<IGoldSilverHornContentSource>(new Content());
        services.AddSingleton<IDailyResetRuleService>(new Daily());
    }
    private sealed class Content : IGoldSilverHornContentSource { public GoldSilverHornContent Load() => new(4123, 3); }
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; }
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class Daily : IDailyResetRuleService
    {
        public DateTimeOffset PeriodStart(DateTimeOffset now)
        {
            var local = now.ToOffset(TimeSpan.FromHours(8));
            var start = new DateTimeOffset(local.Date.AddHours(6), local.Offset);
            return local < start ? start.AddDays(-1) : start;
        }
        public bool IsCurrentPeriod(DateTimeOffset recordedAt, DateTimeOffset now) => recordedAt >= PeriodStart(now);
    }

    [SkippableTheory]
    [InlineData(490001561, 0)]
    [InlineData(490001562, 0)]
    [InlineData(490001563, 7000000)]
    [InlineData(490001564, 7000000)]
    [InlineData(490001565, 0)]
    public async Task Shop_material_is_not_charged_again_when_opening(int itemId, long gold)
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        await using var f = new LearningFixture(useArchive: true, configure: s =>
            s.AddSingleton(new PvfContentOptions { ArchivePath = ContentArchive.Path }));
        var item = await f.Services.GetRequiredService<IPackageOpeningDefinitionSource>().GetAsync(itemId, default);
        Assert.NotNull(item);
        Assert.Equal(0, item.MaterialCount);
        Assert.Equal(gold, item.GoldCost);
    }
}
