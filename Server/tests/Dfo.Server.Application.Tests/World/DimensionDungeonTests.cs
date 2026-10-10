using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.UsageQuotas;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Tests.Characters;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;
using Dfo.Server.Infrastructure.Content;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.World;

public sealed class DimensionDungeonTests
{
    [SkippableFact]
    public async Task All_six_real_cells_materialize_with_the_boss_and_personal_chronicle_loot()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        await using var fixture = new LearningFixture(useArchive: true, configure: services =>
            services.AddSingleton(new PvfContentOptions { ArchivePath = ContentArchive.Path }));
        await fixture.SeedAsync();
        fixture.Sql("UPDATE characters SET job=0,grow_type=1 WHERE character_id=71");
        fixture.Services.GetRequiredService<IIndependentDropRuleService>().Initialize();
        fixture.Services.GetRequiredService<IExtraDropRuleService>().Initialize();
        fixture.Services.GetRequiredService<IMonsterDeclaredDropRuleService>().Initialize();
        fixture.Services.GetRequiredService<IWorldDropRuleService>().Initialize();
        var layout = Assert.IsType<DungeonLayout>(await fixture.Services.GetRequiredService<IDungeonStructureRuleService>()
            .ResolveAsync(7100, DungeonEntryFacts.None, 42, default));
        await using (var tx = fixture.Scope.Begin())
        {
            var entry = await fixture.Services.GetRequiredService<IDimensionDungeonDomainService>()
                .EnterAsync(new(7100, DungeonEntryFacts.None, 0, 0, [new(new(1), new(71), 0, true)]), default);
            Assert.True(entry.Entry is not null, $"Real fusion entry refused: {entry.Failure}; {entry.Detail}");
            await tx.CommitAsync(default);
        }
        Assert.Equal(4, (await fixture.Services.GetRequiredService<IDimensionDungeonDomainService>()
            .ReadQuotaAsync(new(71), default)).Remaining);
        var factory = fixture.Services.GetRequiredService<IDungeonCellFactory>();
        var cells = new List<DungeonCellSeed>();
        for (byte x = 0; x < 6; x++)
        {
            var map = x == 0 ? layout.StartRoomMapId : (await fixture.Services.GetRequiredService<IDungeonStructureRuleService>()
                .SelectMapAsync(layout, new((byte)(x - 1), 0), new(x, 0), default)).MapId!.Value;
            cells.Add(await factory.CreateAsync(new(DungeonRunId.New(), layout, new(x, 0), map, 0,
                DateTimeOffset.UnixEpoch, [new(new(1), new(71), 0, true)]), default));
        }
        Assert.All(cells, cell => Assert.NotEmpty(cell.Actors));
        var boss = Assert.Single(cells[^1].Actors, actor => actor.Actor.Code == 61809);
        Assert.True(boss.Actor.IsBoss);
        Assert.Equal(2, boss.PendingLoot.Count(item => item.Kind == DropItemKind.Equipment && item.OwnerCharacterId == new Dfo.Server.Data.Characters.CharacterId(71)));
        Assert.Equal(2, Assert.Single(boss.PendingLoot, item => item.ItemId == 3311 && item.OwnerCharacterId == new Dfo.Server.Data.Characters.CharacterId(71)).Count);
    }

    [SkippableFact]
    public async Task Fusion_real_content_builds_six_reachable_rooms_and_the_actual_boss_resource()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var content = ContentArchive.CreateContentProvider();
        var rules = new DimensionDungeonRuleService(content.GetRequiredService<IDimensionDungeonContentSource>());
        Assert.True(rules.IsDimension(7100));
        Assert.False(rules.IsDimension(144));
        Assert.Equal(5, rules.Snapshot(new(2026, 10, 10), 0).Remaining);
        var structures = new DungeonStructureRuleService(content.GetRequiredService<IDungeonDefinitionSource>(),
            content.GetRequiredService<IDungeonMapDefinitionSource>());
        var layout = await structures.ResolveAsync(7100, DungeonEntryFacts.None, 42, default);
        Assert.NotNull(layout);
        Assert.Equal(6, layout.Cells.Count);
        Assert.Equal(new MazeCell(0, 0), layout.StartCell);
        Assert.Equal(new MazeCell(5, 0), layout.BossCell);
        for (byte x = 1; x <= 5; x++)
        {
            var map = await structures.SelectMapAsync(layout, new((byte)(x - 1), 0), new(x, 0), default);
            Assert.NotNull(map.MapId);
            if (x == 5) Assert.Equal(53075, map.MapId);
        }
        var drops = rules.DrawMonster(7100, 61809, 0, 1, 42);
        Assert.Equal(2, drops.Count(x => x.Kind == DropItemKind.Equipment));
        var careerPool = content.GetRequiredService<IDimensionDungeonContentSource>().Load().Pools[(0, 1)];
        Assert.All(drops.Where(x => x.Kind == DropItemKind.Equipment), item =>
            Assert.Contains(item, careerPool.Normal.Concat(careerPool.Set)));
        Assert.Equal(2, Assert.Single(drops, x => x.ItemId == 3311).Count);
        Assert.Equal(drops, rules.DrawMonster(7100, 61809, 0, 17, 42));
        Assert.Empty(rules.DrawMonster(144, 61809, 0, 1, 42));
    }

    [Fact]
    public async Task Entry_spends_only_after_preparation_persists_and_rejects_exhaustion()
    {
        var clock = new Clock();
        await using var fixture = await Fixture(clock);
        var visits = new Visits();
        var service = Service(fixture, clock, visits);
        var context = new DungeonEntryContext(7100, DungeonEntryFacts.None, 0, 0, [new(new(1), new(71), 0, true)]);
        visits.Refuse = true;
        await using (var tx = fixture.Scope.Begin())
        {
            Assert.Null((await service.EnterAsync(context, default)).Entry);
            await tx.CommitAsync(default);
        }
        Assert.Equal(5, (await service.ReadQuotaAsync(new(71), default)).Remaining);
        visits.Refuse = false;
        for (var i = 0; i < 5; i++)
        {
            await using var tx = fixture.Scope.Begin();
            Assert.NotNull((await service.EnterAsync(context, default)).Entry);
            await tx.CommitAsync(default);
        }
        await using (var tx = fixture.Scope.Begin())
        {
            Assert.Equal(DungeonEntryFailure.DailyEntryLimitReached, (await service.EnterAsync(context, default)).Failure);
            await tx.CommitAsync(default);
        }
        Assert.Equal(0, (await service.ReadQuotaAsync(new(71), default)).Remaining);
        await using var reload = new LearningFixture(existing: fixture.Path);
        Assert.Equal(5, (await reload.Services.GetRequiredService<IUsageQuotaDomainService>()
            .ReadAsync(new(71), UsageQuotaKey.Dimension(new(2026, 10, 10)), default))!.UsedCount);
    }

    [Fact]
    public async Task Rolled_back_entry_and_blocked_party_member_do_not_consume_any_participant()
    {
        var clock = new Clock();
        await using var fixture = await Fixture(clock);
        var service = Service(fixture, clock, new Visits());
        var solo = new DungeonEntryContext(7100, DungeonEntryFacts.None, 0, 0, [new(new(1), new(71), 0, true)]);
        await using (var tx = fixture.Scope.Begin()) Assert.NotNull((await service.EnterAsync(solo, default)).Entry);
        Assert.Equal(5, (await service.ReadQuotaAsync(new(71), default)).Remaining);
        await using (var tx = fixture.Scope.Begin())
        {
            await fixture.Services.GetRequiredService<IEventService>().DirectAsync(new DimensionEntryRecorded([
                new(new(72), new(2026, 10, 10), null, 5)]));
            await tx.CommitAsync(default);
        }
        await using (var tx = fixture.Scope.Begin())
        {
            var result = await service.EnterAsync(solo with { Participants = [new(new(1), new(71), 0, true), new(new(2), new(72), 1, false)] }, default);
            Assert.Equal(DungeonEntryFailure.MemberEntryLimitReached, result.Failure);
            Assert.Equal((byte)1, result.BlockedSlot);
            await tx.CommitAsync(default);
        }
        Assert.Equal(5, (await service.ReadQuotaAsync(new(71), default)).Remaining);
    }

    [Fact]
    public async Task Reset_is_readonly_and_separate_from_mirror_midnight_and_stale_CAS_rolls_back()
    {
        var clock = new Clock { Now = new(2026, 10, 10, 5, 59, 0, TimeSpan.FromHours(8)) };
        await using var fixture = await Fixture(clock);
        var service = Service(fixture, clock, new Visits());
        var events = fixture.Services.GetRequiredService<IEventService>();
        await using (var tx = fixture.Scope.Begin())
        {
            await events.DirectAsync(new DimensionEntryRecorded([new(new(71), new(2026, 10, 9), null, 5)]));
            await tx.CommitAsync(default);
        }
        Assert.Equal(0, (await service.ReadQuotaAsync(new(71), default)).Remaining);
        clock.Now = clock.Now.AddMinutes(1);
        Assert.Equal(5, (await service.ReadQuotaAsync(new(71), default)).Remaining);
        Assert.Equal(1, fixture.Scalar("SELECT COUNT(*) FROM character_daily_counters"));
        await using (var tx = fixture.Scope.Begin())
            await Assert.ThrowsAsync<InvalidOperationException>(() => events.DirectAsync(new DimensionEntryRecorded([
                new(new(72), new(2026, 10, 10), null, 1), new(new(71), new(2026, 10, 9), null, 1)])).AsTask());
        Assert.Equal(1, fixture.Scalar("SELECT COUNT(*) FROM character_daily_counters"));
    }

    [Fact]
    public void Cards_use_career_normal_and_set_pools_and_preserve_the_ordinary_gold()
    {
        var rules = new DimensionDungeonRuleService(new Source());
        var face = new ClearRewardCardFace(ClearRewardCardKind.Free, [new(0, null), new(1, null)], 321);
        var free = rules.DrawCard(7100, 0, 17, face, 42)!;
        Assert.Equal(321, free.Gold);
        Assert.All(free.Slots, slot => Assert.Equal(100, slot.Item!.Value.ItemId));
        var paid = rules.DrawCard(7100, 0, 1, face with { Kind = ClearRewardCardKind.Paid }, 42)!;
        Assert.All(paid.Slots, slot => Assert.Equal(200, slot.Item!.Value.ItemId));
        Assert.Null(rules.DrawCard(144, 0, 1, face, 42));
        Assert.Null(rules.DrawCard(7100, 0, 1, face with { Kind = ClearRewardCardKind.BlackDiamond }, 42));
    }

    private static async Task<LearningFixture> Fixture(Clock clock)
    {
        var fixture = new LearningFixture(configure: services => services.AddSingleton<IDimensionDungeonContentSource>(new Source()));
        await fixture.SeedAsync();
        return fixture;
    }
    private static DimensionDungeonDomainService Service(LearningFixture f, Clock clock, Visits visits) => new(
        new DimensionDungeonRuleService(new Source()), new Structures(), visits,
        f.Services.GetRequiredService<IUsageQuotaDomainService>(), f.Services.GetRequiredService<IEventService>(),
        new Daily(), clock, f.Scope);
    private sealed class Source : IDimensionDungeonContentSource
    {
        public DimensionDungeonContent Load() => new(new HashSet<int> { 7100 }, 5, 0,
            new Dictionary<(int, int), DimensionChroniclePool> { [(0, 1)] = new(
                [new(100, 1, DropRarity.Rare, DropItemKind.Equipment, 10)],
                [new(200, 1, DropRarity.Rare, DropItemKind.Equipment, 20)]) }, new(3311, 1, DropRarity.Common, DropItemKind.Stackable));
    }
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 10, 10, 12, 0, 0, TimeSpan.FromHours(8));
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class Daily : IDailyResetRuleService
    {
        public DateTimeOffset PeriodStart(DateTimeOffset at)
        {
            var local = at.ToOffset(TimeSpan.FromHours(8));
            var start = new DateTimeOffset(local.Date.AddHours(6), local.Offset);
            return local < start ? start.AddDays(-1) : start;
        }
        public bool IsCurrentPeriod(DateTimeOffset at, DateTimeOffset now) => at >= PeriodStart(now) && at <= now;
    }
    private sealed class Structures : IDungeonStructureRuleService
    {
        public ValueTask<DungeonDefinition?> GetDefinitionAsync(int id, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<DungeonLayout?> ResolveAsync(int id, DungeonEntryFacts facts, int seed, CancellationToken ct) =>
            ValueTask.FromResult<DungeonLayout?>(new(id, 0, 1, 1, new(0, 0), new(0, 0), 53070, seed,
                new Dictionary<MazeCell, DungeonCellDefinition> { [new(0, 0)] = DungeonCellDefinition.Fixed(53070) }));
        public ValueTask<DungeonMapSelection> SelectMapAsync(DungeonLayout layout, MazeCell? from, MazeCell target, CancellationToken ct) => throw new NotSupportedException();
    }
    private sealed class Visits : IDungeonVisitDomainService
    {
        public bool Refuse;
        public bool DefersSelectionState(byte level, bool tutorialCompleted = false) => false;
        public ValueTask<DungeonEntryAttempt> TryEnterPreparedPartyAsync(IReadOnlyList<DungeonRunParticipant> members, DungeonLayout layout,
            DungeonEntryFacts facts, CancellationToken ct) => ValueTask.FromResult(Refuse
                ? DungeonEntryAttempt.Refused(DungeonEntryFailure.ConfigurationInvalid)
                : DungeonEntryAttempt.Accepted(new(layout, new Dictionary<CharacterId, DungeonRoomVisit>())));
        public ValueTask<DungeonEntry?> EnterAsync(CharacterId id, int dungeon, DungeonDifficulty difficulty, DungeonEntryFacts facts, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<DungeonRoomVisit?> MoveAsync(CharacterId id, DungeonRunState current, MazeCell target, CancellationToken ct) => throw new NotSupportedException();
    }
}
