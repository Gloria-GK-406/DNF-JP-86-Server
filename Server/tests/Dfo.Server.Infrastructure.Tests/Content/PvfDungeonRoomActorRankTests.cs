using Dfo.Server.Infrastructure.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

/// <summary>
/// What rank a room's monsters are, and what level, as the archive declares them.
/// </summary>
/// <remarks>
/// Both were read out of the wrong place until 2026-08-27, and both were reported live. A monster
/// record is <c>code, level, autoLevel, x, y, z, randomDrops, fixedDrops</c> and then two quoted
/// words -- where it stands and what rank it is. The rank was being taken from the third INTEGER,
/// which is the auto-level; dungeon 144 sets that to 3 and 4 on monsters it calls
/// <c>[normal]</c>, and 3 is the number the client draws a boss by. The player met ordinary
/// monsters wearing boss health bars.
///
/// The level was the same record read the same way: dungeon 144 declares zero on every monster,
/// which means "use the dungeon's basis level" and was being read as a literal zero and clamped
/// to one.
/// </remarks>
public sealed class PvfDungeonRoomActorRankTests
{
    /// <summary>The room the player met the false bosses in.</summary>
    private const int Dungeon144NormalRoomMapId = 8153;

    /// <summary>Its neighbour, which mixes auto-levels 3 and 4 across monsters of one rank.</summary>
    private const int Dungeon144SecondRoomMapId = 8154;

    /// <summary>The A21 tutorial's boss room, whose monsters are also all ordinary.</summary>
    private const int TutorialBossRoomMapId = 61005;

    [SkippableFact]
    public async Task MonstersTheMapCallsNormalAreReportedAsOrdinary()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfDungeonRoomActorSource(provider);

        var actors = await source.GetAsync(Dungeon144NormalRoomMapId, CancellationToken.None);

        Assert.NotEmpty(actors);
        Assert.All(actors, actor => Assert.Equal(0, actor.ActorType));
    }

    /// <remarks>
    /// The clinching case. Every monster in this room is <c>[normal]</c>, and the auto-levels
    /// differ between them -- 3 on three of them and 4 on two. Read as a rank that is "three
    /// bosses and two ordinary monsters"; read correctly it is five ordinary monsters. A test that
    /// only checked one room could pass on a room where auto-level happens to be zero.
    /// </remarks>
    [SkippableFact]
    public async Task OneRanksWorthOfMonstersIsNotSplitByTheirAutoLevels()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfDungeonRoomActorSource(provider);

        var actors = await source.GetAsync(Dungeon144SecondRoomMapId, CancellationToken.None);

        Assert.Equal(5, actors.Count);
        Assert.All(actors, actor => Assert.Equal(0, actor.ActorType));
        Assert.All(actors, actor => Assert.Equal(63751, actor.Code));
    }

    /// <summary>The tutorial's boss room holds four monsters, and none of them is a boss.</summary>
    /// <remarks>
    /// Which is not a contradiction: the cell is the boss cell, and what stands in it is four
    /// ordinary monsters. The rank is the monster's, not the room's -- and the room being the boss
    /// room is already carried by the layout.
    /// </remarks>
    [SkippableFact]
    public async Task TheBossRoomsMonstersAreOrdinaryToo()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfDungeonRoomActorSource(provider);

        var actors = await source.GetAsync(TutorialBossRoomMapId, CancellationToken.None);

        Assert.Equal([62527, 61357, 61360, 61361], actors.Select(actor => actor.Code));
        Assert.All(actors, actor => Assert.Equal(0, actor.ActorType));
    }

    /// <summary>
    /// A level the map does not set comes back as zero, for the caller to fill from the dungeon.
    /// </summary>
    /// <remarks>
    /// Passed through rather than defaulted here on purpose: a map belongs to no one dungeon, so
    /// this source cannot know the basis level, and inventing a one would be an answer that looks
    /// like the archive's.
    /// </remarks>
    [SkippableFact]
    public async Task AnUndeclaredLevelIsReportedAsAbsentRatherThanAsOne()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfDungeonRoomActorSource(provider);

        var declared = await source.GetAsync(TutorialBossRoomMapId, CancellationToken.None);
        var undeclared = await source.GetAsync(Dungeon144NormalRoomMapId, CancellationToken.None);

        Assert.All(declared, actor => Assert.Equal(1, actor.Level));
        Assert.All(undeclared, actor => Assert.Equal(0, actor.Level));
    }
}
