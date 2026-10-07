using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.World;

namespace Dfo.Server.Application.Tests.World;

/// <summary>
/// The rule that turns dungeon content and one seed into a run's maze.
/// </summary>
/// <remarks>
/// The properties worth pinning are that it is a function -- same inputs, same layout, no matter
/// how often or in what order it is asked -- and that it never reaches for randomness of its own.
/// Both exist so a run can be replayed from its record and so the client, which is told the seed,
/// can be checked against the same derivation.
/// </remarks>
public sealed class DungeonStructureRuleServiceTests
{
    /// <summary>The tutorial's maze, as read out of the archive on 2026-08-18.</summary>
    private static readonly MazeDefinition TutorialMaze = new(
        Index: 0,
        Width: 5,
        Height: 3,
        Cells: new Dictionary<MazeCell, DungeonCellDefinition> {
            [new MazeCell(0, 1)] = FixedRoom(61000),
            [new MazeCell(1, 1)] = FixedRoom(61001),
            [new MazeCell(2, 1)] = FixedRoom(61002),
            [new MazeCell(3, 1)] = FixedRoom(61003),
            [new MazeCell(4, 1)] = FixedRoom(61005),
            [new MazeCell(3, 2)] = FixedRoom(61004),
            [new MazeCell(3, 0)] = FixedRoom(61006),
        },
        StartCandidates: [new MazeCell(0, 1)],
        BossCandidates: [new MazeCell(3, 0)]);

    [Fact]
    public async Task The_tutorial_starts_in_the_room_its_start_cell_names()
    {
        var service = Service(Tutorial());

        var layout = await service.ResolveAsync(10000, DungeonEntryFacts.None, seed: 12345, default);

        Assert.NotNull(layout);
        Assert.Equal(0, layout!.MazeIndex);
        Assert.Equal(new MazeCell(0, 1), layout.StartCell);
        Assert.Equal(new MazeCell(3, 0), layout.BossCell);
        Assert.Equal(61000, layout.StartRoomMapId);
        Assert.Equal(5, layout.Width);
        Assert.Equal(3, layout.Height);
    }

    /// <remarks>
    /// The seed is carried out again because the client is told it: NOTI 0x001D puts it at offset
    /// 3 and draws on it locally, so a layout that dropped it would leave the two sides unable to
    /// agree on anything derived from it.
    /// </remarks>
    [Fact]
    public async Task The_seed_it_was_given_is_the_seed_it_reports()
    {
        var service = Service(Tutorial());

        var layout = await service.ResolveAsync(10000, DungeonEntryFacts.None, seed: -777, default);

        Assert.Equal(-777, layout!.Seed);
    }

    [Fact]
    public async Task The_same_seed_always_produces_the_same_layout()
    {
        var service = Service(ManyCandidates());

        var first = await service.ResolveAsync(1, DungeonEntryFacts.None, seed: 99, default);
        var second = await service.ResolveAsync(1, DungeonEntryFacts.None, seed: 99, default);

        Assert.Equal(first, second);
    }

    /// <remarks>
    /// Without this the previous test would pass on a rule that ignored the seed entirely.
    /// </remarks>
    [Fact]
    public async Task Different_seeds_reach_different_cells()
    {
        var service = Service(ManyCandidates());

        var cells = new HashSet<MazeCell>();
        for (var seed = 0; seed < 200; seed++)
            cells.Add((await service.ResolveAsync(1, DungeonEntryFacts.None, seed, default))!.StartCell);

        Assert.True(cells.Count > 1, "the start cell never varied with the seed");
    }

    [Fact]
    public async Task A_dungeon_the_archive_does_not_declare_has_no_layout()
    {
        var service = Service(Tutorial());

        Assert.Null(await service.ResolveAsync(999_999, DungeonEntryFacts.None, seed: 1, default));
    }

    /// <remarks>
    /// Content that starts a run on a cell carrying no room would put the character in a map the
    /// server cannot name. It is refused here rather than passed on as a zero.
    /// </remarks>
    [Fact]
    public async Task A_start_cell_with_no_room_is_refused()
    {
        var broken = new MazeDefinition(
            0, 5, 3,
            new Dictionary<MazeCell, DungeonCellDefinition> { [new MazeCell(1, 1)] = DungeonCellDefinition.Fixed(61001)},
            [new MazeCell(0, 1)],
            [new MazeCell(1, 1)]);
        var service = Service(new DungeonDefinition(1, "dungeon/Broken.dgn", 1, true, [broken]));

        var error = await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await service.ResolveAsync(1, DungeonEntryFacts.None, seed: 1, default));

