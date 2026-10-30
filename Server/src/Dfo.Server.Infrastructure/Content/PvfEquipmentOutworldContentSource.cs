using Dfo.Pvf.Schema;
using Dfo.Pvf;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Infrastructure.Content;

/// <summary>Reads only A21 amplifyitem facts needed by purification and attribute selection.</summary>
internal sealed class PvfEquipmentOutworldContentSource(PvfArchiveProvider provider)
    : IEquipmentOutworldContentSource, Dfo.Server.Abstractions.Content.IContentWarmup
{
    private static class Fields
    {
        internal static readonly PvfField<IReadOnlyList<int>> PurifyOnlyCeraMaterialField = PvfField.Int32List("[purify only cera material]", optional: true);

        internal static readonly PvfField<int> EquipLevelConstField = PvfField.Int32("[equip level const]", optional: true);
        internal static readonly PvfField<IReadOnlyList<int>> PurifyMaterialField = PvfField.Int32List("[purify material]", optional: true);
        internal static readonly PvfField<IReadOnlyList<int>> PurifyOnlyMaterialField = PvfField.Int32List("[purify only material]", optional: true);
        internal static readonly PvfField<IReadOnlyList<PvfToken>> InvestOptionField = PvfField.Tokens("[invest option]", optional: true);
        internal static readonly PvfField<IReadOnlyList<PvfToken>> ReinvestOptionField = PvfField.Tokens("[reinvest option]", optional: true);
        internal static readonly PvfField<IReadOnlyList<PvfToken>> OptionDataField = PvfField.Tokens("[option data]", optional: true);
        internal static readonly PvfField<IReadOnlyList<PvfToken>> RarityWeightField = PvfField.Tokens("[rarity weight]", optional: true);
    }

    private static readonly IReadOnlyDictionary<string, byte> Attributes =
        new Dictionary<string, byte>(StringComparer.Ordinal)
        {
            ["physical defense"] = 1,
            ["magical defense"] = 2,
            ["physical attack"] = 3,
            ["magical attack"] = 4,
            ["all"] = 0,
        };
    private static readonly string[] RarityNames =
        ["common", "uncommon", "rare", "unique", "epic", "chronicle", "legendary"];

    private readonly Lock gate = new();
    private bool loaded;
    private EquipmentOutworldTable? table;

    // Explicit: the class already has a private Name(archive, token) helper.
    string Dfo.Server.Abstractions.Content.IContentWarmup.Name => "equipment-outworld";

    public async ValueTask<int> WarmAsync(CancellationToken cancellationToken)
    {
        return await GetAsync(cancellationToken).ConfigureAwait(false) is null ? 0 : 1;
    }

    public ValueTask<EquipmentOutworldTable?> GetAsync(CancellationToken cancellationToken)
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

    private static EquipmentOutworldTable? Read(PvfArchive archive)
    {
        var sections = (PvfDocument.TryOpen(archive, "etc/amplifyitem.etc", PvfPreamble.Ignore)?.Sections ?? []);
        if (sections.Count == 0) return null;
        var level = PvfSections.Single(sections, Fields.EquipLevelConstField, PvfRepeats.AsUndeclared, "equipment");
        if (level?.Values is not { Count: 1 } || Int(level.Values[0]) is not int minimum
            || minimum <= 0) return null;
        var purify = ReadItemCounts(sections, Fields.PurifyMaterialField);
        var clear = ReadItemCounts(sections, Fields.PurifyOnlyMaterialField);
        if (purify is null || clear is null) return null;
        foreach (var name in new[] { Fields.PurifyOnlyCeraMaterialField })
        {
            var extra = ReadItemCounts(sections, name);
            if (extra is null || extra.Keys.Any(clear.ContainsKey)) return null;
            foreach (var pair in extra) clear[pair.Key] = pair.Value;
        }
        var invest = ReadOptions(archive, PvfSections.Single(sections, Fields.InvestOptionField, PvfRepeats.AsUndeclared, "equipment"));
        var twist = ReadOptions(archive, PvfSections.Single(sections, Fields.ReinvestOptionField, PvfRepeats.AsUndeclared, "equipment"));
        var weights = ReadRollWeights(archive, PvfSections.Single(sections, Fields.OptionDataField, PvfRepeats.AsUndeclared, "equipment"), out var bases);
        var rarityWeights = ReadRarityWeights(archive, PvfSections.Single(sections, Fields.RarityWeightField, PvfRepeats.AsUndeclared, "equipment"));
        if (invest is null || twist is null || weights is null || rarityWeights is null
            || purify.Count == 0 || clear.Count == 0) return null;
        var initial = new Dictionary<int, IReadOnlyDictionary<byte, ushort>>();
        for (var rarity = 2; rarity < RarityNames.Length; rarity++)
        {
            if (!rarityWeights.TryGetValue(RarityNames[rarity], out var factor)) return null;
            var values = new Dictionary<byte, ushort>();
            foreach (var pair in bases)
            {
                var product = factor * pair.Value;
                if (!double.IsFinite(product) || product < 1 || product > ushort.MaxValue)
                    return null;
                values[pair.Key] = (ushort)product; // upstream truncates positive product
            }
            initial[rarity] = values;
        }
        return new(minimum, purify, clear, invest, twist, initial, weights);
    }

    private static Dictionary<int, int>? ReadItemCounts(
        IReadOnlyList<PvfSection> sections, PvfField name)
    {
        var result = new Dictionary<int, int>();
        foreach (var section in sections.Where(section => section.Name == name.Name))
        {
            if (section.Values.Count == 0 || section.Values.Count % 2 != 0) return null;
            for (var i = 0; i < section.Values.Count; i += 2)
            {
                if (Int(section.Values[i]) is not int id || id <= 0
                    || Int(section.Values[i + 1]) is not int count || count <= 0
                    || !result.TryAdd(id, count)) return null;
            }
        }
        return result;
    }

    private static Dictionary<int, EquipmentOutworldMaterialDefinition>? ReadOptions(
        PvfArchive archive, PvfSection? section)
    {
        if (section is null || section.Values.Count == 0 || section.Values.Count % 3 != 0)
            return null;
        var result = new Dictionary<int, EquipmentOutworldMaterialDefinition>();
        for (var i = 0; i < section.Values.Count; i += 3)
        {
            var name = Name(archive, section.Values[i]);
            if (!Attributes.TryGetValue(name, out var attribute)
                || Int(section.Values[i + 1]) is not int id || id <= 0
                || Int(section.Values[i + 2]) is not int count || count <= 0
                || !result.TryAdd(id, new(attribute, count))) return null;
        }
        return result;
    }

    private static List<EquipmentOutworldRollWeight>? ReadRollWeights(PvfArchive archive,
        PvfSection? section, out Dictionary<byte, int> bases)
    {
        bases = [];
        if (section is null || section.Values.Count != 12) return null;
        var result = new List<EquipmentOutworldRollWeight>(4);
        for (var i = 0; i < section.Values.Count; i += 3)
        {
            var name = Name(archive, section.Values[i]);
            if (!Attributes.TryGetValue(name, out var attribute) || attribute == 0
                || Int(section.Values[i + 1]) is not int weight || weight <= 0
                || Int(section.Values[i + 2]) is not int baseValue || baseValue <= 0
                || !bases.TryAdd(attribute, baseValue)) return null;
            result.Add(new(attribute, weight));
        }
        return result;
    }

    private static Dictionary<string, double>? ReadRarityWeights(PvfArchive archive,
        PvfSection? section)
    {
        if (section is null || section.Values.Count != 14) return null;
        var result = new Dictionary<string, double>(StringComparer.Ordinal);
        for (var i = 0; i < section.Values.Count; i += 2)
        {
            var name = Name(archive, section.Values[i]);
            var value = section.Values[i + 1];
            if (value.Kind != PvfTokenKind.Single || !RarityNames.Contains(name)
                || !result.TryAdd(name, BitConverter.Int32BitsToSingle(value.Value))
                || !double.IsFinite(result[name]) || result[name] <= 0) return null;
        }
        return result;
    }

    private static int? Int(PvfToken token) => token.Kind == PvfTokenKind.Int32
        ? token.Value : null;

    private static string Name(PvfArchive archive, PvfToken token) => token.IsString
        ? archive.ResolveString(token.Value).Trim().Trim('`', '[', ']').ToLowerInvariant()
        : string.Empty;
}
