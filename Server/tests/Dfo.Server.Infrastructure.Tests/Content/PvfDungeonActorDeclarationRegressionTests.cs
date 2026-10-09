using Dfo.Server.Infrastructure.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

public sealed class PvfDungeonActorDeclarationRegressionTests
{
    [SkippableFact]
    public async Task Counter_attack_boss_room_retains_all_five_map_monster_indices()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfDungeonRoomActorSource(provider);
        var actors = await source.GetAsync(22036, default);
        Assert.Equal([61150, 61305, 61305, 61305, 64639], actors.Select(actor => actor.Code));
        Assert.Equal(61150, Assert.Single(actors, actor => actor.IsBoss && actor.IsHostile).Code);
        Assert.True(actors[4].Declaration!.IsDummy);
        Assert.False(actors[4].IsHostile);
    }

    [SkippableFact]
    public async Task Gent_defence_declares_its_boss_and_all_thirty_object_monster_templates()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfDungeonRoomActorSource(provider);
        var actors = await source.GetAsync(21314, default);
        Assert.Equal(31, actors.Count);
        Assert.Equal(10409, actors[0].Code);
        Assert.Equal(8, actors[0].ActorType);
        Assert.Equal(30, actors.Count(actor => actor.ActorType <= 3));
        var templates = actors.Skip(1).Select(actor => actor.Declaration!).ToArray();
        Assert.Equal(Enumerable.Range(0, 9), templates.Select(row => row.ParentObjectIndex!.Value).Distinct());
        Assert.All(templates, row => Assert.True(row.IsSceneOwnedWave));
        Assert.Equal(Enumerable.Range(0, 4), templates.Take(4).Select(row => row.ChildIndex!.Value));
    }
}
