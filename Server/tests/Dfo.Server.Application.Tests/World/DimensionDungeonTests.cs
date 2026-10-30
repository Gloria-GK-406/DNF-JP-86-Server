using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.UsageQuotas;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Tests.Characters;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;
using System.Collections.Immutable;
using Dfo.Server.Infrastructure.Content;
using Microsoft.Extensions.DependencyInjection;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.World;

public sealed class DimensionDungeonTests
{
    [SkippableFact]
    public async Task All_six_real_cells_materialize_with_the_boss_and_personal_chronicle_loot()
    {
        await TestGameplay.RunAsync(async () =>
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
            Assert.InRange(boss.PendingLoot.Count(item => item.Kind == DropItemKind.Equipment && item.OwnerCharacterId == new Dfo.Server.Data.Characters.CharacterId(71)), 1, 2);
            Assert.Equal(2, Assert.Single(boss.PendingLoot, item => item.ItemId == 3311 && item.OwnerCharacterId == new Dfo.Server.Data.Characters.CharacterId(71)).Count);
        });
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
        var drops = rules.DrawMonster(Request(), 42);
        Assert.InRange(drops.Count(x => x.Kind == DropItemKind.Equipment), 1, 2);
        var pools = content.GetRequiredService<IDimensionDungeonContentSource>().Load().Pools.Values;
        Assert.All(drops.Where(x => x.Kind == DropItemKind.Equipment), item =>
            Assert.Contains(item, pools.SelectMany(pool => pool.Normal.Concat(pool.Set))));
        var careerPool = content.GetRequiredService<IDimensionDungeonContentSource>().Load().Pools[(0, 1)];
        var careerIds = careerPool.Normal.Concat(careerPool.Set).Select(item => item.ItemId).ToHashSet();
        var sampled = Enumerable.Range(0, 256).SelectMany(seed => rules.DrawMonster(Request(), (ulong)seed))
            .Where(item => item.Kind == DropItemKind.Equipment).ToArray();
        Assert.Contains(sampled, item => !careerIds.Contains(item.ItemId));
        Assert.Equal(2, Assert.Single(drops, x => x.ItemId == 3311).Count);
        Assert.Equal(drops, rules.DrawMonster(Request(), 42));
        Assert.Empty(rules.DrawMonster(Request(dungeon: 144), 42));
    }

    [Fact]
    public async Task Entry_spends_only_after_preparation_persists_and_rejects_exhaustion()
    {
        await TestGameplay.RunAsync(async () =>
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
        });
    }

    [Fact]
    public async Task Rolled_back_entry_and_blocked_party_member_do_not_consume_any_participant()
    {
        await TestGameplay.RunAsync(async () =>
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
        });
    }

    [Fact]
    public async Task Reset_is_readonly_and_separate_from_mirror_midnight_and_stale_CAS_rolls_back()
    {
        await TestGameplay.RunAsync(async () =>
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
        });
    }

    [Fact]
    public void Monster_equipment_is_not_locked_to_the_recipients_career()
    {
        var rules = new DimensionDungeonRuleService(new CrossCareerSource());
        var equipment = Enumerable.Range(0, 256)
            .SelectMany(seed => rules.DrawMonster(Request(), (ulong)seed))
            .Where(item => item.Kind == DropItemKind.Equipment).Select(item => item.ItemId).ToHashSet();
        Assert.Contains(300, equipment);
        Assert.Contains(400, equipment);
    }

    [Fact]
    public void Cards_use_cross_career_normal_and_set_pools_and_preserve_the_ordinary_gold()
    {
        var rules = new DimensionDungeonRuleService(new CrossCareerSource());
        var face = new ClearRewardCardFace(ClearRewardCardKind.Free, [new(0, null), new(1, null)], 321);
        var free = rules.DrawCard(7100, face, 42)!;
        Assert.Equal(321, free.Gold);
        Assert.Equal(free.Slots, rules.DrawCard(7100, face, 42)!.Slots);
        var freeIds = Enumerable.Range(0, 128).SelectMany(seed => rules.DrawCard(7100, face, (ulong)seed)!.Slots)
            .Select(slot => slot.Item!.Value.ItemId).ToHashSet();
        Assert.Equal(new HashSet<int> { 100, 300 }, freeIds);
        var paid = rules.DrawCard(7100, face with { Kind = ClearRewardCardKind.Paid }, 42)!;
        Assert.Equal(face.Gold, paid.Gold);
        var paidIds = Enumerable.Range(0, 128).SelectMany(seed => rules.DrawCard(7100,
            face with { Kind = ClearRewardCardKind.Paid }, (ulong)seed)!.Slots).Select(slot => slot.Item!.Value.ItemId).ToHashSet();
        Assert.Equal(new HashSet<int> { 200, 400 }, paidIds);
        Assert.Null(rules.DrawCard(144, face, 42));
        Assert.Null(rules.DrawCard(7100, face with { Kind = ClearRewardCardKind.BlackDiamond }, 42));
    }

    [Theory]
    [InlineData(0, .20, .15, .20)]
    [InlineData(1, .25, .20, .30)]
    [InlineData(2, .30, .25, .40)]
    [InlineData(3, .35, .30, .50)]
    [InlineData(4, .40, .35, .60)]
    public void Difficulty_controls_elite_chance_boss_extra_and_set_weight(int tier, double eliteChance,
        double bossExtra, double setWeight)
    {
        var rules = new DimensionDungeonRuleService(new CrossCareerSource());
        const int sample = 10000;
        var eliteEquipment = 0;
        var bossEquipment = new List<RolledDrop>();
        for (ulong seed = 0; seed < sample; seed++)
        {
            var elite = rules.DrawMonster(Request(rank: 0, monster: 61340, tier: tier), seed);
            Assert.Equal(1, Assert.Single(elite, x => x.ItemId == 3311).Count);
            var eliteCount = elite.Count(x => x.Kind == DropItemKind.Equipment);
            Assert.InRange(eliteCount, 0, 1);
            eliteEquipment += eliteCount;
            var boss = rules.DrawMonster(Request(tier: tier), seed);
            Assert.Equal(2, Assert.Single(boss, x => x.ItemId == 3311).Count);
            Assert.InRange(boss.Count(x => x.Kind == DropItemKind.Equipment), 1, 2);
            bossEquipment.AddRange(boss.Where(x => x.Kind == DropItemKind.Equipment));
        }
        Assert.InRange(eliteEquipment / (double)sample, eliteChance - .02, eliteChance + .02);
        Assert.InRange(bossEquipment.Count / (double)sample - 1, bossExtra - .02, bossExtra + .02);
        Assert.InRange(bossEquipment.Count(x => x.ItemId is 200 or 400) / (double)bossEquipment.Count,
            setWeight - .02, setWeight + .02);
        // Every career within each category has equal weight.
        Assert.InRange(bossEquipment.Count(x => x.ItemId == 200) / (double)bossEquipment.Count(x => x.ItemId is 200 or 400), .47, .53);
    }

    [Fact]
    public void Actor_rank_defines_bosses_and_duplicate_career_rows_do_not_change_weights()
    {
        var original = new CrossCareerSource().Load();
        var duplicate = original with { Pools = new Dictionary<(int, int), DimensionChroniclePool>(original.Pools)
            { [(9, 1)] = original.Pools[(0, 1)] } };
        var rules = new DimensionDungeonRuleService(new CrossCareerSource());
        var duplicateRules = new DimensionDungeonRuleService(new FixedSource(duplicate));
        for (ulong seed = 0; seed < 256; seed++)
        {
            var request = Request(monster: 99999);
            var drops = rules.DrawMonster(request, seed);
            Assert.InRange(drops.Count(x => x.Kind == DropItemKind.Equipment), 1, 2);
            Assert.Equal(drops, duplicateRules.DrawMonster(request, seed));
            Assert.Empty(rules.DrawMonster(Request(rank: 0, monster: 99999), seed));
            Assert.Empty(rules.DrawMonster(request with { Context = request.Context with { SourceType = IndependentDropSourceType.APC } }, seed));
        }
    }

    private static MonsterDropRequest Request(int dungeon = 7100, int monster = 61809, int rank = 3, int tier = 0) =>
        new(monster, new(DungeonRunId.New(), dungeon, 42, new(0, 0), 53075, (DungeonDifficulty)tier, 1, 86, rank,
            new(71), ImmutableArray.Create(new MonsterDropParticipant(new(1), new(71)))));

    private sealed class FixedSource(DimensionDungeonContent content) : IDimensionDungeonContentSource
    {
        public DimensionDungeonContent Load() => content;
    }

    private static async Task<LearningFixture> Fixture(Clock clock)
    {
        var fixture = new LearningFixture(configure: services => services.AddSingleton<IDimensionDungeonContentSource>(new Source()));
        await fixture.SeedAsync();
        return fixture;
    }
    private static DimensionDungeonDomainService Service(LearningFixture f, Clock clock, Visits visits) => new(
        new DimensionDungeonRuleService(new Source()), new Structures(), visits,
        f.Services.GetRequiredService<IUsageQuotaDomainService>(), new DungeonAdmissionDomainService(f.Services.GetRequiredService<Dfo.Server.Abstractions.Characters.ICharacterDungeonDifficultyDomainService>(),
            new DungeonDifficultyRuleService(TestRequired.NoDungeonAdmissions), f.Services.GetRequiredService<Dfo.Server.Abstractions.Characters.ICharacterFatigueDomainService>(),
            quotas: f.Services.GetRequiredService<IUsageQuotaDomainService>(), events: f.Services.GetRequiredService<IEventService>(), transactions: f.Scope, characters: Dfo.Server.Application.Tests.Support.CurrentCharacterStub.Domain(new() { Progress = new(85, 0) }), tower: TestRequired.Absent<Dfo.Server.Abstractions.World.IllusionTower.IIllusionTowerDomainService>(), quests: TestRequired.Absent<Dfo.Server.Abstractions.Quests.ICharacterQuestLogDomainService>(), sessions: TestRequired.Absent<Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService>(), onlineCharacters: TestRequired.Absent<Dfo.Server.Abstractions.Characters.IOnlineCharacterRuntimeDomainService>(), runs: TestRequired.Absent<Dfo.Server.Abstractions.World.IDungeonRunDomainService>(), admissionRules: TestRequired.AdmissionRulesFrom(new Structures()), participation: TestRequired.Absent<Dfo.Server.Abstractions.World.IDungeonParticipationDomainService>(), couples: new Dfo.Server.Application.World.CoupleDungeonRule(TestRequired.Absent<Dfo.Server.Application.Marriage.IMarriageRelations>()), materials: TestRequired.Absent<Dfo.Server.Abstractions.World.IDungeonEntryMaterialsDomainService>()),
        new Daily(), clock);
    private sealed class Source : IDimensionDungeonContentSource
    {
        public DimensionDungeonContent Load() => new(new HashSet<int> { 7100 }, 5, 0,
            new Dictionary<(int, int), DimensionChroniclePool> { [(0, 1)] = new(
                [new(100, 1, DropRarity.Rare, DropItemKind.Equipment, 10)],
                [new(200, 1, DropRarity.Rare, DropItemKind.Equipment, 20)]) }, new(3311, 1, DropRarity.Common, DropItemKind.Stackable));
    }
    private sealed class CrossCareerSource : IDimensionDungeonContentSource
    {
        public DimensionDungeonContent Load()
        {
            var content = new Source().Load();
            return content with { Pools = new Dictionary<(int, int), DimensionChroniclePool>(content.Pools)
            {
                [(1, 1)] = new([new(300, 1, DropRarity.Rare, DropItemKind.Equipment, 30)],
                    [new(400, 1, DropRarity.Rare, DropItemKind.Equipment, 40)])
            } };
        }
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
        public ValueTask<DungeonDefinition?> GetDefinitionAsync(int id, CancellationToken ct) => ValueTask.FromResult<DungeonDefinition?>(new(id, "dimension-test", 1, true, []));
        public ValueTask<DungeonLayout?> ResolveAsync(int id, DungeonEntryFacts facts, int seed, CancellationToken ct) =>
            ValueTask.FromResult<DungeonLayout?>(new(id, 0, 1, 1, new(0, 0), new(0, 0), 53070, seed,
                new Dictionary<MazeCell, DungeonCellDefinition> { [new(0, 0)] = DungeonCellDefinition.Fixed(53070) }));
        public ValueTask<DungeonMapSelection> SelectMapAsync(DungeonLayout layout, MazeCell? from, MazeCell target, CancellationToken ct) => throw new NotSupportedException();
    }
    private sealed class Visits : IDungeonVisitDomainService
    {
        public ValueTask<DungeonEntryAttempt> EnterGrantedPartyAsync(IReadOnlyList<DungeonRunParticipant> members, DungeonLayout layout,
            DungeonEntryFacts facts, IDungeonAdmissionReceipt receipt, CancellationToken ct) => TryEnterPreparedPartyAsync(members, layout, facts, ct);
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
