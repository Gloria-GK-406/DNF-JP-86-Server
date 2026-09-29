using Dfo.Server.Infrastructure.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

public sealed class FatiguePotionContentTests
{
    [SkippableTheory]
    [InlineData(2600026, 30, 1, 10000)]
    [InlineData(416, 50, 3, 1000)]
    [InlineData(7456, 40, -1, 86400000)]
    public async Task Archive_distinguishes_daily_quota_from_elapsed_cooldown(int id, int recovery, int limit, int cooldown)
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfStackableActionDefinitionSource(provider);
        var rule = await source.GetAsync(id, default);
        Assert.NotNull(rule);
        Assert.True(rule.IsReadable);
        Assert.Equal("[add fatigue]", rule.Action);
        Assert.Equal(recovery, rule.FatigueRecovery);
        Assert.Equal(limit, rule.DailyUseLimit);
        Assert.Equal(cooldown, rule.CooldownMilliseconds);
    }
}
