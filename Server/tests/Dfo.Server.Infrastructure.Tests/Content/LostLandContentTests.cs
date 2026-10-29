using Dfo.Server.Abstractions.Content;
using Dfo.Pvf.Schema;
using Dfo.Pvf.Tokens;
using Dfo.Server.Infrastructure.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

public sealed class LostLandContentTests
{
    [SkippableFact]
    public async Task Consumables_keep_native_capsule_destinations_and_transformation_room_restrictions()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfLostLandConsumableDefinitionSource(provider);
        (int Item, byte X, byte Y)[] capsules = [(10093342, 9, 6), (10093343, 6, 7),
            (10093344, 5, 5), (10093345, 4, 5), (10093346, 4, 2)];
        foreach (var (item, x, y) in capsules)
        {
            var definition = await source.GetAsync(item, default);
            Assert.NotNull(definition);
            Assert.Equal(Dfo.Server.Abstractions.World.LostLandConsumableKind.Teleport, definition.Kind);
            Assert.Equal(new MazeCell(x, y), definition.Destination);
        }
        var transform = await source.GetAsync(10092741, default);
        Assert.NotNull(transform);
        Assert.Equal(Dfo.Server.Abstractions.World.LostLandConsumableKind.Transformation, transform.Kind);
        Assert.Null(transform.Destination);
        Assert.Contains(11200, transform.DisabledMaps);
        Assert.DoesNotContain(38033, transform.DisabledMaps);
        var alternate = await source.GetAsync(10092764, default);
        Assert.NotNull(alternate);
        Assert.Equal(Dfo.Server.Abstractions.World.LostLandConsumableKind.Transformation, alternate.Kind);
        Assert.Null(alternate.Destination);
        Assert.DoesNotContain(38043, alternate.DisabledMaps);
        Assert.Contains(38045, alternate.DisabledMaps);
        Assert.Null(await source.GetAsync(2000, default));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(-2, -2)]
    [InlineData(10, 0)]
    [InlineData(0, 10)]
    [InlineData(256, 0)]
    public void Warp_source_rejects_partial_wildcard_negative_and_out_of_bounds_coordinates(int x, int y) =>
        Assert.Throws<InvalidDataException>(() => PvfLostLandContentSource.ReadWarps(
            [Section("[warp map condition]"), Section("[source grid pos]", x, y),
             Section("[dest grid pos]", 2, 3), Section("[/warp map condition]")], 10, 10));

    [Fact]
    public void Wildcard_is_preserved_as_a_declaration_and_does_not_erase_same_source_destinations()
    {
        var warps = PvfLostLandContentSource.ReadWarps(
            [Section("[warp map condition]"), Section("[source grid pos]", 1, 2), Section("[dest grid pos]", 2, 3),
             Section("[source grid pos]", 1, 2), Section("[dest grid pos]", 4, 5),
             Section("[source grid pos]", -1, -1), Section("[dest grid pos]", 9, 6), Section("[/warp map condition]")], 10, 10);
        Assert.Equal(3, warps.Length);
        Assert.Equal(new MazeCell(1, 2), warps[0].Source);
        Assert.Equal(new MazeCell(1, 2), warps[1].Source);
        Assert.Equal(new MazeCell(2, 3), warps[0].Target);
        Assert.Equal(new MazeCell(4, 5), warps[1].Target);
        Assert.Null(warps[2].Source);
        Assert.Equal(new MazeCell(9, 6), warps[2].Target);
    }

    [Fact]
    public void Truncated_misordered_or_unclosed_warps_are_refused_instead_of_synthesizing_a_target()
    {
        PvfSection[][] malformed =
        [
            [Section("[warp map condition]"), Section("[source grid pos]", 1, 2), Section("[/warp map condition]")],
            [Section("[warp map condition]"), Section("[dest grid pos]", 1, 2), Section("[/warp map condition]")],
            [Section("[warp map condition]"), Section("[source grid pos]", 1), Section("[dest grid pos]", 2, 3), Section("[/warp map condition]")],
            [Section("[warp map condition]"), Section("[source grid pos]", 1, 2), Section("[dest grid pos]", 2, 3)],
            [Section("[warp map condition]"), Section("[source grid pos]", 1, 2), Section("[dest grid pos]", -1, -1), Section("[/warp map condition]")],
        ];
        foreach (var sections in malformed)
            Assert.Throws<InvalidDataException>(() => PvfLostLandContentSource.ReadWarps(sections, 10, 10));
    }

    private static PvfSection Section(string name, params int[] integers) =>
        new(name, integers.Select(value => new PvfToken(PvfTokenKind.Int32, value)).ToArray());

    [SkippableFact]
    public async Task Current_content_keeps_exploration_warps_and_patrols_including_wildcard_and_multiple_destinations()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var content = await new PvfLostLandContentSource(provider, new PvfDungeonDefinitionSource(provider)).ReadAsync(default);
        Assert.Equal(4000, content.Dungeon.DungeonId);
        Assert.Equal(25, content.Dungeon.MinimumRequiredLevel);
        Assert.False(content.Dungeon.ConsumesFatigue);
        Assert.True(content.Dungeon.NoGiveupPenalty);
        Assert.Equal(1, content.Dungeon.PartyLimit);
        Assert.True(content.Dungeon.ProhibitsPractice);
        Assert.Equal(new MazeCell(9, 6), Assert.Single(Assert.Single(content.Dungeon.Mazes).StartCandidates));
        Assert.Equal(1, content.BaseEntryCount);
        Assert.Equal(new[] { 60, 85, 10, 30, 55, 75, 75, -80 }, content.FeverParameters);
        Assert.Equal(120000, content.FirstPeriodMilliseconds);
        Assert.Equal(120000, content.SecondPeriodMilliseconds);
        Assert.Equal(new[] { 600, 1200 }, content.LimitTimeParameters);
        Assert.Equal(50, content.WarpConditions.Length);
        Assert.Equal(5, content.WarpConditions.Count(w => w.Source is null));
        Assert.Contains(content.WarpConditions, w => w.Source == new MazeCell(1, 2) && w.Target == new MazeCell(0, 2));
        Assert.Contains(content.WarpConditions, w => w.Source == new MazeCell(1, 2) && w.Target == new MazeCell(3, 4));
        Assert.Contains(content.WarpConditions, w => w.Source == new MazeCell(1, 2) && w.Target == new MazeCell(7, 0));
        Assert.Equal(38, content.Patrols.Length);
        Assert.All(content.Patrols, p =>
        {
            Assert.Equal(8000, p.MoveTermMilliseconds);
            Assert.Equal("night", p.DayCondition);
            Assert.Equal(0, p.LoopFlag);
            Assert.Equal(1, p.MeetCondition);
            Assert.NotEmpty(p.Route);
        });
    }
}