        Assert.Contains("declares no room", error.Message);
    }

    [Fact]
    public async Task A_single_maze_is_used_whatever_the_seed()
    {
        var service = Service(Tutorial());

        for (var seed = 0; seed < 50; seed++)
        {
            var layout = await service.ResolveAsync(10000, DungeonEntryFacts.None, seed, default);
            Assert.Equal(0, layout!.MazeIndex);
        }
    }

    [Theory]
    [InlineData("start")]
    [InlineData("boss")]
    [InlineData("geometry")]
    [InlineData("parse")]
    public async Task Selected_maze_configuration_is_rejected_before_a_layout_is_returned(string invalid)
    {
        var maze = TutorialMaze;
        maze = invalid switch
        {
            "start" => maze with { StartCandidates = [] },
            "boss" => maze with { BossCandidates = [new MazeCell(4, 2)] },
            "geometry" => maze with { Width = 1 },
            _ => maze with { ConfigurationError = "greed grid size mismatch" },
        };
        var service = Service(Tutorial() with { Mazes = [maze] });
        var error = await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await service.ResolveAsync(10000, DungeonEntryFacts.None, 1, default));
        Assert.Contains("maze '0'", error.Message);
    }

    [Fact]
    public async Task Unselected_quest_maze_semantics_do_not_block_the_ordinary_maze()
    {
        var brokenQuest = TutorialMaze with
        {
            Index = 1, Width = 0, StartCandidates = [],
            QuestConnection = new(DungeonQuestConnectionState.Active, 1776, -1),
            ConfigurationError = "bad quest maze geometry",
        };
        var definition = Tutorial() with { Mazes = [TutorialMaze, brokenQuest] };
        var rules = Service(definition);
        Assert.NotNull(await rules.ResolveAsync(10000, DungeonEntryFacts.None, 1, default));
        var directed = new DungeonEntryFacts(0, [1776], [], DungeonEntryIntent.Directed(10000));
        var error = await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await rules.ResolveAsync(10000, directed, 1, default));
        Assert.Contains("bad quest maze geometry", error.Message);
    }

    [Fact]
    public async Task Entry_preflights_each_reachable_incoming_direction_including_the_boss_room()
    {
        var start = new MazeCell(0, 0);
        var ordinary = new MazeCell(1, 0);
        var boss = new MazeCell(2, 0);
        var maze = new MazeDefinition(0, 3, 1, new Dictionary<MazeCell, DungeonCellDefinition>
        {
            [start] = new(DungeonPathDirection.Right, [10], []),
            [ordinary] = new(DungeonPathDirection.Left | DungeonPathDirection.Right, [11], []),
            [boss] = new(DungeonPathDirection.Left, [], [12]),
        }, [start], [boss]);
        var definitions = new StubDungeonSource(new(1, "dungeon/preflight.dgn", 1, false, [maze]));
        var maps = new PreflightMaps(ordinaryAcceptsLeft: false, bossPresent: true);
        var rules = new DungeonStructureRuleService(definitions, maps);
        var error = await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await rules.ResolveAsync(1, DungeonEntryFacts.None, 1, default));
        Assert.Contains("(1,0)", error.Message);

        rules = new DungeonStructureRuleService(definitions, new PreflightMaps(true, false));
        error = await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await rules.ResolveAsync(1, DungeonEntryFacts.None, 1, default));
        Assert.Contains("(2,0)", error.Message);
        Assert.NotNull(await new DungeonStructureRuleService(definitions, new PreflightMaps(true, true))
            .ResolveAsync(1, DungeonEntryFacts.None, 1, default));
    }

    private sealed class PreflightMaps(bool ordinaryAcceptsLeft, bool bossPresent) : IDungeonMapDefinitionSource
    {
        public ValueTask<DungeonMapDefinition?> GetAsync(int id, CancellationToken token) =>
            ValueTask.FromResult<DungeonMapDefinition?>(id switch
            {
                10 => new(10, [1], DungeonMapKind.Normal,
                    [new(DungeonPathDirection.Right, DungeonPathDirection.Right)], true),
                11 => new(11, [1], DungeonMapKind.Normal,
                    [new(DungeonPathDirection.Left | DungeonPathDirection.Right,
                        ordinaryAcceptsLeft ? DungeonPathDirection.All : DungeonPathDirection.Right)], false),
                12 when bossPresent => new(12, [1], DungeonMapKind.Boss,
                    [new(DungeonPathDirection.Left, DungeonPathDirection.Left)], false),
                _ => null,
            });
        public ValueTask<IReadOnlyList<DungeonMapDefinition>> ListForDungeonAsync(int id, CancellationToken token) =>
            ValueTask.FromResult<IReadOnlyList<DungeonMapDefinition>>([]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Final_layout_preflights_the_hell_replacement_instead_of_the_unavailable_ordinary_resource(bool replaceStart)
    {
        var start = new MazeCell(0, 0);
        var boss = new MazeCell(1, 0);
        var maze = new MazeDefinition(0, 2, 1, new Dictionary<MazeCell, DungeonCellDefinition>
        {
            [start] = new(DungeonPathDirection.Right, [replaceStart ? 999 : 10], []),
            [boss] = new(DungeonPathDirection.Left, [], [replaceStart ? 11 : 999]),
        }, [start], [boss]);
        IDungeonStructureRuleService rules = new DungeonStructureRuleService(
            new StubDungeonSource(new(1, "dungeon/replacement.dgn", 1, false, [maze])),
            new ReplacementMaps(replaceStart, "valid"));
        var topology = Assert.IsType<DungeonLayout>(await rules.ResolveTopologyAsync(1, DungeonEntryFacts.None, 1, default));
        Assert.Equal(0, topology.StartRoomMapId);
        var prepared = await rules.PrepareLayoutAsync(topology with
        {
            EntryKind = DungeonEntryKind.ManualHell,
            Hell = new HellEntryPlan(0, replaceStart ? start : boss, 900, []),
        }, default);
        Assert.Equal(replaceStart ? 900 : 10, prepared.StartRoomMapId);
        Assert.Equal(900, (await rules.SelectMapAsync(prepared, replaceStart ? null : start,
            replaceStart ? start : boss, default)).MapId);
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await rules.ResolveAsync(1, DungeonEntryFacts.None, 1, default));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("pattern")]
    [InlineData("entrance")]
    [InlineData("separate-pairs")]
    public async Task Hell_replacement_requires_an_existing_map_with_compatible_pattern_and_entrance(string failure)
    {
        var start = new MazeCell(0, 0);
        var boss = new MazeCell(1, 0);
        var layout = new DungeonLayout(1, 0, 2, 1, start, boss, 10, 1,
            new Dictionary<MazeCell, DungeonCellDefinition>
            {
                [start] = new(DungeonPathDirection.Right, [10], []),
                [boss] = new(DungeonPathDirection.Left, [], [11]),
            }) { EntryKind = DungeonEntryKind.ManualHell, Hell = new(0, boss, 900, []) };
        IDungeonStructureRuleService rules = new DungeonStructureRuleService(
            new StubDungeonSource(Tutorial()), new ReplacementMaps(false, failure));
        var selected = await rules.SelectMapAsync(layout, start, boss, default);
        Assert.Null(selected.MapId);
        Assert.Equal(DungeonMapSelectionFailure.NoCompatibleMap, selected.Failure);
        var error = await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await rules.PrepareLayoutAsync(layout, default));
        Assert.Contains("(1,0)", error.Message);
    }

    private sealed class ReplacementMaps(bool replaceStart, string replacement) : IDungeonMapDefinitionSource
    {
        public ValueTask<DungeonMapDefinition?> GetAsync(int id, CancellationToken token)
        {
            var pattern = replaceStart ? DungeonPathDirection.Right : DungeonPathDirection.Left;
            DungeonMapDefinition? result = id switch
            {
                10 => new(10, [1], DungeonMapKind.Normal,
                    [new(DungeonPathDirection.Right, DungeonPathDirection.All)], true),
                11 => new(11, [1], DungeonMapKind.Boss,
                    [new(DungeonPathDirection.Left, DungeonPathDirection.All)], false),
                900 when replacement != "missing" => new(900, [1], DungeonMapKind.Normal,
                    replacement switch
                    {
                        "pattern" => [new(DungeonPathDirection.All, DungeonPathDirection.All)],
                        "entrance" => [new(pattern, DungeonPathDirection.Right)],
                        "separate-pairs" => [new(pattern, DungeonPathDirection.Right),
                            new(DungeonPathDirection.All, DungeonPathDirection.Left)],
                        _ => [new(pattern, DungeonPathDirection.All)],
                    }, replaceStart),
                _ => null,
            };
            return ValueTask.FromResult(result);
        }
        public ValueTask<IReadOnlyList<DungeonMapDefinition>> ListForDungeonAsync(int id, CancellationToken token) =>
            ValueTask.FromResult<IReadOnlyList<DungeonMapDefinition>>([]);
    }

    [Fact]
    public async Task AnActiveQuestDoesNotUpgradeAnInteractiveOrdinaryEntry()
    {
        var service = Service(MixedQuestMazes());
        var facts = new DungeonEntryFacts(
            0,
            [1776],
            [],
            DungeonEntryIntent.Interactive);

        var layout = await service.ResolveAsync(144, facts, seed: 1, default);

        Assert.NotNull(layout);
        Assert.Equal(0, layout!.MazeIndex);
        Assert.Equal(DungeonRunPurpose.Ordinary, layout.Purpose);
    }

    [Fact]
    public async Task ADirectedEntryUsesTheEligibleActiveQuestMazeAndFreezesItsPurpose()
    {
        var service = Service(MixedQuestMazes());
        var facts = new DungeonEntryFacts(
            0,
            [1776],
            [],
            DungeonEntryIntent.Directed(144));

        var layout = await service.ResolveAsync(144, facts, seed: 1, default);

        Assert.NotNull(layout);
        Assert.Equal(1, layout!.MazeIndex);
        Assert.Equal(
            DungeonRunPurpose.QuestLinked(
                1776,
                DungeonQuestConnectionState.Active),
            layout.Purpose);
    }

    [Fact]
    public async Task ADirectedEntryWithConnectionsButNoEligibleQuestIsRefused()
    {
        var service = Service(MixedQuestMazes());
        var facts = new DungeonEntryFacts(
            0,
            [],
            [],
            DungeonEntryIntent.Directed(144));

        Assert.Null(await service.ResolveAsync(144, facts, seed: 1, default));
    }

    private static IDungeonStructureRuleService Service(DungeonDefinition definition) =>
        new DungeonStructureRuleService(new StubDungeonSource(definition), new StubMaps());

    [Theory]
    [InlineData(DungeonEntryMode.Tournament)]
    [InlineData(DungeonEntryMode.VillageRevenge)]
    public async Task Special_structure_is_queryable_but_does_not_start_through_the_ordinary_entry(DungeonEntryMode mode)
    {
        var definition = Tutorial() with { EntryMode = mode };
        var rules = Service(definition);
        Assert.Equal(definition, await rules.GetDefinitionAsync(definition.DungeonId, default));
        Assert.Null(await rules.ResolveAsync(definition.DungeonId, DungeonEntryFacts.None, 42, default));
    }

    private sealed class StubMaps : IDungeonMapDefinitionSource
    {
        public ValueTask<DungeonMapDefinition?> GetAsync(int mapId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<DungeonMapDefinition?>(new(mapId, [], DungeonMapKind.Normal,
                [new(DungeonPathDirection.All, DungeonPathDirection.All)], true));
        public ValueTask<IReadOnlyList<DungeonMapDefinition>> ListForDungeonAsync(int dungeonId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<DungeonMapDefinition>>([]);
    }

    private static DungeonDefinition Tutorial() =>
        new(10000, "dungeon/Tutorial/Tutorial.dgn", 1, false, [TutorialMaze]);

    private static DungeonDefinition ManyCandidates()
    {
        var rooms = new Dictionary<MazeCell, DungeonCellDefinition>();
        var candidates = new List<MazeCell>();
        for (byte x = 0; x < 5; x++)
        {
            rooms[new MazeCell(x, 0)] = FixedRoom(61000 + x);
            candidates.Add(new MazeCell(x, 0));
        }

        return new DungeonDefinition(
            1, "dungeon/Many.dgn", 1, true,
            [new MazeDefinition(0, 5, 1, rooms, candidates, candidates)]);
    }

    private static DungeonDefinition MixedQuestMazes()
    {
        MazeDefinition Maze(int index, DungeonQuestConnection? connection = null) =>
            new(
                index,
                1,
                1,
                new Dictionary<MazeCell, DungeonCellDefinition> { [new MazeCell(0, 0)] = FixedRoom(55600 + index)},
                [new MazeCell(0, 0)],
                [new MazeCell(0, 0)],
                connection);

        return new DungeonDefinition(
            144,
            "dungeon/mixed.dgn",
            1,
            true,
            [
                Maze(0),
                Maze(1, new DungeonQuestConnection(
                    DungeonQuestConnectionState.Active, 1776, -1)),
                Maze(2, new DungeonQuestConnection(
                    DungeonQuestConnectionState.Cleared, 1776, -1)),
            ]);
    }

    private sealed class StubDungeonSource(DungeonDefinition definition) : IDungeonDefinitionSource
    {
        public ValueTask<DungeonDefinition?> GetAsync(
            int dungeonId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(dungeonId == definition.DungeonId ? definition : null);
    }

    // Every candidate can be selected as the boss in the seed tests; provide an explicit boss
    // resource as well, rather than relying on an empty implicit pool the fixture cannot answer.
    private static DungeonCellDefinition FixedRoom(int id) =>
        DungeonCellDefinition.Fixed(id) with { BossMapCandidates = [id] };
}
