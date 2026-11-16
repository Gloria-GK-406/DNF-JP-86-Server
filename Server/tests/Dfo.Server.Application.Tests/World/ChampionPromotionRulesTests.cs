using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.World;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Tests.World;

/// <summary>Random and fixed champion promotion at room creation (#355, 70 getAddChampionMobCnt).</summary>
[Trait("Category", "Unit")]
public sealed class ChampionPromotionRulesTests
{
    private const int MapId = 123;
    private static readonly int[] GrakKarak = [3, 33, 50, 70, 80];

    [Theory]
    [InlineData(DungeonDifficulty.Normal, 3, 3, 33)]
    [InlineData(DungeonDifficulty.Adventure, 3, 3, 544)]
    [InlineData(DungeonDifficulty.Tier2, 3, 3, 1388)]
    [InlineData(DungeonDifficulty.Tier3, 3, 3, 3888)]
    [InlineData(DungeonDifficulty.Tier4, 3, 3, 888)]
    [InlineData(DungeonDifficulty.Normal, 4, 5, 15)]
    public void Chance_is_rate_times_difficulty_multiplier_over_maze_area(
        DungeonDifficulty difficulty, byte width, byte height, int expected) =>
        Assert.Equal(expected, ChampionPromotionRules.RandomChance(GrakKarak, difficulty, width, height));

    [Fact]
    public void An_undeclared_rate_is_no_chance() =>
        Assert.Equal(0, ChampionPromotionRules.RandomChance([], DungeonDifficulty.Tier3, 1, 1));

    [Fact]
    public async Task Fixed_count_promotes_only_plain_hostile_unnamed_monster_rows()
    {
        var contents = await DescribeAsync(Room(), new(2, SuppressesRandom: true), [0, 0, 0, 0, 0]);
        var types = contents.Actors.Select(actor => actor.Actor.ActorType).ToArray();

        Assert.Equal(2, types.Take(3).Count(type => type == 1));
        Assert.All(types.Take(3), type => Assert.True(type is 0 or 1));
        // Named, boss, dummy, AI character and object template keep their declared type.
        Assert.Equal(new byte[] { 0, 3, 0, 5, 0 }, types.Skip(3));
    }

    [Fact]
    public async Task A_fixed_count_beyond_the_candidates_promotes_every_candidate_once()
    {
        var contents = await DescribeAsync(Room(), new(9, SuppressesRandom: true), [0, 0, 0, 0, 0]);
        Assert.Equal(new byte[] { 1, 1, 1, 0, 3, 0, 5, 0 }, contents.Actors.Select(actor => actor.Actor.ActorType));
    }

    [Fact]
    public async Task A_certain_random_chance_adds_exactly_one_champion_unless_the_map_fixes_its_count()
    {
        var random = await DescribeAsync(Room(), default, [100, 100, 100, 100, 100]);
        Assert.Equal(1, random.Actors.Count(actor => actor.Actor.ActorType == 1));

        var fixedOnly = await DescribeAsync(Room(), new(0, SuppressesRandom: true), [100, 100, 100, 100, 100]);
        Assert.DoesNotContain(fixedOnly.Actors, actor => actor.Actor.ActorType == 1);

        var both = await DescribeAsync(Room(), new(1, SuppressesRandom: false), [100, 100, 100, 100, 100]);
        Assert.Equal(2, both.Actors.Count(actor => actor.Actor.ActorType == 1));
    }

    [Fact]
    public async Task A_layout_without_rates_promotes_nothing_even_where_the_map_fixes_a_count()
    {
        var contents = await DescribeAsync(Room(), new(2, SuppressesRandom: true), rates: null);
        Assert.DoesNotContain(contents.Actors, actor => actor.Actor.ActorType == 1);
    }

