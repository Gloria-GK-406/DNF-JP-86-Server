using Dfo.Server.Infrastructure.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

public sealed class PvfItemSaleDefinitionSourceTests
{
    [SkippableFact]
    public async Task Equipment_sale_reads_value_and_the_archive_rate_instead_of_the_shop_price()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfItemSaleDefinitionSource(provider);
        var sword = await source.GetAsync(27312, default);
        Assert.NotNull(sword);
        Assert.True(sword.CanSell);
        Assert.Equal((12960, 200), (sword.Value, sword.RatePerThousand));
        Assert.True(sword.IsEquipment);
        Assert.Equal(45, sword.MaxDurability);
        Assert.Null(await source.GetAsync(int.MaxValue, default));
        var potion = await source.GetAsync(490003532, default);
        Assert.NotNull(potion);
        Assert.False(potion.CanSell);
    }
}
