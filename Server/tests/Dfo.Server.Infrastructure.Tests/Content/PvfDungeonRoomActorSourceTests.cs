using Dfo.Server.Abstractions.Content;
using Dfo.Server.Infrastructure.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

/// <summary>
/// What a room is declared to hold.
/// </summary>
/// <remarks>
/// The tutorial's rooms are used because they are the ones the entry path walks, and because
/// between them they cover both shapes the block takes: a single monster beside an AI character,
/// and a run of several monsters with no AI character at all. A reader that stopped after the
/// first record would pass the first and fail the rest, which is the mistake worth pinning.
/// </remarks>
public sealed class PvfDungeonRoomActorSourceTests
{
    private const int TutorialStartMap = 61000;
    private const int TutorialSecondMap = 61001;
    private const int TutorialThirdMap = 61002;
    private const int TutorialBossMap = 61005;

    [SkippableFact]
    public async Task Gent_south_gate_cycle_boss_and_enemy_APCs_preserve_rank_and_allegiance()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfDungeonRoomActorSource(provider);
        var actors = await source.GetAsync(57323, default);
        Assert.Equal(7, actors.Count);
        Assert.All(actors, actor => Assert.True(actor.IsHostile));
        var boss = Assert.Single(actors, actor => actor.IsBoss);
        Assert.Equal(14003, boss.Code);
        Assert.Equal(8, boss.ActorType);
        Assert.Equal(58, boss.Level);
        Assert.All(actors.Where(actor => !actor.IsBoss), actor =>
        {
            Assert.Equal(10610, actor.Code);
            Assert.Equal(5, actor.ActorType);
        });
        var guide = Assert.Single(await source.GetAsync(TutorialStartMap, default), actor => actor.ActorType >= 5);
        Assert.False(guide.IsHostile);
        Assert.False(guide.IsBoss);
    }

    [SkippableFact]
    public async Task The_tutorials_first_room_holds_one_monster_and_its_guide()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfDungeonRoomActorSource(provider);

        var actors = await source.GetAsync(TutorialStartMap, CancellationToken.None);

        // Monsters first, then the AI characters. A working A21 session sends exactly these two,
        // in this order, and the guide's level is the one its own definition declares -- the map
        // says nothing about it.
        Assert.Equal(
            [
                new DungeonRoomActor(61670, 0, 0, BasisLevelOffset: 0), // 相对副本基础等级
                new DungeonRoomActor(461, 80, 5),   // 阿甘左, [ai character]
            ],
            actors.Select(actor => actor with { Declaration = null }));
    }

    [SkippableFact]
    public async Task A_block_declaring_several_monsters_yields_all_of_them()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfDungeonRoomActorSource(provider);

        var second = await source.GetAsync(TutorialSecondMap, CancellationToken.None);
        var third = await source.GetAsync(TutorialThirdMap, CancellationToken.None);
        var boss = await source.GetAsync(TutorialBossMap, CancellationToken.None);

        Assert.Equal([61670, 61359, 61670, 61359], second.Select(actor => actor.Code));
        Assert.Equal([61356, 61356, 61356, 61356, 61356], third.Select(actor => actor.Code));
        Assert.Equal([62527, 61357, 61360, 61361], boss.Select(actor => actor.Code));
        Assert.All(
            second.Concat(third).Concat(boss),
            actor => Assert.True(actor.ActorType <= 3, $"{actor.Code} is not a monster"));
    }

    [SkippableFact]
    public async Task A_map_the_archive_does_not_declare_holds_nothing()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfDungeonRoomActorSource(provider);

        Assert.Empty(await source.GetAsync(int.MaxValue, CancellationToken.None));
    }
}
