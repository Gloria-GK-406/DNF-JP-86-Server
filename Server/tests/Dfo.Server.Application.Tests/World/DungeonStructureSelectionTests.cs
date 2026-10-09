using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.World;

namespace Dfo.Server.Application.Tests.World;

public sealed class DungeonStructureSelectionTests
{
    private static readonly MazeCell Left = new(0, 0);
    private static readonly MazeCell Right = new(1, 0);
    private static readonly DungeonPathDirection Horizontal = DungeonPathDirection.Left | DungeonPathDirection.Right;

    [Fact]
    public async Task Implicit_maps_use_owner_kind_pattern_and_entrance_not_filename_coordinates()
    {
        var layout = Layout(new(Horizontal, [], []));
        var source = new Maps([
            Map(10, Horizontal, DungeonPathDirection.Right), // wrong entrance
            Map(11, DungeonPathDirection.All, DungeonPathDirection.Left), // wrong pattern
            Map(12, Horizontal, DungeonPathDirection.Left, DungeonMapKind.Boss),
            Map(13, Horizontal, DungeonPathDirection.Left),
            Map(14, Horizontal, DungeonPathDirection.Left, dungeonId: 2),
        ]);
        var selected = await Rules(source).SelectMapAsync(layout, Left, Right, default);
        Assert.Equal(13, selected.MapId);
        Assert.Null(selected.Failure);
    }

    [Fact]
    public async Task Explicit_candidates_are_filtered_by_entrance_and_all_valid_candidates_are_reachable()
    {
        var layout = Layout(new(Horizontal, [10, 11, 12], []));
        var rules = Rules(new Maps([
            Map(10, Horizontal, DungeonPathDirection.Left),
            Map(11, Horizontal, DungeonPathDirection.Left),
            Map(12, Horizontal, DungeonPathDirection.Right),
        ]));
        var selected = new HashSet<int>();
        for (var seed = 0; seed < 100; seed++)
            selected.Add((await rules.SelectMapAsync(layout with { Seed = seed }, Left, Right, default)).MapId!.Value);
        Assert.Equal(new HashSet<int> { 10, 11 }, selected);
        Assert.Equal(await rules.SelectMapAsync(layout, Left, Right, default),
            await rules.SelectMapAsync(layout, Left, Right, default));
    }

    [Fact]
    public async Task Explicit_candidates_require_the_full_pattern_and_entrance_in_the_same_pair()
    {
        var layout = Layout(new(Horizontal, [10, 11, 12], []));
        var wrongPattern = Map(10, DungeonPathDirection.All, DungeonPathDirection.Left);
        var differentPairs = Map(11, Horizontal, DungeonPathDirection.Right) with
        {
            Connections = [new(Horizontal, DungeonPathDirection.Right),
                new(DungeonPathDirection.All, DungeonPathDirection.Left)],
        };
        var rules = Rules(new Maps([wrongPattern, differentPairs, Map(12, Horizontal, DungeonPathDirection.Left)]));
        for (var seed = 0; seed < 30; seed++)
            Assert.Equal(12, (await rules.SelectMapAsync(layout with { Seed = seed }, Left, Right, default)).MapId);
    }

    [Fact]
    public async Task Explicit_start_filters_the_declared_pattern_without_requiring_an_entrance()
    {
        var layout = Layout(new(Horizontal, [10, 11], [])) with { StartCell = Right };
        var rules = Rules(new Maps([Map(10, DungeonPathDirection.All, DungeonPathDirection.All),
            Map(11, Horizontal, DungeonPathDirection.None)]));
        for (var seed = 0; seed < 30; seed++)
            Assert.Equal(11, (await rules.SelectMapAsync(layout with { Seed = seed }, null, Right, default)).MapId);
    }

    [Fact]
    public async Task Missing_declared_pattern_does_not_invent_a_four_door_requirement()
    {
        var layout = Layout(DungeonCellDefinition.Fixed(10));
        var rules = Rules(new Maps([Map(10, DungeonPathDirection.Left, DungeonPathDirection.Left)]));
        Assert.Equal(10, (await rules.SelectMapAsync(layout, Left, Right, default)).MapId);
    }

    [Fact]
    public async Task Incompatible_explicit_candidates_do_not_fall_back_to_the_implicit_pool()
    {
        var layout = Layout(new(Horizontal, [10], []));
        var rules = Rules(new Maps([Map(10, Horizontal, DungeonPathDirection.Right), Map(11, Horizontal, DungeonPathDirection.Left)]));
        var result = await rules.SelectMapAsync(layout, Left, Right, default);
        Assert.Null(result.MapId);
        Assert.Equal(DungeonMapSelectionFailure.NoCompatibleMap, result.Failure);
    }

