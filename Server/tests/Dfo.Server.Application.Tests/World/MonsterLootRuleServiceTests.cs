using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dfo.Server.Application.Tests.World;

/// <summary>What is frozen onto an actor while its cell is created.</summary>
public sealed class MonsterLootRuleServiceTests
{
    private const int Basis = 10000;
    private static readonly CharacterId Character = new(1);
    private const ushort ActorSequence = 30001;
    private const int MonsterLevel = 86;

    private static readonly DungeonLayout Layout = new(
        DungeonId: 144,
        MazeIndex: 0,
        Width: 2,
        Height: 1,
        StartCell: new MazeCell(0, 0),
        BossCell: new MazeCell(1, 0),
        StartRoomMapId: 8153,
        Seed: 0x0BADF00D,
        Rooms: new Dictionary<MazeCell, int>
        {
            [new MazeCell(0, 0)] = 8153,
            [new MazeCell(1, 0)] = 8154,
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

    private static MonsterLootRuleService Build(DropRateTable table) => new(
        new DropRuleService(
            new StubRateTables(table),
            new StubGoldTables(Gold),
            new StubPools(),
            new DropRateOptions { RateBasis = Basis }),
        new NoDropRateAdjustment(),
        new NoQuestDrops(),
        NullLogger<MonsterLootRuleService>.Instance);

    private static DungeonCellCreationContext Run() => new(
        DungeonRunId.New(),
        Layout,
        Layout.StartCell,
        Layout.StartRoomMapId,
        Difficulty: 0,
        DateTimeOffset.UnixEpoch,
        [new DungeonRunParticipant(
            new AccountId(1), Character, Slot: 0, IsLeader: true)]);

    private static MonsterKill Kill(
        DungeonCellCreationContext run,
        ushort actor = ActorSequence,
        int rank = 0,
        int partyMemberCount = 1) =>
        new(Character, run, actor, MonsterLevel, rank, partyMemberCount);

    [Fact]
    public async Task Drop_rules_receive_the_party_size_frozen_by_cell_creation()
    {
        var rules = new RecordingDropRules();
        var service = new MonsterLootRuleService(
            rules,
            new NoDropRateAdjustment(),
            new NoQuestDrops(),
            NullLogger<MonsterLootRuleService>.Instance);

        await service.ResolveAsync(Kill(Run(), partyMemberCount: 2), CancellationToken.None);

        Assert.Equal(2, Assert.IsType<MonsterKill>(rules.Asked).PartyMemberCount);
    }

    [Fact]
    public async Task A_plan_that_hits_no_rate_contains_no_loot()
    {
        Assert.Empty(await Build(Never()).ResolveAsync(Kill(Run()), CancellationToken.None));
    }

    [Fact]
    public async Task Gold_and_equipment_are_both_frozen_on_the_actor()
    {
        var planned = await Build(GoldAndEquipment())
            .ResolveAsync(Kill(Run()), CancellationToken.None);

        Assert.Equal(2, planned.Count);
        Assert.Contains(planned, drop => drop.IsGold && drop.Count >= 1357);
        Assert.Contains(planned, drop => drop.ItemId == 114000088 && drop.Count == 1);
    }

    [Fact]
    public async Task One_occasion_resolves_one_deterministic_answer()
    {
        var once = await Build(Certain()).ResolveAsync(Kill(Run()), CancellationToken.None);
        var again = await Build(Certain()).ResolveAsync(Kill(Run()), CancellationToken.None);

        Assert.NotEmpty(once);
        Assert.Equal(once, again);
    }

    [Fact]
    public async Task Two_actor_identities_are_rolled_apart()
    {
        var service = Build(Certain());
        var run = Run();

        var first = await service.ResolveAsync(Kill(run), CancellationToken.None);
        var second = await service.ResolveAsync(
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

        Assert.Empty(await service.ResolveAsync(Kill(Run()), CancellationToken.None));
    }

    [Fact]
    public async Task A_boss_uses_its_gold_multiple()
    {
        var service = Build(Table([Basis, 0, 0, 0, 0]));
        var run = Run();

        var ordinary = await service.ResolveAsync(Kill(run), CancellationToken.None);
        var boss = await service.ResolveAsync(Kill(run, actor: ActorSequence + 1, rank: 3),
            CancellationToken.None);

        Assert.InRange(ordinary.Single().Count, 1357, 1492);
        Assert.InRange(boss.Single().Count, 8142, 8956);
    }

    [Fact]
    public async Task The_avatar_slot_resolves_no_loot()
    {
        Assert.Empty(await Build(Table([0, 0, 0, 0, Basis]))
            .ResolveAsync(Kill(Run()), CancellationToken.None));
    }

    [Fact]
    public async Task Each_item_slot_uses_its_mapped_family()
    {
        var planned = await Build(Table([0, Basis, Basis, Basis, 0]))
            .ResolveAsync(Kill(Run()), CancellationToken.None);

        Assert.Equal(3, planned.Count);
        Assert.Equal(2, planned.Count(drop => drop.ItemId == 10087807));
        Assert.Single(planned, drop => drop.ItemId == 114000088);
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
        public MonsterKill? Asked { get; private set; }

        public DropRates? RatesFor(MonsterKill kill)
        {
            this.Asked = kill;
            return null;
        }

        public IReadOnlyList<RolledDrop> DrawFor(MonsterKill kill, DropRates rates) => [];
    }

    private sealed class NoQuestDrops : IQuestMonsterDropRuleService
    {
        public ValueTask<IReadOnlyList<RolledDrop>> DrawAsync(
            MonsterKill kill, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<RolledDrop>>([]);
    }
}
