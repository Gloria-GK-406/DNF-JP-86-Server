using Dfo.Server.Infrastructure.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

public sealed class PvfEquipmentRepairDefinitionSourceTests
{
    [SkippableFact]
    public async Task Observed_sword_and_archive_cost_rate_match_the_live_quote_inputs()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfEquipmentRepairDefinitionSource(provider);
        var sword = await source.GetAsync(101010437, default);
        Assert.NotNull(sword);
        Assert.Equal((1680, 3, (ushort)45),
            (sword.RepairPrice, sword.Grade, sword.MaxDurability));
        var rates = await source.RatesAsync(default);
        Assert.NotNull(rates);
        Assert.Equal(0.08415f, rates.Value.CostRate);
        Assert.Equal(1f, rates.Value.UpgradeRates[0]);
        var nondurableBracelet = await source.GetAsync(100310669, default);
        Assert.NotNull(nondurableBracelet);
        Assert.Equal((0, 7, (ushort)0),
            (nondurableBracelet.RepairPrice, nondurableBracelet.Grade, nondurableBracelet.MaxDurability));
        Assert.Null(await source.GetAsync(int.MaxValue, default));
    }
}
