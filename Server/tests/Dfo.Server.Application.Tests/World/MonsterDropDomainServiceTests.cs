using Dfo.Server.Testing;
using System.Collections.Immutable;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dfo.Server.Application.Tests.World;

/// <summary>What is frozen onto an actor while its cell is created.</summary>
public sealed class MonsterDropDomainServiceTests
{
    private sealed class NoGrowthPremiums(int bonus = 0) : Dfo.Server.Abstractions.Accounts.IPremiumServiceDomainService
    {
        public Dfo.Server.Abstractions.Accounts.IAccountPremiumServices For(AccountId id) => throw new NotSupportedException();
        public int Queries { get; private set; }
        public ValueTask<int> GetBossEquipmentDropBonusPercentAsync(IReadOnlyList<AccountId> participants, CancellationToken token)
        {
            Queries++;
            return ValueTask.FromResult(bonus);
        }
    }

    [Fact]
    public async Task Generation_passes_premium_bonus_only_for_ordinary_bosses()
    {
        var premiums = new NoGrowthPremiums(27);
        var independent = new RecordingIndependent();
        var service = new MonsterDropDomainService(new RecordingDropRules(), new NoDropRateAdjustment(), new NoQuestDrops(),
            NullLogger<MonsterDropDomainService>.Instance, new NoWorldDrops(), new NoDeclaredDrops(),
            new NoExtraDrops(), independent, premiums, CurrentCharacterStub.Domain(), dimensions: TestRequired.Absent<Dfo.Server.Abstractions.World.IDimensionDungeonRuleService>());
        await service.GenerateGroundAsync(17, Kill(Run(), rank:3), default);
        Assert.Equal(27, independent.Bonus);
        await service.GenerateGroundAsync(17, Kill(Run(), rank:0), default);
        Assert.Equal(0, independent.Bonus);
        await service.GenerateGroundAsync(17, Kill(Run(), rank:3) with {SourceType=IndependentDropSourceType.APC}, default);
        Assert.Equal(0, independent.Bonus);
        Assert.Equal(1, premiums.Queries);
    }
    [Fact]
    public async Task Independent_level_is_read_per_generation_and_only_for_a_declared_source()
    {
        var reads = 0;
        var character = new CurrentCharacterStub
        {
            ProgressReader = () => { reads++; return ValueTask.FromResult<CharacterProgressionState?>(new(20, 0)); },
        };
        var independent = new LevelRecordingIndependent();
        var service = new MonsterDropDomainService(new RecordingDropRules(), new NoDropRateAdjustment(), new NoQuestDrops(),
            NullLogger<MonsterDropDomainService>.Instance, new NoWorldDrops(), new NoDeclaredDrops(),
            new NoExtraDrops(), independent, new NoGrowthPremiums(), CurrentCharacterStub.Domain(character), dimensions: TestRequired.Absent<Dfo.Server.Abstractions.World.IDimensionDungeonRuleService>());
        await service.GenerateIndependentAsync(17, Kill(Run()), 0, default);
        Assert.Equal((1, 20), (reads, independent.Level));
        await service.GenerateIndependentAsync(17, Kill(Run()), 0, default);
        Assert.Equal(2, reads);
        independent.Covered = false;
        await service.GenerateIndependentAsync(17, Kill(Run()), 0, default);
        Assert.Equal(2, reads);
    }
    private sealed class LevelRecordingIndependent : IIndependentDropRuleService
    {
        public bool Covered { get; set; } = true;
        public int Level { get; private set; }
        public int Initialize() => 0;
        public bool Covers(MonsterDropRequest request) => Covered;
        public IReadOnlyList<RolledDrop> Draw(MonsterDropRequest request, int playerLevel, int growthBonusPercent)
        {
            Level = playerLevel;
            return [];
        }
    }
    private sealed class RecordingIndependent : IIndependentDropRuleService
    {
        public int Bonus { get; private set; }
        public int Initialize() => 0;
        public bool Covers(MonsterDropRequest request) => true;
        public IReadOnlyList<RolledDrop> Draw(MonsterDropRequest request, int playerLevel, int growthBonusPercent)
        {
            Bonus = growthBonusPercent;
            return [];
        }
    }
    private const int Basis = 10000;
    private static readonly CharacterId Character = new(1);
    private const ushort ActorSequence = 30001;
    private const int MonsterLevel = 86;

