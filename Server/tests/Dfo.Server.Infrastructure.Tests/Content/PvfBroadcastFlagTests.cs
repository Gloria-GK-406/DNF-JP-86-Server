using Dfo.Pvf.Tests.Support;
using Dfo.Server.Infrastructure.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

/// <summary>The archive's own announcement marks: a pool row's fourth column and a recipe's [broadcast type].</summary>
public sealed class PvfBroadcastFlagTests
{
    [SkippableFact]
    public async Task A_magic_box_row_carries_its_fourth_column_as_the_broadcast_flag()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfPackageOpeningDefinitionSource(provider,
            new PvfCeraShopDefinitionSource(provider), new PvfItemPlacementDefinitionSource(provider));

        // stackable/ect/chn_random/chn_amazingbox.stk
        var box = await source.GetAsync(200, default);

        Assert.NotNull(box);
        var rows = box.Pools.SelectMany(pool => pool.Choices).ToArray();
        // Rows as declared: item, weight, count, flag.
        Assert.Contains(rows, row => row.Reward is { ItemId: 3, Count: 1, Announce: true } && row.Weight == 100);
        Assert.Contains(rows, row => row.Reward is { ItemId: 421, Count: 100, Announce: true } && row.Weight == 1000);
        Assert.Contains(rows, row => row.Reward is { ItemId: 421, Count: 5, Announce: false } && row.Weight == 25000);
    }

    [SkippableFact]
    public async Task A_requiem_drawing_declares_its_broadcast_and_an_ordinary_drawing_does_not()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var recipes = new PvfRecipeContentSource(provider);

        Assert.True((await recipes.GetAsync(2651400, default))?.Broadcast);
        Assert.False((await recipes.GetAsync(1287, default))?.Broadcast);
    }
}
