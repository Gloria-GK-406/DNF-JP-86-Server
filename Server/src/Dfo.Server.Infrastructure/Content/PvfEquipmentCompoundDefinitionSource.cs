using System.Collections.Frozen;
using Dfo.Pvf;
using Dfo.Pvf.Schema;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfEquipmentCompoundDefinitionSource(PvfArchiveProvider provider) : IEquipmentCompoundDefinitionSource
{
    private const string RulePath = "etc/randomoption/regenerationrandomoption.etc";
    private const string LevelPath = "etc/randomoption/randomizedoptionoverall1.etc";
    private const string IndexPath = "equipment/equipment.lst";

    private static readonly PvfField<IReadOnlyList<PvfToken>> Part = PvfField.Tokens("[choose part]");
    private static readonly PvfField<int> Rarity = PvfField.Int32("[rarity]", optional: true);
    private static readonly PvfField<int> MinimumLevel = PvfField.Int32("[minimum level]", optional: true);
    private static readonly PvfField<int> CreationRate = PvfField.Int32("[creation rate]", optional: true);
    private static readonly PvfField<string> ItemGroup = PvfField.String("[item group name]", optional: true);
    private static readonly PvfField<string> AttachType = PvfField.String("[attach type]", optional: true);
    private static readonly PvfField<IReadOnlyList<string>> Categories = PvfField.Strings("[item category]", optional: true);
    private static readonly PvfField<IReadOnlyList<int>> ForceResult = PvfField.Int32List("[Force Result Item Rule]", optional: true);

    private readonly Lazy<EquipmentCompoundContent> content = new(() => Read(provider.Archive));

    public EquipmentCompoundContent Load() => content.Value;

    private static EquipmentCompoundContent Read(PvfArchive archive)
    {
        var rules = PvfDocument.Open(archive, RulePath).AtRoot();
        var parts = rules.ReadAll(Part).ToFrozenDictionary(
            row =>
            {
                PvfField.Require(row.Count >= 3 && row[0].Kind == PvfTokenKind.Int32 && row[0].Value is >= 1 and <= 15
                    && row.Skip(1).All(x => x.IsString), RulePath, Part.Name, "invalid part row");
                return row[0].Value;
            },
            row => (IReadOnlySet<string>)row.Skip(2).Select(x => Normalize(archive.ResolveString(x.Value))).ToFrozenSet());
        var knownGroups = parts.Values.SelectMany(x => x).ToFrozenSet();
        var materials = ReadMaterials(rules, false)
            .Concat(ReadMaterials(rules, true)).ToFrozenDictionary();
        var multipliers = ReadMultipliers(rules, false)
            .Concat(ReadMultipliers(rules, true)).ToFrozenDictionary();
        var levelRows = PvfDocument.Open(archive, LevelPath).AtRoot().Read(PvfField.Int32List("[regen level limit]"));
        PvfField.Require(levelRows.Count > 0 && levelRows.Count % 4 == 0, LevelPath, "invalid compound level table");
        var levels = levelRows.Chunk(4).GroupBy(row => row[0]).ToFrozenDictionary(x => x.Key,
            x => (IReadOnlyList<EquipmentCompoundLevel>)Array.AsReadOnly(x.Select(row =>
            {
                PvfField.Require(row[1] > 0 && row[2] >= 0 && row[3] >= 0 && (row[2] > 0 || row[3] > 0),
                    LevelPath, "invalid compound level row");
                return new EquipmentCompoundLevel(row[1], row[2], row[3]);
            }).OrderByDescending(row => row.Level).ToArray()));
        var excluded = rules.Read(PvfField.Int32List("[except item index]")).ToFrozenSet();
        var weights = rules.Read(PvfField.Numbers("[probability exception handling]"));
        PvfField.Require(weights.Count % 2 == 0, RulePath, "invalid compound exception weights");
        var itemWeights = weights.Chunk(2).ToFrozenDictionary(x =>
        {
            PvfField.Require(x[0] > 0 && x[0] <= int.MaxValue && x[0] == Math.Truncate(x[0]) && x[1] > 0,
                RulePath, "invalid weighted item");
            return (int)x[0];
        }, x => x[1]);
        var legacyWeight = rules.Read(PvfField.Number("[probability legacy]"));
        PvfField.Require(legacyWeight >= 0, RulePath, "invalid legacy weight");

        var templates = new Dictionary<int, EquipmentCompoundTemplate>();
        foreach (var (id, path) in PvfListFile.Paths(archive, IndexPath, prefix: "equipment/"))
        {
            if (excluded.Contains(id)) continue;
            if (ReadTemplate(archive, id, path, knownGroups) is { } template) templates.Add(id, template);
        }

        return new(parts, excluded, itemWeights, legacyWeight, materials, multipliers, levels,
            templates.ToFrozenDictionary(), templates.Values.GroupBy(x => (x.Rarity, x.Level))
                .ToFrozenDictionary(x => x.Key,
                    x => (IReadOnlyList<EquipmentCompoundTemplate>)Array.AsReadOnly(x.OrderBy(y => y.ItemId).ToArray())));
    }

    private static EquipmentCompoundTemplate? ReadTemplate(PvfArchive archive, int id, string path, IReadOnlySet<string> knownGroups)
    {
        var item = PvfDocument.TryOpen(archive, path, PvfPreamble.Ignore)?.AtRoot();
        if (item is null) return null;
        var group = Normalize(item.ReadAll(ItemGroup).FirstOrDefault() ?? "");
        if (!knownGroups.Contains(group)) return null;
        var rarity = item.ReadAll(Rarity).FirstOrDefault(-1);
        if (rarity is not (2 or 3 or 6)) return null;
        var level = item.ReadAll(MinimumLevel).FirstOrDefault();
        var attach = Normalize(item.ReadAll(AttachType).FirstOrDefault() ?? "");
        if (level <= 0 || attach != "sealing") return null;
        var legacy = item.ReadAll(Categories).SelectMany(x => x).Any(x => Normalize(x) == "legacy");
        var rate = item.ReadAll(CreationRate).FirstOrDefault();
        var force = item.ReadAll(ForceResult).FirstOrDefault();
        var generated = rarity == 3 && rate == 0 && force is { Count: >= 2 } && force[0] == 1 && force[1] == 0;
        return new(id, rarity, level, group, legacy, attach, rate, generated);
    }

    private static Dictionary<(int, bool), IReadOnlyList<RecipeAmount>> ReadMaterials(PvfDocument document, bool directed)
    {
        var name = directed ? "[compound need material for specific]" : "[compound need material for random]";
        var values = document.Read(PvfField.Int32List(name));
        var result = new Dictionary<(int, bool), IReadOnlyList<RecipeAmount>>();
        for (var i = 0; i < values.Count;)
        {
            PvfField.Require(i + 1 < values.Count, RulePath, name, "truncated material header");
            var group = values[i++];
            var count = values[i++];
            PvfField.Require(group > 0 && count > 0 && count <= (values.Count - i) / 2, RulePath, name, "invalid material header");
            var materials = new RecipeAmount[count];
            for (var row = 0; row < count; row++)
            {
                var id = values[i++];
                var amount = values[i++];
                PvfField.Require(id > 0 && amount > 0, RulePath, name, "invalid material row");
                materials[row] = new(id, amount);
            }
            result.Add((group, directed), Array.AsReadOnly(materials));
        }
        return result;
    }

    private static Dictionary<(int, bool), IReadOnlyDictionary<int, int>> ReadMultipliers(PvfDocument document, bool directed)
    {
        var name = directed ? "[compound specific apply cost by level]" : "[compound random apply cost by level]";
        var values = document.Read(PvfField.Tokens(name));
        PvfField.Require(values.Count > 0 && values.Count % 3 == 0, RulePath, name, "invalid compound cost table");
        var rows = values.Chunk(3).Select(row =>
        {
            // The archive encodes the multiplier as a Single even for whole numbers.
            var multiplier = row[2].Kind switch
            {
                PvfTokenKind.Single => (double)row[2].AsSingle(),
                PvfTokenKind.Int32 => row[2].Value,
                _ => double.NaN,
            };
            PvfField.Require(row[0].Kind == PvfTokenKind.Int32 && row[0].Value > 0
                && row[1].Kind == PvfTokenKind.Int32 && row[1].Value > 0
                && multiplier > 0 && multiplier <= int.MaxValue && multiplier == Math.Truncate(multiplier),
                RulePath, name, "invalid compound cost row");
            return (Rarity: row[0].Value, Level: row[1].Value, Multiplier: (int)multiplier);
        });
        return rows.GroupBy(row => row.Rarity).ToDictionary(g => (g.Key, directed),
            g => (IReadOnlyDictionary<int, int>)g.ToFrozenDictionary(row => row.Level, row => row.Multiplier));
    }

    private static string Normalize(string text) => text.Trim().Trim('[', ']').Trim().ToLowerInvariant();
}