    [Fact]
    public async Task Dimension_personal_loot_is_frozen_per_recipient_without_multiplying_by_party_size()
    {
        var service = new MonsterDropDomainService(new RecordingDropRules(), new NoDropRateAdjustment(), new NoQuestDrops(),
            NullLogger<MonsterDropDomainService>.Instance, new NoWorldDrops(), new NoDeclaredDrops(),
            new NoExtraDrops(), new NoIndependentDrops(), new NoGrowthPremiums(), CurrentCharacterStub.Domain(),
            new DimensionDungeonRuleService(new DimensionSource()));
        for (var seed = 0; seed < 128; seed++)
        {
            var context = Kill(Run(), rank: 3, partyMemberCount: 4) with { DungeonId = 7100, Seed = seed };
            var equipment = 0;
            foreach (var participant in context.Participants)
            {
                var drops = await service.GeneratePersonalAsync(61809, participant.CharacterId, context, default);
                Assert.All(drops, item => Assert.Equal(participant.CharacterId, item.OwnerCharacterId));
                Assert.Equal(drops, await service.GeneratePersonalAsync(61809, participant.CharacterId, context, default));
                Assert.InRange(drops.Count(item => item.Kind == DropItemKind.Equipment), 1, 2);
                equipment += drops.Count(item => item.Kind == DropItemKind.Equipment);
                Assert.Equal(2, Assert.Single(drops, item => item.ItemId == 3311).Count);
            }
            Assert.InRange(equipment, 4, 8);
            Assert.Empty(await service.GeneratePersonalAsync(61809, Character, context with { DungeonId = 144 }, default));
        }
    }

    private sealed class DimensionSource : IDimensionDungeonContentSource
    {
        public DimensionDungeonContent Load() => new(new HashSet<int> { 7100 }, 5, 0,
            new Dictionary<(int, int), DimensionChroniclePool> { [(0, 1)] = new(
                [new(100, 1, DropRarity.Rare, DropItemKind.Equipment)],
                [new(200, 1, DropRarity.Rare, DropItemKind.Equipment)]) },
            new(3311, 1, DropRarity.Common, DropItemKind.Stackable));
    }

    private static readonly DungeonLayout Layout = new(
        DungeonId: 144,
        MazeIndex: 0,
        Width: 2,
        Height: 1,
        StartCell: new MazeCell(0, 0),
        BossCell: new MazeCell(1, 0),
        StartRoomMapId: 8153,
        Seed: 0x0BADF00D,
        Cells: new Dictionary<MazeCell, DungeonCellDefinition> {
            [new MazeCell(0, 0)] = DungeonCellDefinition.Fixed(8153),
            [new MazeCell(1, 0)] = DungeonCellDefinition.Fixed(8154),
        },
        BasisLevel: 86);

    private static DropRateTable Certain() => Table([Basis, Basis, Basis, Basis, Basis]);

    private static DropRateTable Never() => Table([0, 0, 0, 0, 0]);

    private static DropRateTable GoldAndEquipment() => Table([Basis, 0, Basis, 0, 0]);

