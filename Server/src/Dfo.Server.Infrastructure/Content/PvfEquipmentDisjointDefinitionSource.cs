using Dfo.Pvf.Schema;
using Dfo.Pvf;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Infrastructure.Content;

/// <summary>Reads system-disjoint inputs from the archive; it does not perform a roll.</summary>
internal sealed class PvfEquipmentDisjointDefinitionSource(PvfArchiveProvider provider)
    : IEquipmentDisjointDefinitionSource, Dfo.Server.Abstractions.Content.IContentWarmup
{
    private static class Fields
    {
        internal static readonly PvfField<IReadOnlyList<PvfToken>> AllSkillItemContainerField = PvfField.Tokens("[all skill item container]", optional: true);
        internal static readonly PvfField<IReadOnlyList<PvfToken>> AllSkillItemField = PvfField.Tokens("[all skill item]", optional: true);
        internal static readonly PvfField<IReadOnlyList<PvfToken>> SkillApplyConditionField = PvfField.Tokens("[skill apply condition]", optional: true);
        internal static readonly PvfField<IReadOnlyList<PvfToken>> ExtraConditionField = PvfField.Tokens("[extra condition]", optional: true);

        internal static readonly PvfField<int> ValueField = PvfField.Int32("[value]", optional: true);
        internal static readonly PvfField<int> GradeField = PvfField.Int32("[grade]", optional: true);
        internal static readonly PvfField<int> RarityField = PvfField.Int32("[rarity]", optional: true);
        internal static readonly PvfField<int> RandomOptionField = PvfField.Int32("[random option]", optional: true);
        internal static readonly PvfField<int> MinimumLevelField = PvfField.Int32("[minimum level]", optional: true);
        internal static readonly PvfField<int> PortableDisjointField = PvfField.Int32("[portable disjoint]", optional: true);
        internal static readonly PvfField<IReadOnlyList<PvfToken>> PortableDisjointInfoField = PvfField.Tokens("[portable disjoint info]", optional: true);
        internal static readonly PvfField<IReadOnlyList<double>> CubeCreationConstField = PvfField.Numbers("[cube creation const]", optional: true);
        internal static readonly PvfField<IReadOnlyList<int>> AdditionalResultField = PvfField.Int32List("[additional result]", optional: true);
        internal static readonly PvfField<IReadOnlyList<double>> AdditionalResultConstField = PvfField.Numbers("[additional result const]", optional: true);
        internal static readonly PvfField<IReadOnlyList<int>> AdditionalResultExpandField = PvfField.Int32List("[additional result expand]", optional: true);
        internal static readonly PvfField<IReadOnlyList<double>> AdditionalResultExpandConstField = PvfField.Numbers("[additional result expand const]", optional: true);
        internal static readonly PvfField<IReadOnlyList<string>> ImpossibleContentsField = PvfField.Strings("[impossible contents]", optional: true);
        internal static readonly PvfField<string> AttachTypeField = PvfField.String("[attach type]", optional: true);
        internal static readonly PvfField<IReadOnlyList<PvfToken>> CubeIndexField = PvfField.Tokens("[cube index]", optional: true);
    }

    private readonly Lock gate = new();
    private readonly Dictionary<int, EquipmentDisjointDefinition?> equipmentCache = [];
    private readonly Dictionary<int, EquipmentDestructionDefinition?> destructionCache = [];
    private readonly Dictionary<int, PortableDisjointMachineDefinition?> portableMachineCache = [];
    private IReadOnlyDictionary<int, string>? equipmentPaths;
    private IReadOnlyDictionary<int, string>? stackablePaths;
    private IReadOnlyDictionary<int, int>? portableLevelLimits;
    private int? equipmentSaleRate;
    private bool tableRead;
    private EquipmentDisjointTable? table;

    public string Name => "equipment-disjoint";

    public async ValueTask<int> WarmAsync(CancellationToken cancellationToken)
    {
        await GetSystemTableAsync(cancellationToken).ConfigureAwait(false);
        lock (gate)
        {
            var archive = provider.Archive;
            EquipmentPathsIndex(archive);
            StackablePathsIndex(archive);
            EquipmentSaleRateIndex();
            PortableLevelLimitsIndex();
            return equipmentPaths.Count + stackablePaths.Count + portableLevelLimits.Count;
        }
    }

    [System.Diagnostics.CodeAnalysis.MemberNotNull(nameof(equipmentPaths))]
    private void EquipmentPathsIndex(Dfo.Pvf.PvfArchive archive)
    {
        equipmentPaths ??= PvfListFile.Paths(archive, "equipment/equipment.lst");
    }

    [System.Diagnostics.CodeAnalysis.MemberNotNull(nameof(stackablePaths))]
    private void StackablePathsIndex(Dfo.Pvf.PvfArchive archive)
    {
        stackablePaths ??= PvfListFile.Paths(archive, "stackable/stackable.lst");
    }

    [System.Diagnostics.CodeAnalysis.MemberNotNull(nameof(portableLevelLimits))]
    private void PortableLevelLimitsIndex()
    {
        portableLevelLimits ??= ReadPortableLevelLimits();
    }

    private void EquipmentSaleRateIndex()
    {
        equipmentSaleRate ??= ReadEquipmentSaleRate();
    }

    public ValueTask<EquipmentDestructionDefinition?> GetDestructionEquipmentAsync(
        int itemId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (!destructionCache.TryGetValue(itemId, out var definition))
                destructionCache[itemId] = definition = ReadDestructionEquipment(itemId);
            return ValueTask.FromResult(definition);
        }
    }

    private EquipmentDestructionDefinition? ReadDestructionEquipment(int itemId)
    {
        var archive = provider.Archive;
        EquipmentPathsIndex(archive);
        if (!equipmentPaths.TryGetValue(itemId, out var relative)) return null;
        var sections = DestructionRootSections(
            PvfNpcShopDefinitionSource.Sections(archive, "equipment/" + relative));
        if (sections is null) return null;
        var value = OneInt(archive, sections, Fields.ValueField);
        var grade = OneInt(archive, sections, Fields.GradeField);
        var rarity = OneInt(archive, sections, Fields.RarityField);
        var randomOption = OneInt(archive, sections, Fields.RandomOptionField);
        if (sections.Any(section => section.Name == Fields.RandomOptionField.Name) && randomOption is not (0 or 1))
            return null;
        var prices = PvfNpcShopDefinitionSource.Sections(archive, "equipment/pricetable.tbl");
        if (value is not >= 0 || grade is not >= 0 || rarity is not >= 0
            || prices.Count == 0 || prices[0].Values.Count != 3
            || prices[0].Values.Any(token => token.Kind != PvfTokenKind.Int32 || token.Value < 0))
            return null;
        // 70 CEquipItem::IsRandomOption selects the third rate; ordinary equipment uses the first.
        return new(value.Value, grade.Value, rarity.Value, prices[0].Values[randomOption == 1 ? 2 : 0].Value);
    }

    private static IReadOnlyList<PvfNpcShopDefinitionSource.Section>? DestructionRootSections(
        IReadOnlyList<PvfNpcShopDefinitionSource.Section> sections)
    {
        // Equipment attributes and skill-condition attributes share names such as [value].
        // Explicit [/tag] delimiters identify structured scopes; retain only root attributes.
        // Known skill containers must also close when a malformed script omits their final delimiter.
        var containers = sections.Where(section => section.Name.StartsWith("[/", StringComparison.Ordinal)
                && section.Name.EndsWith(']'))
            .Select(section => "[" + section.Name[2..]).ToHashSet(StringComparer.Ordinal);
        containers.UnionWith([Fields.AllSkillItemContainerField.Name, Fields.AllSkillItemField.Name,
            Fields.SkillApplyConditionField.Name, Fields.ExtraConditionField.Name]);
        var scope = new Stack<string>();
        var roots = new List<PvfNpcShopDefinitionSource.Section>();
        foreach (var section in sections)
        {
            if (section.Name.StartsWith("[/", StringComparison.Ordinal))
            {
                if (scope.Count == 0 || "[" + section.Name[2..] != scope.Pop()) return null;
                continue;
            }
            if (scope.Count == 0) roots.Add(section);
            if (containers.Contains(section.Name)) scope.Push(section.Name);
        }
        return scope.Count == 0 ? roots : null;
    }

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

    public ValueTask<PortableDisjointMachineDefinition?> GetPortableMachineAsync(
        int itemId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (!portableMachineCache.TryGetValue(itemId, out var definition))
                portableMachineCache[itemId] = definition = ReadPortableMachine(itemId);
            return ValueTask.FromResult(definition);
        }
    }

    private EquipmentDisjointDefinition? ReadEquipment(int itemId)
    {
        var archive = provider.Archive;
        EquipmentPathsIndex(archive);
        if (!equipmentPaths.TryGetValue(itemId, out var relative))
            return null;
        var sections = PvfNpcShopDefinitionSource.Sections(archive, "equipment/" + relative);
        var value = OneInt(archive, sections, Fields.ValueField);
        var rarity = OneInt(archive, sections, Fields.RarityField);
        var minimumLevel = OneInt(archive, sections, Fields.MinimumLevelField);
        EquipmentSaleRateIndex();
        if (value is not { } equipmentValue || equipmentValue < 0
            || rarity is not { } equipmentRarity || equipmentRarity < 0
            || minimumLevel is not { } equipmentLevel || equipmentLevel < 0
            || equipmentSaleRate is not { } saleRate || saleRate < 0)
            return null;

        var cannotDisjoint = sections
            .Where(section => section.Name == Fields.ImpossibleContentsField.Name)
            .SelectMany(section => section.Values)
            .Select(valueToken => Text(archive, valueToken))
            .Any(valueText => Normalize(valueText) == "disjoint");
        var tradeDelete = sections
            .Where(section => section.Name == Fields.AttachTypeField.Name)
            .SelectMany(section => section.Values)
            .Select(valueToken => Text(archive, valueToken))
            .Any(valueText => Normalize(valueText) == "trade delete");

        var sellGold = Math.Max(1, checked((int)((long)equipmentValue * saleRate / 1000)));
        return new(sellGold, equipmentLevel, equipmentRarity, !cannotDisjoint && !tradeDelete);
    }

    private PortableDisjointMachineDefinition? ReadPortableMachine(int itemId)
    {
        var archive = provider.Archive;
        StackablePathsIndex(archive);
        if (!stackablePaths.TryGetValue(itemId, out var relative))
            return null;

        var grade = OneInt(archive, PvfNpcShopDefinitionSource.Sections(
            archive, "stackable/" + relative), Fields.PortableDisjointField);
        PortableLevelLimitsIndex();
        return grade is { } value
            && portableLevelLimits.TryGetValue(value, out var maximumLevel)
            && maximumLevel > 0
                ? new(value, maximumLevel)
                : null;
    }

    private IReadOnlyDictionary<int, int> ReadPortableLevelLimits()
    {
        var values = Integers(One(PvfNpcShopDefinitionSource.Sections(
            provider.Archive, "character/expertjob/disjointer.exj"),
            Fields.PortableDisjointInfoField));
        if (values.Count == 0 || values.Count % 2 != 0)
            return new Dictionary<int, int>();

        var result = new Dictionary<int, int>();
        for (var index = 0; index < values.Count; index += 2)
        {
            if (values[index] < 0 || values[index + 1] <= 0
                || !result.TryAdd(values[index], values[index + 1]))
                return new Dictionary<int, int>();
        }
        return result;
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
        var cubeIndices = sections.Where(section => section.Name == Fields.CubeIndexField.Name).ToArray();
        foreach (var row in cubeIndices)
            if (row.Values.Count < 2 || !row.Values[0].IsString || row.Values.Skip(1).Any(token => token.Kind != PvfTokenKind.Int32))
                throw new InvalidDataException($"PVF 'etc/disjoint.etc', field '{Fields.CubeIndexField.Name}': expected element string and Int32 item IDs.");
        var noElement = cubeIndices.Where(row => Normalize(Text(provider.Archive, row.Values[0])) == "no element").ToArray();
        if (noElement.Length > 1) throw new InvalidDataException($"PVF 'etc/disjoint.etc', field '{Fields.CubeIndexField.Name}': duplicate no-element row.");
        var cubeItem = noElement.Length == 1 ? noElement[0].Values[1].Value : 0;
        var cube = Numbers(One(sections, Fields.CubeCreationConstField));
        var additional = Integers(One(sections, Fields.AdditionalResultField));
        var additionalConstants = Numbers(One(sections, Fields.AdditionalResultConstField));
        var expand = Integers(One(sections, Fields.AdditionalResultExpandField));
        var expandConstants = Numbers(One(sections, Fields.AdditionalResultExpandConstField));
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
        IReadOnlyList<PvfNpcShopDefinitionSource.Section> sections, PvfField name)
    {
        var matches = sections.Where(section => section.Name == name.Name).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private static int? OneInt(
        PvfArchive archive, IReadOnlyList<PvfNpcShopDefinitionSource.Section> sections, PvfField<int> name)
    {
        var section = One(sections, name);
        return section is { Values.Count: 1 } && section.Values[0].Kind == PvfTokenKind.Int32
            ? name.Decode(archive, section.Values, "equipment") : null;
    }

    private static List<int> Integers(PvfNpcShopDefinitionSource.Section? section)
    {
        if (section is null) return [];
        if (section.Values.Any(value => value.Kind != PvfTokenKind.Int32))
            throw new InvalidDataException($"PVF 'etc/disjoint.etc', field '{section.Name}': expected Int32 list.");
        return section.Values.Select(value => value.Value).ToList();
    }

    private static List<double> Numbers(PvfNpcShopDefinitionSource.Section? section)
    {
        if (section is null) return [];
        var result = new List<double>();
        foreach (var token in section.Values)
        {
            var value = token.Kind switch { PvfTokenKind.Int32 => (double)token.Value,
                PvfTokenKind.Single => token.AsSingle(), _ => double.NaN };
            if (!double.IsFinite(value)) throw new InvalidDataException($"PVF equipment disjoint, field '{section.Name}': expected finite number list.");
            result.Add(value);
        }
        return result;
    }

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
