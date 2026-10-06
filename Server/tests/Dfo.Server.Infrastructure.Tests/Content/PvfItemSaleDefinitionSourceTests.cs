using Dfo.Server.Infrastructure.Content;
using Dfo.Pvf.Tests.Support;

namespace Dfo.Server.Infrastructure.Tests.Content;

public sealed class PvfItemSaleDefinitionSourceTests
{
    [Theory]
    [InlineData("gift", true, true)]
    [InlineData("[gift]", true, true)]
    [InlineData("sell", true, false)]
    [InlineData("unknown", true, false)]
    [InlineData("gift", false, false)]
    [InlineData("", true, false)]
    public async Task Gift_restriction_alone_does_not_prohibit_selling_but_unreadable_restrictions_stay_closed(
        string restriction, bool closed, bool canSell)
    {
        var builder=new PvfArchiveBuilder();
        builder.AddScript("equipment","equipment.lst").Int(9002).Quoted("unused.equ").End();
        builder.AddScript("stackable","stackable.lst").Int(9001).Quoted("coin.stk").End();
        var script=builder.AddScript("stackable","coin.stk").Tag("[value]").Int(45000)
            .Tag("[impossible contents]").Quoted(restriction);
        if(closed)script.Tag("[/impossible contents]");
        script.End();
        var path=Path.Combine(Path.GetTempPath(),$"dfo-sale-restriction-{Guid.NewGuid():N}.pvf");
        File.WriteAllBytes(path,builder.Build());
        try
        {
            using var provider=new PvfArchiveProvider(new PvfContentOptions{ArchivePath=path});
            var definition=await new PvfItemSaleDefinitionSource(provider).GetAsync(9001,default);
            Assert.NotNull(definition);Assert.Equal(canSell,definition.CanSell);
            Assert.Equal((45000,200),(definition.Value,definition.RatePerThousand));
        }
        finally { File.Delete(path); }
    }

    [SkippableTheory]
    [InlineData(490002916)]
    [InlineData(490700609)]
    public async Task Knight_coins_can_be_sold_at_the_declared_material_value(int itemId)
    {
        Skip.IfNot(ContentArchive.Available,ContentArchive.SkipReason);
        using var provider=ContentArchive.CreateProvider();
        var definition=await new PvfItemSaleDefinitionSource(provider).GetAsync(itemId,default);
        Assert.NotNull(definition);Assert.True(definition.CanSell);
        Assert.Equal((45000,200),(definition.Value,definition.RatePerThousand));
    }

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
