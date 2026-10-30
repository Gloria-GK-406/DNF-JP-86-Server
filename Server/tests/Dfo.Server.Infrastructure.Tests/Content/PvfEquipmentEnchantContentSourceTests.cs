using Dfo.Pvf.Tests.Support;
using Dfo.Pvf.Tokens;
using Dfo.Server.Infrastructure.Content;
using Dfo.Pvf.Schema;

namespace Dfo.Server.Infrastructure.Tests.Content;

public sealed class PvfEquipmentEnchantContentSourceTests
{
    [Fact]
    public void Unsupported_or_ambiguous_card_enchant_index_is_never_interpreted_as_zero()
    {
        static PvfSection Index(int value) =>
            new("[enchant index]", [new(PvfTokenKind.Int32, value)]);
        Assert.False(PvfEquipmentEnchantContentSource.HasSupportedEnchantIndex([]));
        Assert.False(PvfEquipmentEnchantContentSource.HasSupportedEnchantIndex([Index(0), Index(0)]));
        Assert.False(PvfEquipmentEnchantContentSource.HasSupportedEnchantIndex([Index(1)]));
        Assert.True(PvfEquipmentEnchantContentSource.HasSupportedEnchantIndex([Index(0)]));
    }

    [SkippableFact]
    public async Task A21_bead_resolves_its_card_and_rejects_an_incompatible_equipment_type()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfEquipmentEnchantContentSource(provider);

        var valid = await source.ResolveAsync(2600294, 101010437, default);
        Assert.NotNull(valid);
        Assert.Equal(3600, valid.CardItemId);
        Assert.Null(await source.ResolveAsync(2600294, 10018, default));
        Assert.Null(await source.ResolveAsync(3600, 101010437, default));
        Assert.Null(await source.ResolveAsync(2600294, int.MaxValue, default));
    }
}
