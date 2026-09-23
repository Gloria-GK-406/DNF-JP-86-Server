using System.Collections.Immutable;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dfo.Server.Application.Tests.World;

/// <summary>What is frozen onto an actor while its cell is created.</summary>
public sealed class MonsterDropDomainServiceTests
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

    private static MonsterDropDomainService Build(
        DropRateTable table, IQuestMonsterDropDomainService? quests = null) => new(
        new DropRuleService(
            new StubRateTables(table),
            new StubGoldTables(Gold),
            new StubPools(),
            new DropRateOptions { RateBasis = Basis }),
        new NoDropRateAdjustment(),
        quests ?? new NoQuestDrops(),
        NullLogger<MonsterDropDomainService>.Instance);

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
            NullLogger<MonsterDropDomainService>.Instance);

        await service.GenerateAsync(65601, Kill(Run(), partyMemberCount: 2), CancellationToken.None);

        Assert.Equal(2, Assert.IsType<MonsterDropRequest>(rules.Asked).PartyMemberCount);
    }

    [Fact]
    public async Task A_plan_that_hits_no_rate_contains_no_loot()
    {
        Assert.Empty(await Build(Never()).GenerateAsync(65601, Kill(Run()), CancellationToken.None));
    }

    [Fact]
    public async Task Gold_and_equipment_are_both_frozen_on_the_actor()
    {
        var planned = await Build(GoldAndEquipment())
            .GenerateAsync(65601, Kill(Run()), CancellationToken.None);

        Assert.Equal(2, planned.Count);
        Assert.Contains(planned, drop => drop.IsGold && drop.Count >= 1357);
        Assert.Contains(planned, drop => drop.ItemId == 114000088 && drop.Count == 1);
    }

    [Fact]
    public async Task One_occasion_resolves_one_deterministic_answer()
    {
        var once = await Build(Certain()).GenerateAsync(65601, Kill(Run()), CancellationToken.None);
        var again = await Build(Certain()).GenerateAsync(65601, Kill(Run()), CancellationToken.None);

        Assert.NotEmpty(once);
        Assert.Equal(once, again);
    }

    [Fact]
    public async Task Two_actor_identities_are_rolled_apart()
    {
        var service = Build(Certain());
        var run = Run();

        var first = await service.GenerateAsync(65601, Kill(run), CancellationToken.None);
        var second = await service.GenerateAsync(65601,
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

        Assert.Empty(await service.GenerateAsync(65601, Kill(Run()), CancellationToken.None));
    }

    [Fact]
    public async Task A_boss_uses_its_gold_multiple()
    {
        var service = Build(Table([Basis, 0, 0, 0, 0]));
        var run = Run();

        var ordinary = await service.GenerateAsync(65601, Kill(run), CancellationToken.None);
        var boss = await service.GenerateAsync(65601, Kill(run, actor: ActorSequence + 1, rank: 3),
            CancellationToken.None);

        Assert.InRange(ordinary.Single().Count, 1357, 1492);
        Assert.InRange(boss.Single().Count, 8142, 8956);
    }

    [Fact]
    public async Task The_avatar_slot_resolves_no_loot()
    {
        Assert.Empty(await Build(Table([0, 0, 0, 0, Basis]))
            .GenerateAsync(65601, Kill(Run()), CancellationToken.None));
    }

    [Fact]
    public async Task Each_item_slot_uses_its_mapped_family()
    {
        var planned = await Build(Table([0, Basis, Basis, Basis, 0]))
            .GenerateAsync(65601, Kill(Run()), CancellationToken.None);

        Assert.Equal(3, planned.Count);
        Assert.Equal(2, planned.Count(drop => drop.ItemId == 10087807));
        Assert.Single(planned, drop => drop.ItemId == 114000088);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Quest_source_is_composed_even_when_ordinary_table_does_not_cover_level(bool covered)
    {
        var table = GoldAndEquipment();
        if (!covered)
            table = table with { LevelBands = [] };
        var quests = new RecordingQuestDrops();
        var context = Kill(Run(), partyMemberCount: 2);
        var result = await Build(table, quests).GenerateAsync(65601, context, CancellationToken.None);

        Assert.Equal(new MonsterDropRequest(65601, context), quests.Asked);
        Assert.Equal(covered ? 3 : 1, result.Count);
        Assert.Equal(RecordingQuestDrops.Drop, result[^1]);
    }

    [Fact]
    public async Task An_invalid_participant_context_is_rejected_before_querying_sources()
    {
        var rules = new RecordingDropRules();
        var service = new MonsterDropDomainService(rules, new NoDropRateAdjustment(),
            new NoQuestDrops(), NullLogger<MonsterDropDomainService>.Instance);
        var context = Kill(Run());
        MonsterDropParticipant participant = new(new AccountId(1), Character);
        foreach (var invalid in new[]
        {
            context with { Participants = [] },
            context with { Participants = [participant, participant] },
            context with { CharacterId = new CharacterId(999) },
        })
            await Assert.ThrowsAsync<ArgumentException>(async () =>
                await service.GenerateAsync(65601, invalid, CancellationToken.None));
        Assert.Null(rules.Asked);
    }

    private sealed class RecordingQuestDrops : IQuestMonsterDropDomainService
    {
        public static readonly RolledDrop Drop = new(9001, 1, DropRarity.Common,
            DropItemKind.Stackable, OwnerCharacterId: Character,
            Delivery: RolledDropDelivery.DirectQuestItem, QuestId: 17, GrantLimit: 5);
        public MonsterDropRequest? Asked { get; private set; }
        public ValueTask<IReadOnlyList<RolledDrop>> DrawAsync(
            MonsterDropRequest request, CancellationToken cancellationToken)
        {
            this.Asked = request;
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

        public IReadOnlyList<RolledDrop> DrawFor(MonsterDropRequest kill, DropRates rates) => [];
    }

    private sealed class NoQuestDrops : IQuestMonsterDropDomainService
    {
        public ValueTask<IReadOnlyList<RolledDrop>> DrawAsync(
            MonsterDropRequest kill, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<RolledDrop>>([]);
    }
}