    private static DropRateTable Table(IReadOnlyList<int> rates) => new(
        DropRateTables.Monster,
        [new DropRateBand(1, 200, rates)],
        [
            new RarityLadder([1000002, 1000002, 1000002, 1000002, 1000002, 1000002, 1000003]),
            new RarityLadder([1000002, 1000002, 1000002, 1000002, 1000002, 1000002, 1000003]),
            new RarityLadder([1000002, 1000002, 1000002, 1000002, 1000002, 1000002, 1000003]),
            new RarityLadder([1000002, 1000002, 1000002, 1000002, 1000002, 1000002, 1000003]),
        ],
        [[1, 1, 1, 1], [1, 1, 1, 1], [1, 1, 1, 1], [1, 1, 1, 1], [1, 1, 1, 1]],
        [[1], [1], [1], [1], [1]],
        [[1], [1], [1], [1], [1]],
        new Dictionary<int, ItemGradeBand> { [MonsterLevel] = new(85, 87) });

    private static readonly GoldDropTable Gold = new(
        new Dictionary<int, int> { [MonsterLevel] = 1357 },
        new Dictionary<int, int> { [MonsterLevel] = 9 },
        [1, 2, 4, 6],
        (100, 110));

    private static MonsterDropDomainService Build(
        DropRateTable table, IQuestEnemyDropDomainService? quests = null,
        IWorldDropRuleService? world = null, IExtraDropRuleService? extra = null,
        IIndependentDropRuleService? independent = null) => new(
        new DropRuleService(
            new StubRateTables(table),
            new StubGoldTables(Gold),
            new StubPools(),
            new DropRateOptions { RateBasis = Basis }),
        new NoDropRateAdjustment(),
        quests ?? new NoQuestDrops(),
        NullLogger<MonsterDropDomainService>.Instance, world ?? new NoWorldDrops(), new NoDeclaredDrops(), extra ?? new NoExtraDrops(), independent ?? new NoIndependentDrops(), new NoGrowthPremiums(), CurrentCharacterStub.Domain(), dimensions: TestRequired.Absent<Dfo.Server.Abstractions.World.IDimensionDungeonRuleService>());

    private static DungeonCellCreationContext Run() => new(
        DungeonRunId.New(),
        Layout,
        Layout.StartCell,
        Layout.StartRoomMapId,
        Difficulty: 0,
        DateTimeOffset.UnixEpoch,
        [new DungeonRunParticipant(
            new AccountId(1), Character, Slot: 0, IsLeader: true)]);

    private static MonsterDropContext Kill(
        DungeonCellCreationContext run,
        ushort actor = ActorSequence,
        int rank = 0,
        int partyMemberCount = 1) =>
        new(run.RunId, run.Layout.DungeonId, run.Layout.Seed, run.Cell, run.MapId,
            run.Difficulty, actor, MonsterLevel, rank, Character,
            Enumerable.Range(1, partyMemberCount)
                .Select(id => new MonsterDropParticipant(new AccountId(id), new CharacterId(id)))
                .ToImmutableArray());

    [Fact]
    public async Task Drop_rules_receive_the_party_size_frozen_by_cell_creation()
    {
        var rules = new RecordingDropRules();
        var service = new MonsterDropDomainService(
            rules,
            new NoDropRateAdjustment(),
            new NoQuestDrops(),
            NullLogger<MonsterDropDomainService>.Instance, new NoWorldDrops(), new NoDeclaredDrops(), new NoExtraDrops(), new NoIndependentDrops(), new NoGrowthPremiums(), CurrentCharacterStub.Domain(), dimensions: TestRequired.Absent<Dfo.Server.Abstractions.World.IDimensionDungeonRuleService>());

        await service.GenerateGroundAsync(65601, Kill(Run(), partyMemberCount: 2), CancellationToken.None);

        Assert.Equal(2, Assert.IsType<MonsterDropRequest>(rules.Asked).PartyMemberCount);
    }

    [Fact]
    public async Task A_plan_that_hits_no_rate_contains_no_loot()
    {
        Assert.Empty(await Build(Never()).GenerateGroundAsync(65601, Kill(Run()), CancellationToken.None));
    }

