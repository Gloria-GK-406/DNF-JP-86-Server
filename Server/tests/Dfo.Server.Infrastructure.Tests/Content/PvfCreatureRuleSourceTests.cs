using Dfo.Server.Infrastructure.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

public sealed class PvfCreatureRuleSourceTests
{
    [SkippableFact]
    public async Task Real_archive_supplies_table_maximum_hunger_and_egg_output()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfCreatureRuleSource(provider, new PvfCreatureDefinitionSource(provider));

        var faras = await source.GetAsync(63000, CancellationToken.None);
        Assert.NotNull(faras);
        Assert.Equal(1, faras.CreatureId);
        Assert.Equal(50, faras.MaxLevel);
        Assert.True(faras.HungerEnabled);
        Assert.False(faras.IsEgg);
        Assert.Equal(63001, faras.OutputTemplateId);
        Assert.Contains(faras.Evolutions, evolution =>
            evolution.RequiredLevel == 20 && evolution.EvolvesIntoCreatureId == 2);
        Assert.Equal(55, faras.ExperienceThresholds.Count);
        Assert.Equal(2, faras.ExperienceThresholds[0]);
        Assert.Equal(5579, faras.ExperienceThresholds[^1]);
        Assert.Same(faras, await source.GetAsync(63000, CancellationToken.None));
        var definitions = await new PvfCreatureDefinitionSource(provider).GetAllAsync(CancellationToken.None);
        Assert.All(definitions, definition =>
            Assert.InRange(definition.MaximumLevel, 1, faras.ExperienceThresholds.Count + 1));

        var egg = await source.GetAsync(63022, CancellationToken.None);
        Assert.NotNull(egg);
        Assert.True(egg.IsEgg);
        Assert.Equal(63023, egg.HatchOutputTemplateId);
        Assert.Equal(20, (await source.GetAsync(63023, CancellationToken.None))?.CreatureId);

        var selectiveGrowth = await source.GetAsync(63158, CancellationToken.None);
        Assert.NotNull(selectiveGrowth);
        Assert.False(selectiveGrowth.HungerEnabled);

        var artifact = await source.GetArtifactExperienceAsync(63500, CancellationToken.None);
        Assert.NotNull(artifact);
        Assert.Equal(10, artifact.RequiredCreatureLevel);
        Assert.Equal(10f, artifact.BonusPercent);
        Assert.Same(artifact, await source.GetArtifactExperienceAsync(63500, CancellationToken.None));
        Assert.Null(await source.GetAsync(int.MaxValue, CancellationToken.None));
    }
}
