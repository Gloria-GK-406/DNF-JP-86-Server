using Dfo.Pvf.Tokens;
using Dfo.Server.Infrastructure.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

public sealed class PvfDevilContractTests
{
    [SkippableTheory]
    [InlineData(100625, 60)]
    [InlineData(103156, 30)]
    public async Task Both_wrapper_formats_resolve_the_eight_declared_services(int id, int days)
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var product = await new PvfCeraShopDefinitionSource(provider).GetProductAsync(id, default);
        Assert.NotNull(product);
        Assert.Empty(product.Diagnostics);
        Assert.True(product.IsService);
        Assert.Equal(3880, product.Price!.Cera);
        Assert.Equal(days, product.ServiceDays);
        Assert.Equal(Enumerable.Range(0, 8), product.ServiceSlots.Order());
    }

    [Theory]
    [InlineData(30, 1, true)]
    [InlineData(60, 1, true)]
    [InlineData(0, 1, false)]
    [InlineData(-1, 1, false)]
    [InlineData(30, 0, false)]
    public void Unknown_bundle_terms_or_flags_are_refused(int days, int flag, bool valid)
    {
        PvfToken N(int value) => new(PvfTokenKind.Int32, value);
        var product = PvfCeraShopDefinitionSource.ParseProduct("[charac premium package]",
            [N(100625), N(2682006), N(3880), new(PvfTokenKind.QuotedString, 0), N(0), N(0), N(flag), N(days), N(100614)]);
        Assert.Equal(valid, product.Diagnostics.IsEmpty);
    }
}
