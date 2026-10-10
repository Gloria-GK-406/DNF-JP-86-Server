using Dfo.Server.Infrastructure.Content;
using Dfo.Pvf.Tests.Support;

namespace Dfo.Server.Infrastructure.Tests.Content;

public sealed class PvfItemSaleDefinitionSourceTests
{
    [Theory]
    [InlineData("gift", true, true)]
    [InlineData("[gift]", true, true)]
    [InlineData("sell", true, false)]
    [InlineData("[sell]", true, false)]
    [InlineData("unknown", true, true)]
    [InlineData("upgrade", true, true)]
    [InlineData("disjoint", true, true)]
    [InlineData("trade", true, true)]
    [InlineData("gift", false, true)]
    [InlineData("", true, true)]
    public async Task Only_sale_restrictions_prohibit_NPC_selling(
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
        Assert.True(potion.CanSell);
        Assert.NotNull(potion.PricingFailure);
    }

    [SkippableTheory]
    [InlineData(101040127, 104960, 48)]
    [InlineData(106020145, 91840, 34)]
    public async Task Reported_equipment_uses_root_value_without_effect_values(int itemId, int value, int durability)
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var definition = await new PvfItemSaleDefinitionSource(provider).GetAsync(itemId, default);
        Assert.NotNull(definition);
        Assert.True(definition.CanSell);
        Assert.Null(definition.PricingFailure);
        Assert.Equal((value, 200, durability), (definition.Value, definition.RatePerThousand, (int)definition.MaxDurability));
    }

    [Theory]
    [InlineData("nested", false, 100)]
    [InlineData("duplicate", true, 0)]
    [InlineData("missing", true, 0)]
    [InlineData("negative", true, 0)]
    [InlineData("malformed", true, 0)]
    [InlineData("durability", true, 0)]
    [InlineData("rate", true, 0)]
    [InlineData("zero", false, 0)]
    public async Task Pricing_content_errors_are_distinct_from_sale_permission(string shape, bool failure, int value)
    {
        var builder = new PvfArchiveBuilder();
        builder.AddScript("equipment", "equipment.lst").Int(9002).Quoted("sword.equ").End();
        builder.AddScript("stackable", "stackable.lst").Int(9001).Quoted("unused.stk").End();
        var script = builder.AddScript("equipment", "sword.equ").Tag("[name]").Quoted("Sword");
        if (shape != "missing")
        {
            script.Tag("[value]");
            if (shape == "malformed") script.Quoted("bad");
            else script.Int(shape == "negative" ? -1 : shape == "zero" ? 0 : 100);
        }
        if (shape == "duplicate") script.Tag("[value]").Int(200);
        if (shape == "nested") script.Tag("[effect]").Tag("[value]").Int(999)
            .Tag("[durability]").Int(999).Tag("[/effect]");
        script.Tag("[durability]").Int(shape == "durability" ? -1 : 45).End();
        var table = builder.AddScript("equipment", "pricetable.tbl").Tag("[sale rate]");
        if (shape == "rate") table.Quoted("bad");
        else table.Int(200).Int(150).Int(30);
        table.End();
        var path = Path.Combine(Path.GetTempPath(), $"dfo-sale-price-{Guid.NewGuid():N}.pvf");
        File.WriteAllBytes(path, builder.Build());
        try
        {
            using var provider = new PvfArchiveProvider(new PvfContentOptions { ArchivePath = path });
            var definition = await new PvfItemSaleDefinitionSource(provider).GetAsync(9002, default);
            Assert.NotNull(definition);
            Assert.True(definition.CanSell);
            Assert.Equal(value, definition.Value);
            Assert.Equal(failure, definition.PricingFailure is not null);
            if (!failure) Assert.Equal(45, definition.MaxDurability);
        }
        finally { File.Delete(path); }
    }
}
