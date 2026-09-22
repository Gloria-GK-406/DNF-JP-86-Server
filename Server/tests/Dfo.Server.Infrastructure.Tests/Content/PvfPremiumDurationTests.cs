using Dfo.Pvf.Tokens;
using Dfo.Server.Infrastructure.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

public sealed class PvfPremiumDurationTests
{
    [SkippableFact]
    public void A21_contract_levels_and_short_term_products_are_read_from_the_archive()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var levels = new PvfPremiumServiceDefinitionSource(provider).ReadContractLevels();
        Assert.Equal(10, new PvfPremiumServiceDefinitionSource(provider).ReadFreeWeaknessDailyLimit());
        Assert.Equal(6, new PvfPremiumServiceDefinitionSource(provider).ReadAutoRepairDailyLimit());
        Assert.Equal(5, levels.ExpertSkill);
        Assert.Equal(10, levels.OverEquipment);
        var terms = PvfPackageOpeningDefinitionSource.ParsePremiums(
            PvfNpcShopDefinitionSource.Sections(provider.Archive, "etc/premiumlist_new.etc"));
        Assert.Equal((22, TimeSpan.FromMinutes(60)), terms[10096109]);
        Assert.Equal((27, TimeSpan.FromMinutes(60)), terms[10096110]);
        Assert.Contains(198, terms.Keys);
    }

    [Fact]
    public void Item_boundaries_preserve_minute_units_and_intervening_restrictions()
    {
        var result = PvfPackageOpeningDefinitionSource.ParsePremiums([
            S("[type]", 27), S("[item]", 10096110), S("[term]", 60), S("[/term]"),
            S("[is term unit minute]", 1), S("[/item]"),
            S("[item]", 43), S("[restrict]", 1), S("[term]", 3), S("[/item]"),
            S("[item]", 999), S("[/item]"), S("[type]", 22), S("[term]", 90)]);
        Assert.Equal((27, TimeSpan.FromMinutes(60)), result[10096110]);
        Assert.Equal((27, TimeSpan.FromDays(3)), result[43]);
        Assert.False(result.ContainsKey(999));
    }
    private static PvfNpcShopDefinitionSource.Section S(string name, params int[] values) =>
        new(name, values.Select(value => new PvfToken(PvfTokenKind.Int32, value)).ToList());
}
