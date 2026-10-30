using Dfo.Pvf.Schema;
using Dfo.Pvf;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfEquipmentRepairDefinitionSource(PvfArchiveProvider provider) : IEquipmentRepairDefinitionSource, Dfo.Server.Abstractions.Content.IContentWarmup
{
    private static class Fields
    {
        internal static readonly PvfField<int> RepairPriceField = PvfField.Int32("[repair price]", optional: true);
        internal static readonly PvfField<int> GradeField = PvfField.Int32("[grade]", optional: true);
        internal static readonly PvfField<int> DurabilityField = PvfField.Int32("[durability]", optional: true);
        internal static readonly PvfField<double> RepairCostField = PvfField.Number("[repair cost]", optional: true);
        internal static readonly PvfField<IReadOnlyList<double>> RepairCostRateByUpgradeLevelField = PvfField.Numbers("[repair cost rate by upgrade level]", optional: true);
    }

    private readonly Lock gate = new();
    private readonly Dictionary<int, EquipmentRepairDefinition?> cache = [];
    private IReadOnlyDictionary<int, string>? paths;
    private (float CostRate, IReadOnlyList<float> UpgradeRates)? rates;

    public string Name => "equipment-repair";

    public async ValueTask<int> WarmAsync(CancellationToken cancellationToken)
    {
        await RatesAsync(cancellationToken).ConfigureAwait(false);
        lock (gate)
        {
            PathsIndex(provider.Archive);
            return paths.Count;
        }
    }

    [System.Diagnostics.CodeAnalysis.MemberNotNull(nameof(paths))]
    private void PathsIndex(Dfo.Pvf.PvfArchive archive)
    {
        paths ??= PvfListFile.Paths(archive, "equipment/equipment.lst");
    }

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
        PathsIndex(archive);
        if (!paths.TryGetValue(itemId, out var relative))
            return null;
        var sections = (PvfDocument.TryOpen(archive, "equipment/" + relative, PvfPreamble.Ignore)?.Sections ?? []);
        var price = PvfSections.SingleInt32(archive, sections, Fields.RepairPriceField, PvfRepeats.AsUndeclared, "equipment");
        var grade = PvfSections.SingleInt32(archive, sections, Fields.GradeField, PvfRepeats.AsUndeclared, "equipment");
        var durability = PvfSections.SingleInt32(archive, sections, Fields.DurabilityField, PvfRepeats.AsUndeclared, "equipment");
        // Some equipment (such as non-durable bracelets) declares neither repair field. It is
        // still a valid equipment instance: repairing it is a zero-cost, unchanged operation.
        // A single missing or malformed field is not the same thing and must stay unreadable.
        if (grade is >= 0 && !sections.Any(x => (x.Name == Fields.RepairPriceField.Name || x.Name == Fields.DurabilityField.Name)))
            return new(0, grade.Value, 0);
        return price is > 0 && grade is >= 0 && durability is > 0 and <= ushort.MaxValue
            ? new(price.Value, grade.Value, (ushort)durability.Value) : null;
    }

    private (float CostRate, IReadOnlyList<float> UpgradeRates)? ReadRates()
    {
        var archive = provider.Archive;
        var price = (PvfDocument.TryOpen(archive, "equipment/pricetable.tbl", PvfPreamble.Ignore)?.Sections ?? []);
        var upgrade = (PvfDocument.TryOpen(archive, "etc/upgrade.etc", PvfPreamble.Ignore)?.Sections ?? []);
        var cost = OneFloat(archive, price, Fields.RepairCostField);
        var section = upgrade.Where(x => x.Name == Fields.RepairCostRateByUpgradeLevelField.Name).ToArray();
        if (cost is not > 0 || section.Length != 1 || section[0].Values.Count == 0)
            return null;
        var upgradeRates = section[0].Values.Select(Number).ToArray();
        if (upgradeRates.Any(x => !float.IsFinite(x) || x <= 0))
            return null;
        return (cost.Value, upgradeRates);
    }

    private static float? OneFloat(PvfArchive archive, IReadOnlyList<PvfSection> sections, PvfField<double> name)
    {
        var matches = sections.Where(x => x.Name == name.Name).ToArray();
        return matches.Length == 1 && matches[0].Values.Count == 1
            && float.IsFinite(Number(matches[0].Values[0]))
            ? (float)name.Decode(archive, matches[0].Values, "equipment repair rate") : null;
    }

    private static float Number(PvfToken value) => value.Kind switch
    {
        PvfTokenKind.Int32 => value.Value,
        PvfTokenKind.Single => value.AsSingle(),
        _ => float.NaN,
    };
}
