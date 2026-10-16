using Dfo.Server.Abstractions.Content;
using Dfo.Server.Infrastructure.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

public sealed class MarriageProposalContentTests
{
    [SkippableFact]
    public void Ceremony_tiers_and_wait_selections_come_from_the_current_archive()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var content = new PvfMarriageCeremonyContentSource(provider).Load();
        Assert.Equal(new[] { 0, 1, 2 }, content.Tiers.Select(x => x.Tier));
        Assert.Equal(500000, content.Tiers.Single(x => x.Tier == 0).Gold);
        Assert.Equal(new[] { 300, 600, 900 }, content.WaitDurations);
    }
    [SkippableFact]
    public void Minimum_proposal_level_comes_from_current_wedding_content()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var content = new PvfMarriageProposalContentSource(provider).Load();
        Assert.Equal(30, content.MinimumLevel);
        Assert.Equal(20737, content.TrialQuestId);
        Assert.Equal(20738, content.MaterialQuestId);
    }

    [SkippableFact]
    public async Task Current_ordinary_boss_maps_and_trial_reward_box_are_supported()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var definitions = new PvfDungeonDefinitionSource(provider);
        var maps = new PvfDungeonMapDefinitionSource(provider);
        var map = (await maps.ListForDungeonAsync(2, default)).First();
        var source = new PvfMarriageDungeonContentSource(provider, definitions, maps);
        Assert.True(await source.IsOrdinaryMapAsync(2, map.MapId, default));
        Assert.False(await source.IsOrdinaryMapAsync(2, int.MaxValue, default));
        var packages = new PvfPackageOpeningDefinitionSource(provider,
            new PvfCeraShopDefinitionSource(provider), new PvfItemPlacementDefinitionSource(provider));
        Assert.NotNull(await packages.GetAsync(10002616, default));
    }
}
