using Dfo.Server.Abstractions.Content;
using Dfo.Server.Infrastructure.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

public sealed class PvfMirrorDungeonTests
{
    [SkippableFact]
    public async Task A21_mirror_catalog_resolves_observed_pair_entry_permissions_and_daily_rewards()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var catalog = new PvfMirrorDungeonDefinitionSource(provider).Load();
        Assert.Equal(8000, catalog.WrapperDungeonId);
        Assert.Equal(31, catalog.WorldMapId);
        Assert.Equal((byte)21, catalog.EntryTownId);
        Assert.Equal((byte)2, catalog.EntryAreaId);
        Assert.Equal((byte)55, catalog.MinimumLevel);
        Assert.Equal([14395], catalog.RequiredQuestIds);
        Assert.Equal(10, catalog.DailyEntryLimit);
        Assert.Equal(5, catalog.DailyClearLimit);
        Assert.Equal(5, catalog.DailyReward.RequiredClears);
        Assert.Equal(3037, catalog.DailyReward.ItemId);
        Assert.Equal(10, catalog.DailyReward.Count);
        Assert.Contains(catalog.Candidates, x => x.DungeonId == 32 && x.QuestId == 14299);
        var band = Assert.Single(catalog.Rewards);
        Assert.Equal(55, band.MinimumLevel);
        Assert.Equal(86, band.MaximumLevel);
        Assert.Equal(10100301, Assert.Single(band.Items).ItemId);
        var quest = await new PvfQuestDefinitionSource(provider).GetAsync(14299, default);
        Assert.Equal(QuestGrade.Crack, quest!.Grade);
        Assert.Contains(quest.Rewards, x => x.Kind == QuestRewardKind.CrackOfDimension);
    }
}
