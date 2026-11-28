using Dfo.Server.Infrastructure.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

public sealed class PvfConsumableCooldownTests
{
    [SkippableTheory]
    [InlineData(2600047, 900000, false)]
    [InlineData(2600053, 900000, false)]
    [InlineData(2680327, 5000, false)]
    [InlineData(10005034, 86400000, true)]
    [InlineData(10151895, 300000, true)]
    public async Task Actual_templates_use_the_last_time_and_the_third_flag(int itemId, int duration, bool persists)
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var rule = Assert.IsType<Dfo.Server.Abstractions.Content.ItemUseRule>(
            await new PvfItemUseRuleDefinitionSource(provider).GetAsync(itemId, default));
        Assert.Equal(duration, rule.CooldownMilliseconds);
        Assert.Equal(persists, rule.CooldownMaintenance.PersistOnServer);
        if (itemId is 2600047 or 2600053)
        {
            Assert.Equal(100, rule.CooltimeGroup);
            Assert.True(rule.SummonsApc);
        }
        var usage = await new PvfItemUsageRuleSource(provider).GetAsync(itemId, default);
        Assert.NotNull(usage);
        Assert.Equal(duration, usage.CooldownMilliseconds);
        Assert.Equal(rule.CooldownMaintenance, usage.CooldownMaintenance);
    }
}
