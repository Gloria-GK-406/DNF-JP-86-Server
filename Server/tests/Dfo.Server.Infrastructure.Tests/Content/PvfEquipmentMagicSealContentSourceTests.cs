using Dfo.Pvf.Tests.Support;
using Dfo.Server.Infrastructure.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

public sealed class PvfEquipmentMagicSealContentSourceTests
{
    [SkippableFact]
    public async Task A21_real_pool_rolls_quality_instead_of_stat_endpoints_for_unseal_and_change()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var rules = new Dfo.Server.Application.Inventory.EquipmentMagicSealRuleService(new PvfEquipmentMagicSealContentSource(provider));
        var values = new HashSet<byte>();
        var forceValues = new HashSet<byte>();
        foreach (var change in new[] { false, true })
        {
            for (var seed = 0; seed < 1000; seed++)
            {
                Dfo.Server.Data.Inventory.RandomOptionState[] existing = change ? [new(15, 13, 98), new(0, 0, 0), new(0, 0, 0)]
                    : [new(0, 0, 0), new(0, 0, 0), new(0, 0, 0)];
                var result = await rules.DecideAsync(101010339, existing, change ? (byte)0 : null, seed, default);
                Assert.Equal(Dfo.Server.Data.Inventory.EquipmentMagicSealRejection.None, result.Rejection);
                foreach (var option in result.Options!.Where(option => option.Type != 0))
                {
                    Assert.InRange(option.Value1, (byte)0, (byte)55);
                    Assert.InRange(option.Value2, (byte)0, (byte)55);
                    values.Add(option.Value1); values.Add(option.Value2);
                    if (option.Type == 15) { forceValues.Add(option.Value1); forceValues.Add(option.Value2); }
                }
            }
        }
        // A21's rare postfix thresholds span C/B/A/S at 14/37/47. All bands must be reachable,
        // including the originally reported Force option, rather than being fixed at Value2=98.
        foreach (var range in new[] { (0, 13), (14, 36), (37, 46), (47, 55) })
        {
            Assert.Contains(values, value => value >= range.Item1 && value <= range.Item2);
            Assert.Contains(forceValues, value => value >= range.Item1 && value <= range.Item2);
        }
    }

    [SkippableFact]
    public async Task A21_sealed_katana_has_real_option_pool_costs_and_low_level_scroll()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfEquipmentMagicSealContentSource(provider);

        var equipment = await source.GetEquipmentAsync(101010339, default);
        Assert.NotNull(equipment);
        Assert.Equal(75, equipment.MinimumLevel);
        Assert.Equal(2, equipment.Rarity);
        Assert.Equal(1720, equipment.UnsealGoldCost);
        Assert.Equal(3800, equipment.ChangeGoldCost);
        Assert.Contains(equipment.InitialGroups[0], candidate =>
            candidate.Type == 15 && candidate.MinimumStat == 13 && candidate.MaximumStat == 98);
        Assert.NotEmpty(equipment.Quantities);
        Assert.Equal(new[] { new Dfo.Server.Abstractions.Content.MagicSealValueBand(0, 25, 1500),
            new(10, 30, 7000), new(30, 55, 1500) }, equipment.ChangeValues);
        Assert.Null(await source.GetEquipmentAsync(101010437, default));

        var scroll = await source.GetScrollAsync(2600460, default);
        Assert.NotNull(scroll);
        Assert.Equal(30, scroll.MaximumEquipmentLevel);
        Assert.Null(await source.GetScrollAsync(2600294, default));
    }
}
