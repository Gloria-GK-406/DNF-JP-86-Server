using Dfo.Server.Abstractions.Content;
using Dfo.Server.Infrastructure.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

/// <summary>
/// What a room's breakable objects are declared to yield.
/// </summary>
/// <remarks>
/// The tutorial covers both answers and is the reason this exists: its second room holds one
/// breakable object with an item in it, and its first holds none. A room told to yield nothing
/// when it should yield something stops the run there -- the room clears, the scripted pause the
/// step opens is never resumed, and nothing else on the wire looks wrong.
/// </remarks>
public sealed class PvfDungeonRoomDropSourceTests
{
    private const int TutorialDungeon = 10000;
    private const int DemonicTutorialDungeon = 10005;

    [SkippableFact]
    public async Task The_tutorials_second_room_yields_the_shoulder_its_dungeon_table_names()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfDungeonRoomDropSource(provider);

        var drops = await source.GetAsync(TutorialDungeon, 61001, CancellationToken.None);

        // The room names group zero; the dungeon's table gives that group item 14400, whose own
        // definition declares durability 25. A working A21 session's equivalent room sends exactly
        // this item.
        Assert.Equal([new DungeonRoomDrop(0, 14400, 25)], drops);
    }

    [SkippableFact]
    public async Task A_room_with_nothing_breakable_yields_nothing()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfDungeonRoomDropSource(provider);

        // The tutorial's own first room, and the demonic tutorial's first two -- the rooms a
        // working A21 session walked through while its server sent no drops at all.
        Assert.Empty(await source.GetAsync(TutorialDungeon, 61000, CancellationToken.None));
        Assert.Empty(await source.GetAsync(DemonicTutorialDungeon, 61030, CancellationToken.None));
        Assert.Empty(await source.GetAsync(DemonicTutorialDungeon, 61031, CancellationToken.None));
    }

    /// <remarks>
    /// The demonic tutorial's third room is the one a working A21 session did send a drop for, and
    /// the item it sent was 14400. Both tutorials reach the same item through their own tables,
    /// which is what makes this a check on the lookup rather than on one file.
    /// </remarks>
    [SkippableFact]
    public async Task The_demonic_tutorials_third_room_reaches_the_same_item_through_its_own_table()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfDungeonRoomDropSource(provider);

        var drops = await source.GetAsync(DemonicTutorialDungeon, 61032, CancellationToken.None);

        Assert.Equal([new DungeonRoomDrop(0, 14400, 25)], drops);
    }

    [SkippableFact]
    public async Task A_dungeon_or_map_the_archive_does_not_declare_yields_nothing()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfDungeonRoomDropSource(provider);

        Assert.Empty(await source.GetAsync(TutorialDungeon, int.MaxValue, CancellationToken.None));
        Assert.Empty(await source.GetAsync(int.MaxValue, 61001, CancellationToken.None));
    }
}
