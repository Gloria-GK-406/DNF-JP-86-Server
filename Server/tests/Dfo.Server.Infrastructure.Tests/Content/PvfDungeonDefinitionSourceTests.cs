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
    private const int SwordmanTutorialDungeonId = 10004;

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
    /// The swordman tutorial: the dungeon a level-1 swordman is pulled into on first entering
    /// town. It matters here for one word: its boss room is
    /// declared <c>boss 2 0 35038</c>, not <c>map 2 0 35038</c>. Dungeon 10000 above spells every
    /// room <c>map</c>, so it cannot tell whether the reader handles the other kind -- and the
    /// reader did not. The boss cell carried no room, the layout still named it, and the player
    /// reached the last door with nowhere on the other side of it.
    ///
    /// The room after the misread one is the second half of the same defect: the skip used to run
    /// to the end of the section. Here <c>boss</c> is last so only it was lost, which is why the
    /// count below (eight, not seven) is the assertion that pins both halves.
    /// </remarks>
    [SkippableFact]
    public async Task The_swordman_tutorial_declares_its_boss_room_with_the_other_room_keyword()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfDungeonDefinitionSource(provider);

        var dungeon = await source.GetAsync(SwordmanTutorialDungeonId, CancellationToken.None);

        Assert.NotNull(dungeon);
        Assert.Equal("dungeon/Tutorial/Tutorial_knight.dgn", dungeon!.ArchivePath);
        var maze = Assert.Single(dungeon.Mazes);
        Assert.Equal(5, maze.Width);
        Assert.Equal(3, maze.Height);
        Assert.Equal(
            new Dictionary<MazeCell, int>
            {
                [new MazeCell(0, 1)] = 35031,
                [new MazeCell(1, 1)] = 35032,
                [new MazeCell(2, 1)] = 35033,
                [new MazeCell(3, 1)] = 35034,
                [new MazeCell(4, 1)] = 35036,
                [new MazeCell(3, 2)] = 35035,
                [new MazeCell(3, 0)] = 35037,
                [new MazeCell(2, 0)] = 35038,
            },
            maze.Rooms);
        Assert.Equal([new MazeCell(0, 1)], maze.StartCandidates);
        Assert.Equal([new MazeCell(2, 0)], maze.BossCandidates);
        Assert.All(
            maze.StartCandidates.Concat(maze.BossCandidates),
            cell => Assert.True(maze.Rooms.ContainsKey(cell), $"cell {cell} carries no room"));
    }

    /// <summary>
    /// A dungeon's own experience weight and basis level, which price every kill inside it.
    /// </summary>
    /// <remarks>
    /// Dungeon 144 is the one the 2026-08-27 kill captures were taken in, so these two numbers are
    /// load-bearing for every award those captures check. The weight is written with a decimal
    /// point and therefore stored as a single; read through the integer path it would arrive as the
    /// bit pattern 1069967704 and price a kill a billion times over.
    ///
    /// The basis level matters whenever a map requests a relative monster level. The captured
    /// run included level-5 monsters; their level cannot be inferred from this dungeon field alone.
    /// </remarks>
    [SkippableFact]
    public async Task ADungeonDeclaresWhatItsKillsAreWorthAndWhatLevelItsMonstersAre()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfDungeonDefinitionSource(provider);

        var dungeon = await source.GetAsync(144, CancellationToken.None);

        Assert.NotNull(dungeon);
        Assert.Equal((double)1.45f, dungeon!.ExperienceWeight);
        Assert.Equal(5, dungeon.BasisLevel);
    }

    [SkippableFact]
    public async Task Dungeon144PreservesTheQuest1776MazeConnection()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfDungeonDefinitionSource(provider);

        var dungeon = await source.GetAsync(144, CancellationToken.None);

        Assert.NotNull(dungeon);
        var connected = Assert.Single(
            dungeon!.Mazes,
            maze => maze.QuestConnection == new DungeonQuestConnection(
                DungeonQuestConnectionState.Active, 1776, -1));
        Assert.Equal(1, connected.Index);
        Assert.Contains(dungeon.Mazes, maze => maze.QuestConnection is null);
    }

    [Fact]
    public void AQuestConnectionWithoutDifficultyAppliesToEveryDifficulty()
    {
        var connection = PvfDungeonDefinitionSource.ProjectQuestConnection([0, 1776]);

        Assert.Equal(
            new DungeonQuestConnection(DungeonQuestConnectionState.Active, 1776, -1),
            connection);
    }

    [Theory]
    [InlineData()]
    [InlineData(0)]
    [InlineData(0, 1776, -1, 9)]
    public void UnknownQuestConnectionShapesAreNotProjected(params int[] values)
    {
        Assert.Null(PvfDungeonDefinitionSource.ProjectQuestConnection(values));
    }

    /// <summary>A weight written as a whole number rather than as a decimal.</summary>
    /// <remarks>
    /// Both forms are real, and reading only the decimal one left every dungeon using the other at
    /// the default of 1. The four below are the ones checked by hand, and they are the largest
    /// under-payments it caused: <c>port.dgn</c> and <c>mainbridge.dgn</c> were paying a thirtieth
    /// of what they are worth, <c>circus_new.dgn</c> a fifteenth and <c>circus.dgn</c> a
    /// thirty-fifth. A comment saying the tag was never written that way made the omission look
    /// deliberate.
    /// <para>
    /// Every case here is a genuine <c>Int32</c> in the archive, verified token by token. That
    /// matters more than it looks: <c>bakalcastle.dgn</c> also declares 4 and would have seemed to
    /// belong here, but it writes it as a single and so exercises the branch that already worked.
    /// A pin that lands on the wrong branch says nothing about the fix it was written for.
    /// </para>
    /// </remarks>
    [SkippableTheory]
    [InlineData(4103, 35.0)]
    [InlineData(4108, 30.0)]
    [InlineData(4114, 15.0)]
    [InlineData(4109, 9.0)]
    public async Task AWeightWrittenAsAWholeNumberIsReadToo(int dungeonId, double weight)
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfDungeonDefinitionSource(provider);

        var dungeon = await source.GetAsync(dungeonId, CancellationToken.None);

        Assert.NotNull(dungeon);
        Assert.Equal(weight, dungeon!.ExperienceWeight);
    }

    /// <summary>A dungeon declaring no experience at all pays none.</summary>
    /// <remarks>
    /// Zero is a value, not an absence. Two dungeons declare it -- 6001 and 6005, both minigames --
    /// and reading it as invalid left them at the default of 1, paying in full where the archive
    /// says they pay nothing. Only a tag that is absent takes the default.
    /// </remarks>
    [SkippableFact]
    public async Task ADungeonDeclaringNoExperienceGetsNone()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfDungeonDefinitionSource(provider);

        var dungeon = await source.GetAsync(6001, CancellationToken.None);

        Assert.NotNull(dungeon);
        Assert.Equal(0.0, dungeon!.ExperienceWeight);
    }

    /// <summary>A weight below one, which the tutorial has and which pays a tenth.</summary>
    /// <remarks>
    /// The weight is not a bonus that dungeons may only add: the tutorial declares 0.1 and so pays
    /// a tenth of what its monsters' levels are worth. Asserted because a reader who met only
    /// dungeon 144's 1.45 would reasonably assume the field never reduces, and clamping it at one
    /// would make the tutorial pay ten times over.
    /// </remarks>
    [SkippableFact]
    public async Task ADungeonsWeightCanPayLessThanAMonsterIsWorth()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfDungeonDefinitionSource(provider);

        var dungeon = await source.GetAsync(TutorialDungeonId, CancellationToken.None);

        Assert.Equal((double)0.1f, dungeon!.ExperienceWeight);
        Assert.Equal(1, dungeon.BasisLevel);
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
