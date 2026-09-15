using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dfo.Server.Application.Tests.World;

/// <summary>
/// What a monster's death puts on the floor, end to end but without the archive.
/// </summary>
/// <remarks>
/// The tables here are hand-built so that a rate is either certain or impossible, which is what
/// makes "these two slots hit and no others" a statement about the arrangement rather than about
/// a seed. The archive-shaped versions of the same numbers are asserted in
/// <c>PvfDropRateTableSourceTests</c>.
/// </remarks>
public sealed class MonsterDropServiceTests
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

    /// <summary>Every slot certain, so what comes out is decided by the pools alone.</summary>
    private static DropRateTable Certain() => Table([Basis, Basis, Basis, Basis, Basis]);

    /// <summary>No slot possible.</summary>
    private static DropRateTable Never() => Table([0, 0, 0, 0, 0]);

    /// <summary>Gold and equipment certain, the rest impossible.</summary>
    private static DropRateTable GoldAndEquipment() => Table([Basis, 0, Basis, 0, 0]);

    private static DropRateTable Table(IReadOnlyList<int> rates) => new(
        DropRateTables.Monster,
        [new DropRateBand(1, 200, rates)],
        [
            // Every ladder pinned to common, so a draw lands in the one bucket the pool has.
            new RarityLadder([1000002, 1000002, 1000002, 1000002, 1000002, 1000002, 1000003]),
            new RarityLadder([1000002, 1000002, 1000002, 1000002, 1000002, 1000002, 1000003]),
            new RarityLadder([1000002, 1000002, 1000002, 1000002, 1000002, 1000002, 1000003]),
            new RarityLadder([1000002, 1000002, 1000002, 1000002, 1000002, 1000002, 1000003]),
        ],
        [[1, 1, 1, 1], [1, 1, 1, 1], [1, 1, 1, 1], [1, 1, 1, 1], [1, 1, 1, 1]],
        [[1], [1], [1], [1], [1]],
        [[1], [1], [1], [1], [1]],
        new Dictionary<int, ItemGradeBand> { [MonsterLevel] = new(85, 87) });

    private static DropItemPool EquipmentPool() => OnePool(DropItemFamily.Equipment, 114000088);

    private static DropItemPool StackablePool() => OnePool(DropItemFamily.Stackable, 10087807);

    private static DropItemPool OnePool(DropItemFamily family, int itemId) =>
        new(
            family,
            new Dictionary<(int, int), IReadOnlyList<DropPoolEntry>>
            {
                [(86, (int)DropRarity.Common)] = [new DropPoolEntry(itemId, 1000)],
            });

    private static readonly GoldDropTable Gold = new(
        new Dictionary<int, int> { [MonsterLevel] = 1357 },
        new Dictionary<int, int> { [MonsterLevel] = 9 },
        [1, 2, 4, 6],
        (100, 110));

    private static (MonsterDropService Service, DungeonRunDomainService Runs) Build(
        DropRateTable table, int declaredRoomDrops = 0)
    {
        var runs = new DungeonRunDomainService();
        var service = new MonsterDropService(
            new DropRuleService(
                new StubRateTables(table),
                new StubGoldTables(Gold),
                new StubPools(),
                new DropRateOptions { RateBasis = Basis }),
            new NoDropRateAdjustment(),
            runs,
            new DungeonRoomRuleService(
                new NoRoomActors(), new StubRoomDrops(declaredRoomDrops)),
            NullLogger<MonsterDropService>.Instance);
        return (service, runs);
    }

    [Fact]
    public async Task Drop_rules_receive_the_party_size_frozen_on_the_shared_run()
    {
        var rules = new RecordingDropRules();
        var runs = new DungeonRunDomainService();
        var member = new DungeonRunParticipant(
            new AccountId(2), new CharacterId(2), Slot: 1, IsLeader: false);
        runs.BeginParty(
            [
                new DungeonRunParticipant(
                    new AccountId(1), Character, Slot: 0, IsLeader: true),
                member,
            ],
            Layout,
            difficulty: 0);
        var service = new MonsterDropService(
            rules,
            new NoDropRateAdjustment(),
            runs,
            new DungeonRoomRuleService(new NoRoomActors(), new StubRoomDrops(0)),
            NullLogger<MonsterDropService>.Instance);

        await service.ForKillAsync(
            new MonsterKill(member.CharacterId, runs.Find(member.CharacterId)!, ActorSequence, 86, 0),
            CancellationToken.None);

        Assert.NotNull(rules.Asked);
        Assert.Equal(2, rules.Asked.PartyMemberCount);
    }

    private static MonsterKill Kill(DungeonRunState run, ushort actor = ActorSequence, int rank = 0) =>
        new(Character, run, actor, MonsterLevel, rank);

    /// <summary>A kill whose every rate is zero puts nothing on the floor.</summary>
    [Fact]
    public async Task A_kill_that_hits_no_rate_drops_nothing()
    {
        var (service, runs) = Build(Never());
        var run = runs.Begin(Character, Layout, difficulty: 0);

        Assert.Empty(await service.ForKillAsync(Kill(run), CancellationToken.None));
        Assert.Empty(runs.Find(Character)!.MonsterDrops);
    }

    /// <summary>
    /// Two rates hitting produce two pieces under two numbers.
    /// </summary>
    /// <remarks>
    /// The outcome a mutually-exclusive distribution cannot produce, and the one the captures
    /// show: a kill that paid gold and an item in one frame.
    /// </remarks>
    [Fact]
    public async Task A_kill_that_hits_gold_and_equipment_drops_both_under_different_numbers()
    {
        var (service, runs) = Build(GoldAndEquipment());
        var run = runs.Begin(Character, Layout, difficulty: 0);

        var dropped = await service.ForKillAsync(Kill(run), CancellationToken.None);

        Assert.Equal(2, dropped.Count);
        Assert.Contains(dropped, drop => drop.IsGold && drop.Count >= 1357);
        Assert.Contains(dropped, drop => drop.ItemId == 114000088 && drop.Count == 1);
        Assert.Equal(2, dropped.Select(drop => drop.Number).Distinct().Count());
    }

    /// <summary>The same kill of the same monster in the same room answers the same way.</summary>
    /// <remarks>
    /// Two runs rather than two calls on one: a second call is a second kill, and a number handed
    /// out cannot be handed out again. What has to be reproducible is what was rolled, which is
    /// everything but the number.
    /// </remarks>
    [Fact]
    public async Task One_occasion_rolls_one_answer()
    {
        var first = Build(Certain());
        var second = Build(Certain());
        var firstRun = first.Runs.Begin(Character, Layout, difficulty: 0);
        var secondRun = second.Runs.Begin(Character, Layout, difficulty: 0);

        var once = await first.Service.ForKillAsync(Kill(firstRun), CancellationToken.None);
        var again = await second.Service.ForKillAsync(Kill(secondRun), CancellationToken.None);

        Assert.NotEmpty(once);
        Assert.Equal(once, again);
    }

    /// <summary>Two monsters of one room are not paid the same.</summary>
    [Fact]
    public async Task Two_monsters_of_one_room_are_rolled_apart()
    {
        var (service, runs) = Build(Certain());
        var run = runs.Begin(Character, Layout, difficulty: 0);

        var first = await service.ForKillAsync(Kill(run), CancellationToken.None);
        var second = await service.ForKillAsync(
            Kill(runs.Find(Character)!, actor: ActorSequence + 1), CancellationToken.None);

        Assert.NotEmpty(first);
        Assert.NotEmpty(second);
        Assert.NotEqual(
            first.Select(drop => drop.Count),
            second.Select(drop => drop.Count));

        // And nothing was numbered twice.
        var numbers = runs.Find(Character)!.MonsterDrops.Select(drop => drop.Drop.Number);
        Assert.Equal(numbers.Count(), numbers.Distinct().Count());
    }

    /// <summary>
    /// Numbering starts above what the room declares.
    /// </summary>
    /// <remarks>
    /// A room's own pieces are numbered from one by their declaration order and renumbered every
    /// time the room is described. A dropped piece given one of those numbers would be shadowed
    /// by a box the moment the player walked back in.
    /// </remarks>
    [Fact]
    public async Task A_number_is_given_above_what_the_room_already_declares()
    {
        var (service, runs) = Build(GoldAndEquipment(), declaredRoomDrops: 4);
        var run = runs.Begin(Character, Layout, difficulty: 0);

        var dropped = await service.ForKillAsync(Kill(run), CancellationToken.None);

        Assert.Equal([5, 6], dropped.Select(drop => (int)drop.Number));

        var next = await service.ForKillAsync(
            Kill(runs.Find(Character)!, actor: ActorSequence + 1), CancellationToken.None);
        Assert.Equal([7, 8], next.Select(drop => (int)drop.Number));
    }

    /// <summary>A character who is not in a run is not paid.</summary>
    [Fact]
    public async Task A_kill_outside_a_run_drops_nothing()
    {
        var (service, runs) = Build(Certain());
        var run = runs.Begin(Character, Layout, difficulty: 0);
        runs.End(Character);

        Assert.Empty(await service.ForKillAsync(Kill(run), CancellationToken.None));
    }

    /// <summary>A monster of a level the table does not cover drops nothing rather than throwing.</summary>
    [Fact]
    public async Task A_level_the_table_does_not_cover_drops_nothing()
    {
        var (service, runs) = Build(Table([Basis, 0, 0, 0, 0]) with
        {
            LevelBands = [new DropRateBand(1, 50, [Basis, 0, 0, 0, 0])],
        });
        var run = runs.Begin(Character, Layout, difficulty: 0);

        Assert.Empty(await service.ForKillAsync(Kill(run), CancellationToken.None));
    }

    /// <summary>
    /// A rank multiplies what a kill pays.
    /// </summary>
    /// <remarks>
    /// Asserted through gold rather than through a rate, because gold's rank multiple is a whole
    /// number the archive states outright: a boss pays six times an ordinary monster's base.
    /// </remarks>
    [Fact]
    public async Task A_boss_pays_its_own_multiple_of_the_gold_base()
    {
        var (service, runs) = Build(Table([Basis, 0, 0, 0, 0]));
        var run = runs.Begin(Character, Layout, difficulty: 0);

        var ordinary = await service.ForKillAsync(Kill(run), CancellationToken.None);
        var boss = await service.ForKillAsync(
            Kill(runs.Find(Character)!, actor: ActorSequence + 1, rank: 3),
            CancellationToken.None);

        Assert.InRange(ordinary.Single().Count, 1357, 1492);
        Assert.InRange(boss.Single().Count, 8142, 8956);
    }


    /// <summary>
    /// The avatar slot draws nothing even when it is certain to hit.
    /// </summary>
    /// <remarks>
    /// Avatars sit in the same list as ordinary gear, so a slot sent to the equipment pool with no
    /// filter pays fashion's rate in weapons and armour. At the endgame band that is one extra
    /// gear drop in twenty-eight, arriving through a rate the archive assigns to something else --
    /// and it would look like nothing but a slightly generous server.
    /// <para>
    /// The claim underneath this, that no avatar in the archive declares itself creatable, is
    /// checked in <c>PvfDropRateTableSourceTests.No_avatar_declares_itself_creatable</c>.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task The_avatar_slot_draws_nothing()
    {
        var (service, runs) = Build(Table([0, 0, 0, 0, Basis]));
        var run = runs.Begin(Character, Layout, difficulty: 0);

        Assert.Empty(await service.ForKillAsync(Kill(run), CancellationToken.None));
    }

    /// <summary>The two stackable slots draw stackables and the equipment slot draws equipment.</summary>
    [Fact]
    public async Task Each_item_slot_draws_from_the_family_it_is_mapped_to()
    {
        var (service, runs) = Build(Table([0, Basis, Basis, Basis, 0]));
        var run = runs.Begin(Character, Layout, difficulty: 0);

        var dropped = await service.ForKillAsync(Kill(run), CancellationToken.None);

        Assert.Equal(3, dropped.Count);
        Assert.Equal(2, dropped.Count(drop => drop.ItemId == 10087807));
        Assert.Single(dropped, drop => drop.ItemId == 114000088);
    }

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

    /// <summary>A room declaring no actors; these tests supply their own or need none.</summary>
    private sealed class NoRoomActors : IDungeonRoomActorSource
    {
        public ValueTask<IReadOnlyList<DungeonRoomActor>> GetAsync(
            int mapId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<DungeonRoomActor>>([]);
    }

    private sealed class StubRoomDrops(int count) : IDungeonRoomDropSource
    {
        public ValueTask<IReadOnlyList<DungeonRoomDrop>> GetAsync(
            int dungeonId, int mapId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<DungeonRoomDrop>>(
                [.. Enumerable.Range(0, count)
                    .Select(index => new DungeonRoomDrop((byte)index, 1000 + index, 0))]);
    }
}