    [Fact]
    public async Task Gold_and_equipment_are_both_frozen_on_the_actor()
    {
        var planned = await Build(GoldAndEquipment())
            .GenerateGroundAsync(65601, Kill(Run()), CancellationToken.None);

        Assert.Equal(2, planned.Count);
        Assert.Contains(planned, drop => drop.IsGold && drop.Count >= 1357);
        Assert.Contains(planned, drop => drop.ItemId == 114000088 && drop.Count == 1);
    }

    [Fact]
    public async Task One_occasion_resolves_one_deterministic_answer()
    {
        var once = await Build(Certain()).GenerateGroundAsync(65601, Kill(Run()), CancellationToken.None);
        var again = await Build(Certain()).GenerateGroundAsync(65601, Kill(Run()), CancellationToken.None);

        Assert.NotEmpty(once);
        Assert.Equal(once, again);
    }

    [Fact]
    public async Task Source_composition_preserves_ordinary_draws_across_seeds()
    {
        var table = Table([4300, 2500, 6100, 1800, 1700]);
        var rules = new DropRuleService(new StubRateTables(table), new StubGoldTables(Gold),
            new StubPools(), new DropRateOptions { RateBasis = Basis });
        var service = Build(table);
        var context = Kill(Run());
        for (var seed = 1; seed <= 64; seed++)
        {
            var sample = context with { Seed = seed };
            var request = new MonsterDropRequest(65601, sample);
            var expected = rules.DrawFor(request, rules.RatesFor(request)!);
            var actual = await service.GenerateGroundAsync(65601, sample, CancellationToken.None);
            Assert.Equal(expected.ToArray(), actual.ToArray());
        }
    }

    [Fact]
    public async Task World_items_append_once_without_changing_ordinary_items_or_needing_a_rate_band()
    {
        var context = Kill(Run());
        var world = new CertainWorldDrop();
        var ordinary = await Build(Certain()).GenerateGroundAsync(65601, context, CancellationToken.None);
        var combined = await Build(Certain(), world: world).GenerateGroundAsync(65601, context, CancellationToken.None);
        Assert.Equal(ordinary.ToArray(), combined.Take(ordinary.Count).ToArray());
        Assert.Equal(3030, combined[^1].ItemId);
        Assert.Equal(ordinary.Count + 1, combined.Count);
        var uncovered = await Build(Certain() with { LevelBands = [] }, world: world)
            .GenerateGroundAsync(65601, context, CancellationToken.None);
        Assert.Equal(3030, Assert.Single(uncovered).ItemId);
        Assert.Equal(2, world.Calls);
    }

    private sealed class CertainWorldDrop : IWorldDropRuleService
    {
        public int Calls { get; private set; }
        public int Initialize() => 1;
        public IReadOnlyList<RolledDrop> Draw(MonsterDropRequest request)
        {
            Calls++;
            return [new(3030, 1, DropRarity.Uncommon, DropItemKind.Stackable)];
        }
    }

    [Fact]
    public async Task Two_actor_identities_are_rolled_apart()
    {
        var service = Build(Certain());
        var run = Run();

        var first = await service.GenerateGroundAsync(65601, Kill(run), CancellationToken.None);
        var second = await service.GenerateGroundAsync(65601,
            Kill(run, actor: ActorSequence + 1), CancellationToken.None);

        Assert.NotEqual(first.Select(drop => drop.Count), second.Select(drop => drop.Count));
    }

    [Fact]
    public async Task A_level_the_table_does_not_cover_resolves_no_loot()
    {
        var service = Build(Table([Basis, 0, 0, 0, 0]) with
        {
            LevelBands = [new DropRateBand(1, 50, [Basis, 0, 0, 0, 0])],
        });

        Assert.Empty(await service.GenerateGroundAsync(65601, Kill(Run()), CancellationToken.None));
    }

