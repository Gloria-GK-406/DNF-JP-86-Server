using Dfo.Pvf;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfEquipmentRepairDefinitionSource(PvfArchiveProvider provider) : IEquipmentRepairDefinitionSource
{
    private readonly Lock gate = new();
    private readonly Dictionary<int, EquipmentRepairDefinition?> cache = [];
    private Dictionary<int, string>? paths;
    private (float CostRate, IReadOnlyList<float> UpgradeRates)? rates;

    public ValueTask<EquipmentRepairDefinition?> GetAsync(int itemId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (!cache.TryGetValue(itemId, out var result))
                cache[itemId] = result = Read(itemId);
            return ValueTask.FromResult(result);
        }
    }

    public ValueTask<(float CostRate, IReadOnlyList<float> UpgradeRates)?> RatesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
            return ValueTask.FromResult(rates ??= ReadRates());
    }

    private EquipmentRepairDefinition? Read(int itemId)
    {
        var archive = provider.Archive;
        paths ??= PvfListFile.Read(archive, "equipment/equipment.lst")
            .ToDictionary(entry => entry.Id, entry => entry.RelativePath);
        if (!paths.TryGetValue(itemId, out var relative))
            return null;
        var sections = PvfNpcShopDefinitionSource.Sections(archive, "equipment/" + relative);
        var price = OneInt(sections, "[repair price]");
        var grade = OneInt(sections, "[grade]");
        var durability = OneInt(sections, "[durability]");
        return price is > 0 && grade is >= 0 && durability is > 0 and <= ushort.MaxValue
            ? new(price.Value, grade.Value, (ushort)durability.Value) : null;
    }

    private (float CostRate, IReadOnlyList<float> UpgradeRates)? ReadRates()
    {
        var archive = provider.Archive;
        var price = PvfNpcShopDefinitionSource.Sections(archive, "equipment/pricetable.tbl");
        var upgrade = PvfNpcShopDefinitionSource.Sections(archive, "etc/upgrade.etc");
        var cost = OneFloat(price, "[repair cost]");
        var section = upgrade.Where(x => x.Name == "[repair cost rate by upgrade level]").ToArray();
        if (cost is not > 0 || section.Length != 1 || section[0].Values.Count == 0)
            return null;
        var upgradeRates = section[0].Values.Select(Number).ToArray();
        if (upgradeRates.Any(x => !float.IsFinite(x) || x <= 0))
            return null;
        return (cost.Value, upgradeRates);
    }

    private static int? OneInt(IReadOnlyList<PvfNpcShopDefinitionSource.Section> sections, string name)
    {
        var matches = sections.Where(x => x.Name == name).ToArray();
        return matches.Length == 1 && matches[0].Values.Count == 1
            && matches[0].Values[0].Kind == PvfTokenKind.Int32 ? matches[0].Values[0].Value : null;
    }

    private static float? OneFloat(IReadOnlyList<PvfNpcShopDefinitionSource.Section> sections, string name)
    {
        var matches = sections.Where(x => x.Name == name).ToArray();
        return matches.Length == 1 && matches[0].Values.Count == 1
            ? Number(matches[0].Values[0]) : null;
    }

    private static float Number(PvfToken value) => value.Kind switch
    {
        PvfTokenKind.Int32 => value.Value,
        PvfTokenKind.Single => value.AsSingle(),
        _ => float.NaN,
    };
}
