using Dfo.Server.Infrastructure.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

public sealed class PvfStackableActionDefinitionSourceTests
{
    [SkippableFact]
    public async Task Batch_coupon_has_action_and_level_ceiling_independent_of_item_id()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfStackableActionDefinitionSource(provider);
        var rule = await source.GetAsync(10006007, CancellationToken.None);
        Assert.NotNull(rule);
        Assert.True(rule.IsReadable);
        Assert.Equal("[quest clear]", rule.Action);
        Assert.Equal(0, rule.MinimumLevel);
        Assert.Equal(84, rule.MaximumLevel);
        Assert.Same(rule, await source.GetAsync(10006007, CancellationToken.None));
        Assert.Null(await source.GetAsync(int.MaxValue, CancellationToken.None));
    }

    [SkippableFact]
    public async Task Single_quest_coupon_is_a_different_action()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfStackableActionDefinitionSource(provider);
        var rule = await source.GetAsync(10003230, CancellationToken.None);
        Assert.NotNull(rule);
        Assert.Equal("[quest clear coupon]", rule.Action);
    }
}
