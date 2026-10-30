using Dfo.Pvf.Schema;
using Dfo.Pvf;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Infrastructure.Content;

/// <summary>Reads A21 separate-upgrade declarations; does not mutate items.</summary>
internal sealed class PvfEquipmentForgingContentSource(PvfArchiveProvider provider)
    : IEquipmentForgingContentSource, Dfo.Server.Abstractions.Content.IContentWarmup
{
    private static class Fields
    {
        internal static readonly PvfField<int> SeparateUpgradeMaxField = PvfField.Int32("[separate upgrade max]", optional: true);
        internal static readonly PvfField<IReadOnlyList<double>> TableField = PvfField.Numbers("[table]", optional: true);
        internal static readonly PvfField<IReadOnlyList<int>> ItemWeightsByGradeField = PvfField.Int32List("[item weights by grade]", optional: true);
        internal static readonly PvfField<IReadOnlyList<double>> ItemWeightsByRarityField = PvfField.Numbers("[item weights by rarity]", optional: true);
    }

    private readonly Lock gate = new();
    private bool loaded;
    private EquipmentForgingTable? table;

    public string Name => "equipment-forging";

    public async ValueTask<int> WarmAsync(CancellationToken cancellationToken)
    {
        return await GetAsync(cancellationToken).ConfigureAwait(false) is null ? 0 : 1;
    }

    public ValueTask<EquipmentForgingTable?> GetAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (!loaded)
            {
                table = Read(provider.Archive);
                loaded = true;
            }
            return ValueTask.FromResult(table);
        }
    }

    private static EquipmentForgingTable? Read(PvfArchive archive)
    {
        var sections = (PvfDocument.TryOpen(archive, "etc/upgrade_separate.etc", PvfPreamble.Ignore)?.Sections ?? []);
        var max = PvfSections.Single(sections, Fields.SeparateUpgradeMaxField, PvfRepeats.AsUndeclared, "equipment");
        var rows = PvfSections.Single(sections, Fields.TableField, PvfRepeats.AsUndeclared, "equipment");
        var grade = PvfSections.Single(sections, Fields.ItemWeightsByGradeField, PvfRepeats.AsUndeclared, "equipment");
        var rarity = PvfSections.Single(sections, Fields.ItemWeightsByRarityField, PvfRepeats.AsUndeclared, "equipment");
        if (max?.Values is not { Count: 1 } || Int(max.Values[0]) is not int maximum
            || maximum is <= 0 or > byte.MaxValue
            || rows is null || rows.Values.Count < maximum * 4 || rows.Values.Count % 4 != 0
            || grade is null || grade.Values.Count == 0 || grade.Values.Count % 3 != 0
            || rarity?.Values is not { Count: 7 }) return null;

        var levels = new List<EquipmentForgingLevel>(rows.Values.Count / 4);
        for (var i = 0; i < rows.Values.Count; i += 4)
        {
            if (Int(rows.Values[i]) is not int first || first < 0
                || Int(rows.Values[i + 1]) is not int second || second < 0
                || Int(rows.Values[i + 2]) is not int weight || weight is < 0 or > 10000
                || Number(rows.Values[i + 3]) is not double factor || !double.IsFinite(factor)
                || factor <= 0) return null;
            levels.Add(new(levels.Count + 1, weight, factor));
        }
        var materials = new Dictionary<int, EquipmentForgingMaterial>();
        for (var i = 0; i < grade.Values.Count; i += 3)
        {
            if (Int(grade.Values[i]) is not int equipmentGrade || equipmentGrade < 0
                || Int(grade.Values[i + 1]) is not int itemId || itemId <= 0
                || Int(grade.Values[i + 2]) is not int baseCount || baseCount <= 0
                || !materials.TryAdd(equipmentGrade, new(itemId, baseCount))) return null;
        }
        var rarityWeights = new List<double>(rarity.Values.Count);
        foreach (var token in rarity.Values)
        {
            if (Number(token) is not double factor || !double.IsFinite(factor)
                || factor <= 0) return null;
            rarityWeights.Add(factor);
        }
        return new(maximum, levels, materials, rarityWeights);
    }

    private static int? Int(PvfToken token) => token.Kind == PvfTokenKind.Int32
        ? token.Value : null;

    private static double? Number(PvfToken token) => token.Kind switch
    {
        PvfTokenKind.Int32 => token.Value,
        PvfTokenKind.Single => BitConverter.Int32BitsToSingle(token.Value),
        _ => null,
    };
}