    [Fact]
    public async Task The_same_room_of_the_same_run_promotes_the_same_monsters()
    {
        var first = await DescribeAsync(Room(), new(1, SuppressesRandom: true), [0, 0, 0, 0, 0]);
        var second = await DescribeAsync(Room(), new(1, SuppressesRandom: true), [0, 0, 0, 0, 0]);
        Assert.Equal(first.Actors.Select(actor => actor.Actor.ActorType), second.Actors.Select(actor => actor.Actor.ActorType));
    }

    [Fact]
    public async Task Promotion_spreads_over_the_candidates_across_runs()
    {
        var picked = new HashSet<int>();
        for (var seed = 1; seed <= 64; seed++)
        {
            var contents = await DescribeAsync(Room(), new(1, SuppressesRandom: true), [0, 0, 0, 0, 0], seed);
            picked.Add(contents.Actors.ToList().FindIndex(actor => actor.Actor.ActorType == 1));
        }
        Assert.Equal(new[] { 0, 1, 2 }, picked.Order());
    }

    private static DungeonRoomActor[] Room() =>
    [
        Monster(61670), Monster(61671), Monster(61672),
        Monster(50001),
        new(61673, 20, 3) { Declaration = new(61673, DungeonActorSpawnSource.Monster, 4) { Rank = 3 } },
        new(61674, 20, 0) { Declaration = new(61674, DungeonActorSpawnSource.Monster, 5) { IsDummy = true }, IsHostile = false },
        new(461, 20, 5) { Declaration = new(461, DungeonActorSpawnSource.AiCharacter, 0) },
        new(61675, 20, 0)
        {
            Declaration = new(61675, DungeonActorSpawnSource.SpecialPassiveObject, 0) { ParentObjectIndex = 0, ChildIndex = 0 },
        },
    ];

    private static DungeonRoomActor Monster(int code) =>
        new(code, 20, 0) { Declaration = new(code, DungeonActorSpawnSource.Monster, 0) };

    private static async Task<DungeonRoomContents> DescribeAsync(
        DungeonRoomActor[] actors, MapChampionDeclaration map, int[]? rates, int seed = 456)
    {
        var cell = new MazeCell(0, 0);
        var layout = new DungeonLayout(7, 0, 1, 1, cell, cell, MapId, seed,
            new Dictionary<MazeCell, DungeonCellDefinition> { [cell] = DungeonCellDefinition.Fixed(MapId) },
            BasisLevel: 20) { ChampionRates = rates };
        var source = new ChampionRoomSource(actors, map);
        return await new DungeonRoomRuleService(source, source, source, source, mechanisms: new DungeonRoomMechanismRuleService())
            .DescribeAsync(new(DungeonRunId.New(), layout, cell, MapId, DungeonDifficulty.Normal, DateTimeOffset.UnixEpoch, []), default);
    }

    private sealed class ChampionRoomSource(DungeonRoomActor[] actors, MapChampionDeclaration map) : IDungeonRoomActorSource,
        IDungeonRoomDropSource, IObjectDropRuleService, IDungeonRoomObjectSource
    {
        public ValueTask<IReadOnlyList<DungeonRoomActor>> GetAsync(int id, CancellationToken ct) =>
            ValueTask.FromResult<IReadOnlyList<DungeonRoomActor>>(actors);
        public ValueTask<DungeonMapActorDeclarations> GetDeclarationsAsync(int mapId, CancellationToken ct) =>
            ValueTask.FromResult(new DungeonMapActorDeclarations([], []) { Champions = map });
        public ValueTask<IReadOnlyList<ObjectDropDefinition>> GetAsync(int dungeon, int mapId, CancellationToken ct) =>
            ValueTask.FromResult<IReadOnlyList<ObjectDropDefinition>>([]);
        public IReadOnlyList<DungeonRoomDrop> Draw(IReadOnlyList<ObjectDropDefinition> definitions, DungeonCellCreationContext context) => [];
        public ValueTask<IReadOnlyList<DungeonRoomObject>> GetObjectsAsync(int mapId, CancellationToken ct) =>
            ValueTask.FromResult<IReadOnlyList<DungeonRoomObject>>([]);
    }
}