    [Fact]
    public async Task Only_the_selected_boss_cell_uses_the_boss_candidate_list()
    {
        var layout = Layout(new(Horizontal, [10], [11]));
        var rules = Rules(new Maps([Map(10, Horizontal, DungeonPathDirection.Left),
            Map(11, Horizontal, DungeonPathDirection.Left, DungeonMapKind.Boss)]));
        Assert.Equal(10, (await rules.SelectMapAsync(layout, Left, Right, default)).MapId);
        Assert.Equal(11, (await rules.SelectMapAsync(layout with { BossCell = Right }, Left, Right, default)).MapId);
    }

    [Fact]
    public async Task Implicit_start_uses_start_area_pool_instead_of_the_ordinary_room_pool()
    {
        var layout = Layout(new(Horizontal, [], [])) with { StartCell = Right };
        var rules = Rules(new Maps([Map(10, Horizontal, Horizontal),
            Map(11, Horizontal, Horizontal) with { HasStartArea = true }]));
        Assert.Equal(11, (await rules.SelectMapAsync(layout, null, Right, default)).MapId);
    }

    [Fact]
    public async Task Boss_without_explicit_boss_candidates_uses_the_boss_pool_not_the_ordinary_binding()
    {
        var layout = Layout(new(Horizontal, [10], [])) with { BossCell = Right };
        var rules = Rules(new Maps([Map(10, Horizontal, DungeonPathDirection.Left),
            Map(11, Horizontal, DungeonPathDirection.Left, DungeonMapKind.Boss)]));
        Assert.Equal(11, (await rules.SelectMapAsync(layout, Left, Right, default)).MapId);
    }

    [Fact]
    public async Task An_unselected_boss_declaration_does_not_replace_the_ordinary_pool_with_dummy_maps()
    {
        var layout = Layout(new(Horizontal, [], [12]));
        var rules = Rules(new Maps([Map(10, Horizontal, DungeonPathDirection.Left),
            Map(11, Horizontal, DungeonPathDirection.Left, DungeonMapKind.Dummy),
            Map(12, Horizontal, DungeonPathDirection.Left, DungeonMapKind.Boss)]));
        Assert.Equal(10, (await rules.SelectMapAsync(layout, Left, Right, default)).MapId);
    }

    [Fact]
    public async Task Adjacent_cells_without_matching_doors_are_refused()
    {
        var layout = Layout(new(DungeonPathDirection.Right, [10], []));
        var result = await Rules(new Maps([Map(10, Horizontal, Horizontal)]))
            .SelectMapAsync(layout, Left, Right, default);
        Assert.Equal(DungeonMapSelectionFailure.Disconnected, result.Failure);
        Assert.False(layout.CanTraverse(Left, Right));
    }

    [Theory]
    [InlineData(1, 0, DungeonPathDirection.Left)]
    [InlineData(-1, 0, DungeonPathDirection.Right)]
    [InlineData(0, 1, DungeonPathDirection.Up)]
    [InlineData(0, -1, DungeonPathDirection.Down)]
    public void Entrance_is_the_side_of_the_destination_cell(int dx, int dy, DungeonPathDirection entrance) =>
        Assert.Equal(entrance, DungeonLayout.EntranceFrom(new(1, 1), new((byte)(1 + dx), (byte)(1 + dy))));

    private static DungeonLayout Layout(DungeonCellDefinition right) => new(1, 0, 2, 1, Left,
        new(9, 9), 1, 42, new Dictionary<MazeCell, DungeonCellDefinition>
        { [Left] = new(Horizontal, [1], []), [Right] = right });

    private static DungeonMapDefinition Map(int id, DungeonPathDirection pattern, DungeonPathDirection entrance,
        DungeonMapKind kind = DungeonMapKind.Normal, int dungeonId = 1) => new(id, [dungeonId], kind, [new(pattern, entrance)], false);

    private static DungeonStructureRuleService Rules(Maps maps) => new(new Definitions(), maps);
    private sealed class Definitions : IDungeonDefinitionSource
    {
        public ValueTask<DungeonDefinition?> GetAsync(int dungeonId, CancellationToken cancellationToken) => ValueTask.FromResult<DungeonDefinition?>(null);
    }
    private sealed class Maps(IReadOnlyList<DungeonMapDefinition> values) : IDungeonMapDefinitionSource
    {
        public ValueTask<DungeonMapDefinition?> GetAsync(int mapId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(values.SingleOrDefault(value => value.MapId == mapId));
        public ValueTask<IReadOnlyList<DungeonMapDefinition>> ListForDungeonAsync(int dungeonId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<DungeonMapDefinition>>(values.Where(value => value.DungeonIds.Contains(dungeonId)).ToArray());
    }
}
