using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;
namespace Dfo.Server.Application.Tests.World;

public sealed class ExtraDropRuleServiceTests
{
    private static MonsterDropRequest Request() => new(1, new(DungeonRunId.New(), 144, 23,
        new MazeCell(0, 0), 8153, DungeonDifficulty.Normal, 1, 20, 0, new CharacterId(1),
        [new(new AccountId(1), new CharacterId(1))]));
    private static ExtraDropContent Content() => new(
        new Dictionary<(DropItemKind, int, ExtraDropDungeonType), DropItemPool> {
            [(DropItemKind.Equipment, 0, ExtraDropDungeonType.Normal)] = new(DropItemFamily.Equipment,
                new Dictionary<(int, int), IReadOnlyList<DropPoolEntry>> {
                    [(20, 0)] = [new(7001, 10, 30)] }) },
        new Dictionary<int, ExtraDropDungeonType> { [144] = ExtraDropDungeonType.Normal }, 1);
    private static DropRateTable Table() => new(DropRateTables.MonsterExtra,
        [new(1, 100, [10000, 0, 10000, 0, 0])],
        [new([10, 11]), new([10, 11]), new([10, 11]), new([10, 11])],
        [], [], [], new Dictionary<int, ItemGradeBand> { [20] = new(19, 21) });
    private static ExtraDropRuleService Service(ExtraDropContent? content = null, DropRateTable? table = null) =>
        new(new Tables(table ?? Table()), new Source(content ?? Content()), new DropRateOptions());

    [Fact]
    public void Tagged_equipment_can_drop_and_carries_durability_without_extra_gold()
    {
        var service = Service();
        var request = Request();
        Assert.Equal(1, service.Initialize());
        Assert.Equal(new RolledDrop(7001, 1, DropRarity.Common, DropItemKind.Equipment, 30),
            Assert.Single(service.Draw(request, 0)));
        Assert.Equal(service.Draw(request, 0), service.Draw(request, 0));
    }
    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public void Difficulty_masks_do_not_leak(int difficulty)
    {
        var request = Request();
        Assert.Empty(Service().Draw(request with { Context = request.Context with {
            Difficulty = (DungeonDifficulty)difficulty } }, 0));
    }
    [Fact]
    public void Dungeon_type_masks_do_not_leak() => Assert.Empty(Service(Content() with {
        Dungeons = new Dictionary<int, ExtraDropDungeonType> { [144] = ExtraDropDungeonType.Ancient }
    }).Draw(Request(), 0));
    [Fact]
    public void Empty_candidates_and_zero_rates_are_real_misses()
    {
        Assert.Empty(Service(Content() with { Pools = new Dictionary<(DropItemKind, int, ExtraDropDungeonType), DropItemPool>() }).Draw(Request(), 0));
        Assert.Empty(Service(table: Table() with { LevelBands = [new(1, 100, [0, 0, 0, 0, 0])] }).Draw(Request(), 0));
        Assert.Empty(Service(table: Table() with { GradeBands = new Dictionary<int, ItemGradeBand> { [20] = new(30, 40) } }).Draw(Request(), 0));
    }
    [Fact]
    public void Unknown_dungeon_is_not_silently_treated_as_normal() => Assert.Throws<InvalidDataException>(() =>
        Service(Content() with { Dungeons = new Dictionary<int, ExtraDropDungeonType>() }).Draw(Request(), 0));
    private sealed class Tables(DropRateTable table) : IDropRateTableSource { public DropRateTable Load(string path) => table; }
    private sealed class Source(ExtraDropContent content) : IExtraDropContentSource { public ExtraDropContent Load() => content; }
}
