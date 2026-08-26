using Dfo.Server.Abstractions.Content;
using Dfo.Server.Infrastructure.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

/// <summary>
/// The tutorial dungeon as the archive declares it.
/// </summary>
/// <remarks>
/// Dungeon 10000 is the one the client asks for unprompted on a level-1 character, so it is the
/// dungeon the entry path has to work for first. It is also the plainest member of the maze family
/// -- one maze, no difficulty, no event monsters, no party tags -- which is what makes it usable
/// as a fixed expectation here.
/// </remarks>
public sealed class PvfDungeonDefinitionSourceTests
{
    private const int TutorialDungeonId = 10000;

    [SkippableFact]
    public async Task The_tutorial_dungeon_reads_as_one_maze_on_a_three_by_two_grid()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfDungeonDefinitionSource(provider);

        var dungeon = await source.GetAsync(TutorialDungeonId, CancellationToken.None);

        Assert.NotNull(dungeon);
        Assert.Equal("dungeon/Tutorial/Tutorial.dgn", dungeon!.ArchivePath);
        Assert.Equal(1, dungeon.MinimumRequiredLevel);
        Assert.False(dungeon.ConsumesFatigue);

        var maze = Assert.Single(dungeon.Mazes);
        Assert.Equal(0, maze.Index);
        Assert.Equal(3, maze.Width);
        Assert.Equal(2, maze.Height);
    }

    /// <remarks>
    /// The four rooms and their map identifiers, spelled out. The start and boss cells both carry
    /// a room, which is the property the layout depends on when it resolves the room a run opens
    /// in -- a start cell with no room would put the character nowhere.
    ///
    /// A21's tutorial is a different dungeon from the older client's, not the same one read
    /// differently: three by two with four rooms against five by three with seven, and the boss in
    /// a different cell. Anything derived from the older layout -- a room count, a path, a map
    /// identifier -- does not carry over.
    /// </remarks>
    [SkippableFact]
    public async Task The_tutorial_maze_declares_four_rooms_including_its_start_and_boss_cells()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfDungeonDefinitionSource(provider);

        var maze = (await source.GetAsync(TutorialDungeonId, CancellationToken.None))!.Mazes[0];

        Assert.Equal(
            new Dictionary<MazeCell, int>
            {
                [new MazeCell(0, 1)] = 61000,
                [new MazeCell(1, 1)] = 61001,
                [new MazeCell(2, 1)] = 61002,
                [new MazeCell(2, 0)] = 61005,
            },
            maze.Rooms);
        Assert.Equal([new MazeCell(0, 1)], maze.StartCandidates);
        Assert.Equal([new MazeCell(2, 0)], maze.BossCandidates);
        Assert.All(
            maze.StartCandidates.Concat(maze.BossCandidates),
            cell => Assert.True(maze.Rooms.ContainsKey(cell), $"cell {cell} carries no room"));
    }

    /// <remarks>
    /// An absent dungeon and a dungeon the archive declares without a maze answer the same way, on
    /// purpose: neither can be entered by the maze path, and a caller that had to tell them apart
    /// would be deciding structural family from a null check.
    /// </remarks>
    [SkippableFact]
    public async Task A_dungeon_the_archive_does_not_declare_is_absent()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfDungeonDefinitionSource(provider);

        Assert.Null(await source.GetAsync(999_999, CancellationToken.None));
    }

    /// <remarks>
    /// Tower of Death carries no <c>[maze info]</c> at all -- it uses <c>[death tower map
    /// indexes]</c> instead -- so it is the archive's own example of the second structural family.
    /// </remarks>
    [SkippableFact]
    public async Task A_dungeon_with_no_maze_is_absent_rather_than_a_dungeon_with_no_rooms()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfDungeonDefinitionSource(provider);

        Assert.Null(await source.GetAsync(11000, CancellationToken.None));
    }

    [SkippableFact]
    public async Task Repeated_reads_answer_from_one_parse()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfDungeonDefinitionSource(provider);

        var first = await source.GetAsync(TutorialDungeonId, CancellationToken.None);
        var second = await source.GetAsync(TutorialDungeonId, CancellationToken.None);

        Assert.Same(first, second);
    }
}
