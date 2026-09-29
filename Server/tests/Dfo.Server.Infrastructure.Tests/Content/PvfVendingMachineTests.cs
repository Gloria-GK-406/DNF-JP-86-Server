using Dfo.Server.Infrastructure.Content;
using Dfo.Server.Data.Inventory;
namespace Dfo.Server.Infrastructure.Tests.Content;
public sealed class PvfVendingMachineTests
{
    [SkippableFact]
    public void Actual_archive_preloads_coin_and_weight_table_without_future_archive_reads()
    {
        Skip.IfNot(ContentArchive.Available,ContentArchive.SkipReason);
        using var provider=ContentArchive.CreateProvider();
        var source=new PvfVendingMachineDefinitionSource(provider);
        Assert.Equal(2,source.Initialize());
        var d=source.GetRequired(1,1); Assert.Equal(7454,d.MaterialId); Assert.Equal(1,d.MaterialCount);
        Assert.Equal(7463,d.Rewards[0].Reward.ItemId); Assert.Equal(4,d.Rewards[0].Reward.Count);
        Assert.Equal(1,d.Rewards[0].CumulativeWeight); Assert.True(d.Rewards[^1].CumulativeWeight>1000000);
        var advanced=source.GetRequired(3,1); Assert.Equal(2749933,advanced.MaterialId); Assert.Equal(1,advanced.MaterialCount);
        Assert.Equal(5,advanced.Rewards[0].Reward.Count); Assert.Equal(37,advanced.Rewards.Length);
        Assert.Throws<OpeningRejectedException>(()=>source.GetRequired(2,1));
        provider.Dispose(); Assert.Same(d,source.GetRequired(1,1)); Assert.Equal(2,source.Initialize());
        Assert.Throws<OpeningRejectedException>(()=>source.GetRequired(999,1));
    }
    [Fact]
    public void Malformed_probability_table_fails_loudly()
    {
        Assert.Throws<InvalidDataException>(()=>PvfVendingMachineDefinitionSource.ParseRewards([1,1,1]));
        Assert.Throws<InvalidDataException>(()=>PvfVendingMachineDefinitionSource.ParseRewards([1,-1,1,0]));
        Assert.Throws<InvalidDataException>(()=>PvfVendingMachineDefinitionSource.ParseRewards([1,0,1,0]));
    }
}
