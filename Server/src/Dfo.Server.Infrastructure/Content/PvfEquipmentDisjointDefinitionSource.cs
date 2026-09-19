using Dfo.Pvf;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Infrastructure.Content;

/// <summary>Reads system-disjoint inputs from the archive; it does not perform a roll.</summary>
internal sealed class PvfEquipmentDisjointDefinitionSource(PvfArchiveProvider provider)
    : IEquipmentDisjointDefinitionSource
{
    private readonly Lock gate = new();
    private readonly Dictionary<int, EquipmentDisjointDefinition?> equipmentCache = [];
    private Dictionary<int, string>? equipmentPaths;
    private int? equipmentSaleRate;
    private bool tableRead;
    private EquipmentDisjointTable? table;

    public ValueTask<EquipmentDisjointDefinition?> GetEquipmentAsync(
        int itemId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (!equipmentCache.TryGetValue(itemId, out var definition))
                equipmentCache[itemId] = definition = ReadEquipment(itemId);
            return ValueTask.FromResult(definition);
        }
    }

    public ValueTask<EquipmentDisjointTable?> GetSystemTableAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (!tableRead)
            {
                table = ReadTable();
                tableRead = true;
            }
            return ValueTask.FromResult(table);
        }
    }

    private EquipmentDisjointDefinition? ReadEquipment(int itemId)
    {
        var archive = provider.Archive;
        equipmentPaths ??= PvfListFile.Read(archive, "equipment/equipment.lst")
            .ToDictionary(entry => entry.Id, entry => entry.RelativePath);
        if (!equipmentPaths.TryGetValue(itemId, out var relative))
            return null;
        var sections = PvfNpcShopDefinitionSource.Sections(archive, "equipment/" + relative);
        var value = OneInt(sections, "[value]");
        var rarity = OneInt(sections, "[rarity]");
        var minimumLevel = OneInt(sections, "[minimum level]");
        equipmentSaleRate ??= ReadEquipmentSaleRate();
        if (value is not { } equipmentValue || equipmentValue < 0
            || rarity is not { } equipmentRarity || equipmentRarity < 0
            || minimumLevel is not { } equipmentLevel || equipmentLevel < 0
            || equipmentSaleRate is not { } saleRate || saleRate < 0)
            return null;

        var cannotDisjoint = sections
            .Where(section => section.Name == "[impossible contents]")
            .SelectMany(section => section.Values)
            .Select(valueToken => Text(archive, valueToken))
            .Any(valueText => Normalize(valueText) == "disjoint");
        var tradeDelete = sections
            .Where(section => section.Name == "[attach type]")
            .SelectMany(section => section.Values)
            .Select(valueToken => Text(archive, valueToken))
            .Any(valueText => Normalize(valueText) == "trade delete");

        var sellGold = Math.Max(1, checked((int)((long)equipmentValue * saleRate / 1000)));
        return new(sellGold, equipmentLevel, equipmentRarity, !cannotDisjoint && !tradeDelete);
    }

    private int? ReadEquipmentSaleRate()
    {
        var sections = PvfNpcShopDefinitionSource.Sections(provider.Archive, "equipment/pricetable.tbl");
        return sections.Count > 0 && sections[0].Values.Count == 3
            && sections[0].Values.All(value => value.Kind == PvfTokenKind.Int32 && value.Value >= 0)
                ? sections[0].Values[0].Value
                : null;
    }

    private EquipmentDisjointTable? ReadTable()
    {
        var sections = PvfNpcShopDefinitionSource.Sections(provider.Archive, "etc/disjoint.etc");
        var cubeItem = sections
            .Where(section => section.Name == "[cube index]"
                && section.Values.Count > 1
                && Normalize(Text(provider.Archive, section.Values[0])) == "no element")
            .Select(section => section.Values.Skip(1)
                .FirstOrDefault(value => value.Kind == PvfTokenKind.Int32).Value)
            .FirstOrDefault();
        var cube = Numbers(One(sections, "[cube creation const]"));
        var additional = Integers(One(sections, "[additional result]"));
        var additionalConstants = Numbers(One(sections, "[additional result const]"));
        var expand = Integers(One(sections, "[additional result expand]"));
        var expandConstants = Numbers(One(sections, "[additional result expand const]"));
        if (cubeItem <= 0 || cube.Count < 2 || cube[0] <= 0
            || cube.Skip(1).Any(value => !double.IsFinite(value) || value < 0))
            return null;

        var additionalRows = new List<IReadOnlyList<int>>();
        for (var index = 0; index < additional.Count;)
        {
            var count = additional[index++];
            if (count < 0 || index + count > additional.Count)
                return null;
            var row = additional.Skip(index).Take(count).ToArray();
            if (row.Any(itemId => itemId <= 0))
                return null;
            additionalRows.Add(row);
            index += count;
        }

        var additionalRules = new List<EquipmentDisjointAdditionalRule>();
        var constantIndex = 0;
        for (var row = 0; row < additionalRows.Count && constantIndex < additionalConstants.Count; row++)
        {
            var remainingRows = additionalRows.Count - row - 1;
            if (additionalConstants.Count - constantIndex == 1 + remainingRows * 4)
            {
                additionalRules.Add(new(additionalConstants[constantIndex++], 0, 0, 0));
                continue;
            }
            if (constantIndex + 3 >= additionalConstants.Count)
                return null;
            additionalRules.Add(new(additionalConstants[constantIndex], additionalConstants[constantIndex + 1],
                additionalConstants[constantIndex + 2], additionalConstants[constantIndex + 3]));
            constantIndex += 4;
        }
        if (additionalRules.Count != additionalRows.Count || constantIndex != additionalConstants.Count)
            return null;

        if (expand.Count % 2 != 0)
            return null;
        var expandRows = new List<EquipmentDisjointExpandRule>();
        var expandConstantIndex = 0;
        for (var index = 0; index < expand.Count; index += 2)
        {
            var enabled = expand[index] > 0 && expand[index + 1] > 0;
            var remainingRows = (expand.Count - index) / 2 - 1;
            if (!enabled && expand[index + 1] <= 0
                && expandConstants.Count - expandConstantIndex == 2 + remainingRows * 3)
            {
                expandRows.Add(new(false, expand[index + 1], expandConstants[expandConstantIndex],
                    expandConstants[expandConstantIndex + 1], 0));
                expandConstantIndex += 2;
                continue;
            }
            if (expandConstantIndex + 2 >= expandConstants.Count)
                return null;
            expandRows.Add(new(enabled, expand[index + 1], expandConstants[expandConstantIndex],
                expandConstants[expandConstantIndex + 1], expandConstants[expandConstantIndex + 2]));
            expandConstantIndex += 3;
        }
        if (expandConstantIndex != expandConstants.Count)
            return null;

        return new(cubeItem, cube[0], cube.Skip(1).ToArray(), additionalRows,
            additionalRules, expandRows);
    }

    private static PvfNpcShopDefinitionSource.Section? One(
        IReadOnlyList<PvfNpcShopDefinitionSource.Section> sections, string name)
    {
        var matches = sections.Where(section => section.Name == name).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private static int? OneInt(
        IReadOnlyList<PvfNpcShopDefinitionSource.Section> sections, string name)
    {
        var section = One(sections, name);
        return section is { Values.Count: 1 } && section.Values[0].Kind == PvfTokenKind.Int32
            ? section.Values[0].Value : null;
    }

    private static List<int> Integers(PvfNpcShopDefinitionSource.Section? section) =>
        section is not null && section.Values.All(value => value.Kind == PvfTokenKind.Int32)
            ? section.Values.Select(value => value.Value).ToList() : [];

    private static List<double> Numbers(PvfNpcShopDefinitionSource.Section? section) =>
        section?.Values.Select(value => value.Kind switch
        {
            PvfTokenKind.Int32 => (double)value.Value,
            PvfTokenKind.Single => value.AsSingle(),
            _ => double.NaN,
        }).Where(double.IsFinite).ToList() ?? [];

    private static string? Text(PvfArchive archive, PvfToken token) =>
        token.IsString ? archive.ResolveString(token.Value) : null;

    private static string Normalize(string? value)
    {
        var normalized = value?.Trim().Trim('`').Trim() ?? string.Empty;
        if (normalized.Length >= 2 && normalized[0] == '[' && normalized[^1] == ']')
            normalized = normalized[1..^1];
        return normalized.Trim().ToLowerInvariant();
    }
}
