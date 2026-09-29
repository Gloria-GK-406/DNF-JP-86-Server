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
        Assert.Equal(1,source.Initialize());
        var d=source.GetRequired(1,1); Assert.Equal(7454,d.MaterialId); Assert.Equal(1,d.MaterialCount);
        Assert.Equal(7463,d.Rewards[0].Reward.ItemId); Assert.Equal(4,d.Rewards[0].Reward.Count);
        Assert.Equal(1,d.Rewards[0].CumulativeWeight); Assert.True(d.Rewards[^1].CumulativeWeight>1000000);
        provider.Dispose(); Assert.Same(d,source.GetRequired(1,1)); Assert.Equal(1,source.Initialize());
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