    [Fact]
    public async Task A_boss_uses_its_gold_multiple()
    {
        var service = Build(Table([Basis, 0, 0, 0, 0]));
        var run = Run();

        var ordinary = await service.GenerateGroundAsync(65601, Kill(run), CancellationToken.None);
        var boss = await service.GenerateGroundAsync(65601, Kill(run, actor: ActorSequence + 1, rank: 3),
            CancellationToken.None);

        Assert.InRange(ordinary.Single().Count, 1357, 1492);
        Assert.InRange(boss.Single().Count, 8142, 8956);
    }

    [Fact]
    public async Task The_avatar_slot_resolves_no_loot()
    {
        Assert.Empty(await Build(Table([0, 0, 0, 0, Basis]))
            .GenerateGroundAsync(65601, Kill(Run()), CancellationToken.None));
    }

    [Fact]
    public async Task Each_item_slot_uses_its_mapped_family()
    {
        var planned = await Build(Table([0, Basis, Basis, Basis, 0]))
            .GenerateGroundAsync(65601, Kill(Run()), CancellationToken.None);

        Assert.Equal(3, planned.Count);
        Assert.Equal(2, planned.Count(drop => drop.ItemId == 10087807));
        Assert.Single(planned, drop => drop.ItemId == 114000088);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Personal_generation_is_separate_from_ordinary_table_coverage(bool covered)
    {
        var table = GoldAndEquipment();
        if (!covered)
            table = table with { LevelBands = [] };
        var quests = new RecordingQuestDrops();
        var context = Kill(Run(), partyMemberCount: 2);
        var service = Build(table, quests);
        var ground = await service.GenerateGroundAsync(65601, context, CancellationToken.None);
        Assert.Null(quests.Asked);
        Assert.Equal(covered ? 2 : 0, ground.Count);
        var result = await service.GeneratePersonalAsync(65601, Character, context, CancellationToken.None);

        Assert.Equal(QuestEnemyDropRequest.FromMonster(new MonsterDropRequest(65601, context)), quests.Asked);
        Assert.Single(result);
        Assert.Equal(Character, quests.Recipient.CharacterId);
        Assert.Equal(RecordingQuestDrops.Drop, result[^1]);
    }

    [Fact]
    public async Task An_invalid_participant_context_is_rejected_before_querying_sources()
    {
        var rules = new RecordingDropRules();
        var service = new MonsterDropDomainService(rules, new NoDropRateAdjustment(),
            new NoQuestDrops(), NullLogger<MonsterDropDomainService>.Instance, new NoWorldDrops(), new NoDeclaredDrops(), new NoExtraDrops(), new NoIndependentDrops(), new NoGrowthPremiums(), CurrentCharacterStub.Domain(), dimensions: TestRequired.Absent<Dfo.Server.Abstractions.World.IDimensionDungeonRuleService>());
        var context = Kill(Run());
        MonsterDropParticipant participant = new(new AccountId(1), Character);
        foreach (var invalid in new[]
        {
            context with { Participants = [] },
            context with { Participants = [participant, participant] },
            context with { CharacterId = new CharacterId(999) },
        })
            await Assert.ThrowsAsync<ArgumentException>(async () =>
                await service.GenerateGroundAsync(65601, invalid, CancellationToken.None));
        Assert.Null(rules.Asked);
    }

    [Fact]
    public async Task Personal_generation_rejects_outsiders_without_reading_any_source()
    {
        var quests = new RecordingQuestDrops();
        await Assert.ThrowsAsync<ArgumentException>(async () => await Build(Certain(), quests)
            .GeneratePersonalAsync(65601, new CharacterId(999), Kill(Run()), CancellationToken.None));
        Assert.Null(quests.Asked);
    }

    [Fact]
    public async Task Personal_generation_queries_only_the_requested_identity_and_never_ordinary_rules()
    {
        var rules = new RecordingDropRules();
        var quests = new RecordingQuestDrops();
        var service = new MonsterDropDomainService(rules, new NoDropRateAdjustment(), quests,
            NullLogger<MonsterDropDomainService>.Instance, new NoWorldDrops(), new NoDeclaredDrops(), new NoExtraDrops(), new NoIndependentDrops(), new NoGrowthPremiums(), CurrentCharacterStub.Domain(), dimensions: TestRequired.Absent<Dfo.Server.Abstractions.World.IDimensionDungeonRuleService>());
        await service.GeneratePersonalAsync(65601, new CharacterId(2),
            Kill(Run(), partyMemberCount: 2), CancellationToken.None);
        Assert.Null(rules.Asked);
        Assert.Equal(new MonsterDropParticipant(new AccountId(2), new CharacterId(2)), quests.Recipient);
    }

    private sealed class RecordingQuestDrops : IQuestEnemyDropDomainService
    {
        public static readonly RolledDrop Drop = new(9001, 1, DropRarity.Common,
            DropItemKind.Stackable, OwnerCharacterId: Character,
            Delivery: RolledDropDelivery.DirectQuestItem, QuestId: 17, GrantLimit: 5);
        public QuestEnemyDropRequest? Asked { get; private set; }
        public MonsterDropParticipant Recipient { get; private set; }
        public ValueTask<IReadOnlyList<RolledDrop>> DrawAsync(
            QuestEnemyDropRequest request, MonsterDropParticipant participant, CancellationToken cancellationToken)
        {
            this.Asked = request;
            this.Recipient = participant;
            return ValueTask.FromResult<IReadOnlyList<RolledDrop>>([Drop]);
        }
    }

    private static DropItemPool EquipmentPool() => OnePool(
        DropItemFamily.Equipment, 114000088);

    private static DropItemPool StackablePool() => OnePool(
        DropItemFamily.Stackable, 10087807);

    private static DropItemPool OnePool(DropItemFamily family, int itemId) => new(
        family,
        new Dictionary<(int, int), IReadOnlyList<DropPoolEntry>>
        {
            [(86, (int)DropRarity.Common)] = [new DropPoolEntry(itemId, 1000)],
        });

    private sealed class StubRateTables(DropRateTable table) : IDropRateTableSource
    {
        public DropRateTable Load(string archivePath) => table;
    }

    private sealed class StubGoldTables(GoldDropTable gold) : IGoldDropTableSource
    {
        public GoldDropTable Load(string archivePath) => gold;
    }

    private sealed class StubPools : IDropItemPoolSource
    {
        public DropItemPool Load(DropItemFamily family) =>
            family == DropItemFamily.Equipment ? EquipmentPool() : StackablePool();
    }

    private sealed class RecordingDropRules : IDropRuleService
    {
        public MonsterDropRequest? Asked { get; private set; }

        public DropRates? RatesFor(MonsterDropRequest kill)
        {
            this.Asked = kill;
            return null;
        }

        public IReadOnlyList<RolledDrop> DrawFor(MonsterDropRequest kill, DropRates rates, bool includeItems = true, int attempt = 0) => [];
    }

    private sealed class NoQuestDrops : IQuestEnemyDropDomainService
    {
        public ValueTask<IReadOnlyList<RolledDrop>> DrawAsync(
            QuestEnemyDropRequest kill, MonsterDropParticipant participant, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<RolledDrop>>([]);
    }
    private sealed class NoWorldDrops : IWorldDropRuleService
    {
        public int Initialize() => 0;
        public IReadOnlyList<RolledDrop> Draw(MonsterDropRequest request) => [];
    }
    [Fact]
    public async Task Extra_success_suppresses_ordinary_items_but_keeps_gold_and_world()
    {
        var drops = await Build(GoldAndEquipment(), world: new CertainWorldDrop(), extra: new CertainExtra())
            .GenerateGroundAsync(65601, Kill(Run()), CancellationToken.None);
        Assert.Contains(drops, d => d.IsGold);
        Assert.Contains(drops, d => d.ItemId == 7001);
        Assert.Contains(drops, d => d.ItemId == 3030);
        Assert.DoesNotContain(drops, d => d.ItemId == 114000088);
    }
    [Fact]
    public async Task One_late_extra_hit_suppresses_every_ordinary_item_attempt()
    {
        var context = Kill(Run()) with { RandomDropCount = 3 };
        var drops = await Build(GoldAndEquipment(), extra: new LateExtra())
            .GenerateGroundAsync(65601, context, CancellationToken.None);
        Assert.Equal(3, drops.Count(d => d.IsGold));
        Assert.Single(drops, d => d.ItemId == 7001);
        Assert.DoesNotContain(drops, d => d.ItemId == 114000088);
    }
    [Fact]
    public async Task No_extra_hit_preserves_all_ordinary_attempts()
    {
        var drops = await Build(GoldAndEquipment()).GenerateGroundAsync(65601,
            Kill(Run()) with { RandomDropCount = 3 }, CancellationToken.None);
        Assert.Equal(3, drops.Count(d => d.IsGold));
        Assert.Equal(3, drops.Count(d => d.ItemId == 114000088));
    }
    [Fact]
    public async Task Zero_random_count_disables_both_channels_but_not_world()
    {
        var drops = await Build(Certain(), world: new CertainWorldDrop(), extra: new CertainExtra())
            .GenerateGroundAsync(65601, Kill(Run()) with { RandomDropCount = 0 }, CancellationToken.None);
        Assert.Equal(3030, Assert.Single(drops).ItemId);
    }
    private sealed class LateExtra : IExtraDropRuleService
    {
        public int Initialize() => 1;
        public IReadOnlyList<RolledDrop> Draw(MonsterDropRequest request, int attempt) => attempt == 2
            ? [new(7001, 1, DropRarity.Common, DropItemKind.Equipment)] : [];
    }
    private sealed class CertainExtra : IExtraDropRuleService
    {
        public int Initialize() => 1;
        public IReadOnlyList<RolledDrop> Draw(MonsterDropRequest request, int attempt) =>
            [new(7001, 1, DropRarity.Common, DropItemKind.Equipment)];
    }
    private sealed class NoExtraDrops : IExtraDropRuleService
    {
        public int Initialize() => 0;
        public IReadOnlyList<RolledDrop> Draw(MonsterDropRequest request, int attempt) => [];
    }
    private sealed class NoDeclaredDrops : IMonsterDeclaredDropRuleService
    {
        public int Initialize() => 0;
        public IReadOnlyList<RolledDrop> Draw(MonsterDropRequest request) => [];
    }
    private sealed class NoIndependentDrops : IIndependentDropRuleService
    {
        public int Initialize() => 0;
        public bool Covers(MonsterDropRequest request) => false;
        public IReadOnlyList<RolledDrop> Draw(MonsterDropRequest request, int playerLevel, int growthBonusPercent) => [];
    }
    [Theory]
    [InlineData(IndependentDropSourceType.Monster, 3)]
    [InlineData(IndependentDropSourceType.APC, 1)]
    public async Task Independent_rows_compose_with_monsters_but_APC_bypasses_ordinary_sources(
        IndependentDropSourceType type, int expected)
    {
        var service = Build(GoldAndEquipment(), independent: new CertainIndependent());
        var result = await service.GenerateGroundAsync(17, Kill(Run()) with { SourceType = type }, CancellationToken.None);
        Assert.Equal(expected, result.Count);
        Assert.Contains(result, d => d.ItemId == 998);
    }
    private sealed class CertainIndependent : IIndependentDropRuleService
    {
        public int Initialize() => 1;
        public bool Covers(MonsterDropRequest request) => true;
        public IReadOnlyList<RolledDrop> Draw(MonsterDropRequest request, int playerLevel, int growthBonusPercent) =>
            [new(998, 1, DropRarity.Common, DropItemKind.Stackable)];
    }
}
