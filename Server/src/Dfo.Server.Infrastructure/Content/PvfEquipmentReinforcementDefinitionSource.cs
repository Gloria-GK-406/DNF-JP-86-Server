using Dfo.Pvf.Schema;
using Dfo.Pvf;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Infrastructure.Content;

/// <summary>Reads separate reinforcement and amplification declarations, without applying a roll.</summary>
internal sealed class PvfEquipmentReinforcementDefinitionSource(PvfArchiveProvider provider)
    : IEquipmentReinforcementDefinitionSource
{
    private static class Fields
    {
        internal static readonly PvfField<EquipmentUpgradeTicketDefinition> ReinforcementTicket =
            PvfField.Custom("[equipment reinforcement ticket]", (archive, values, path) =>
                DecodeTicket(archive, values, path, "[equipment reinforcement ticket]", EquipmentUpgradeKind.Reinforcement), optional: true);
        internal static readonly PvfField<EquipmentUpgradeTicketDefinition> AmplificationTicket =
            PvfField.Custom("[equipment amplify reinforcement ticket]", (archive, values, path) =>
                DecodeTicket(archive, values, path, "[equipment amplify reinforcement ticket]", EquipmentUpgradeKind.Amplification), optional: true);
        internal static readonly PvfField<IReadOnlyList<PvfToken>> ActionTypeField = PvfField.Tokens("[action type]", optional: true);
        internal static readonly PvfField<int> NoticeField = PvfField.Int32("[notice]", optional: true);
        internal static readonly PvfField<IReadOnlyList<double>> CostField = PvfField.Numbers("[cost]", optional: true);
        internal static readonly PvfField<IReadOnlyList<PvfToken>> CostWeightsByRarityField = PvfField.Tokens("[cost weights by rarity]", optional: true);
        internal static readonly PvfField<IReadOnlyList<double>> TypeField = PvfField.Numbers("[type]", optional: true);
        internal static readonly PvfField<IReadOnlyList<double>> CostWeightByUpgradeLevelField = PvfField.Numbers("[cost weight by upgrade level]", optional: true);
        internal static readonly PvfField<IReadOnlyList<PvfToken>> DestroyLevelByRarityField = PvfField.Tokens("[destroy level by rarity]", optional: true);
        internal static readonly PvfField<IReadOnlyList<PvfToken>> MaxUpgradeLevelByRarityField = PvfField.Tokens("[max upgrade level by rarity]", optional: true);
        internal static readonly PvfField<int> DisjointBonusItemField = PvfField.Int32("[disjoint bonus item]", optional: true);
        internal static readonly PvfField<int> EquipLevelConstField = PvfField.Int32("[equip level const]", optional: true);
        internal static readonly PvfField<int> UpgradeConstForBonusItemCountField = PvfField.Int32("[upgrade const for bonus item count]", optional: true);
        internal static readonly PvfField<IReadOnlyList<PvfToken>> UpgradeFailedBonusWeightByRarityField = PvfField.Tokens("[upgrade failed bonus weight by rarity]", optional: true);
        internal static readonly PvfField<IReadOnlyList<PvfToken>> CorrectionGradeByRarityField = PvfField.Tokens("[correction grade by rarity]", optional: true);
        internal static readonly PvfField<bool> CloseActionTypeField = PvfField.Marker("[/action type]", optional: true);
        internal static readonly PvfField<IReadOnlyList<double>> TableField = PvfField.Numbers("[table]", optional: true);
        internal static readonly PvfField<string> TableTypeField = PvfField.String("[table type]", optional: true);
        internal static readonly PvfField<bool> CloseTableField = PvfField.Marker("[/table]", optional: true);
        internal static readonly PvfField<IReadOnlyList<int>> DisjointField = PvfField.Int32List("[disjoint]", optional: true);
        internal static readonly PvfField<bool> CloseDisjointField = PvfField.Marker("[/disjoint]", optional: true);
    }

    private static readonly IReadOnlyDictionary<string, int> Rarities = new Dictionary<string, int>(
        StringComparer.OrdinalIgnoreCase)
    {
        ["common"] = 0,
        ["uncommon"] = 1,
        ["rare"] = 2,
        ["unique"] = 3,
        ["epic"] = 4,
        ["chronicle"] = 5,
        ["legendary"] = 6,
    };

    private readonly Lock gate = new();
    private EquipmentReinforcementTable? table;
    private bool tableRead;
    private EquipmentReinforcementTable? amplificationTable;
    private Dictionary<int, string>? stackablePaths;
    private readonly Dictionary<(int ItemId, EquipmentUpgradeKind Kind), EquipmentReinforcementProtectionDefinition?> protectionCache = [];
    private readonly Dictionary<int, EquipmentUpgradeTicketDefinition?> ticketCache = [];

    public ValueTask<EquipmentUpgradeTicketDefinition?> GetTicketAsync(int itemId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (ticketCache.TryGetValue(itemId, out var cached)) return ValueTask.FromResult(cached);
            var archive = provider.Archive;
            stackablePaths ??= ReadPaths(archive, "stackable/stackable.lst");
            if (!stackablePaths.TryGetValue(itemId, out var path))
                return ValueTask.FromResult(ticketCache[itemId] = null);
            var document = PvfDocument.Open(archive, "stackable/" + path).AtRoot();
            var normal = document.TryRead(Fields.ReinforcementTicket, out var reinforcement);
            var amplify = document.TryRead(Fields.AmplificationTicket, out var amplification);
            if (normal && amplify)
                throw new InvalidDataException($"PVF 'stackable/{path}': both upgrade ticket kinds are declared.");
            return ValueTask.FromResult(ticketCache[itemId] = normal ? reinforcement : amplify ? amplification : null);
        }
    }

    private static EquipmentUpgradeTicketDefinition DecodeTicket(PvfArchive archive, IReadOnlyList<PvfToken> values,
        string path, string field, EquipmentUpgradeKind kind)
    {
        if (values.Count != 4 || values[0].Kind != PvfTokenKind.Int32 || values[1].Kind != PvfTokenKind.Int32
            || !values[2].IsString || values[3].Kind != PvfTokenKind.Int32
            || values[0].Value is < 0 or > 31 || values[1].Value is < 0 or > 100)
            throw new InvalidDataException($"PVF '{path}', field '{field}': expected level 0..31, percent 0..100, mode string, Int32 apply value.");
        return new(kind, (byte)values[0].Value, values[1].Value, archive.ResolveString(values[2].Value), values[3].Value);
    }

    public ValueTask<EquipmentReinforcementTable?> GetNormalTableAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (!tableRead)
            {
                table = ReadTable(provider.Archive, "etc/upgrade.etc", "normal");
                tableRead = true;
            }
            return ValueTask.FromResult(table);
        }
    }

    public ValueTask<EquipmentReinforcementTable?> GetAmplificationTableAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            amplificationTable ??= ReadTable(provider.Archive, "etc/amplifyupgrade.etc", "amplify")
                ?? throw new InvalidDataException("PVF etc/amplifyupgrade.etc: invalid amplification table or cost/failure declarations.");
            return ValueTask.FromResult<EquipmentReinforcementTable?>(amplificationTable);
        }
    }

    public ValueTask<EquipmentReinforcementProtectionDefinition?> GetProtectionAsync(
        int itemId, CancellationToken cancellationToken,
        EquipmentUpgradeKind kind = EquipmentUpgradeKind.Reinforcement)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (!protectionCache.TryGetValue((itemId, kind), out var definition))
                protectionCache[(itemId, kind)] = definition = ReadProtection(itemId, kind);
            return ValueTask.FromResult(definition);
        }
    }

    private EquipmentReinforcementProtectionDefinition? ReadProtection(int itemId, EquipmentUpgradeKind kind)
    {
        var archive = provider.Archive;
        stackablePaths ??= ReadPaths(archive, "stackable/stackable.lst");
        if (!stackablePaths.TryGetValue(itemId, out var path))
            return null;
        var sections = PvfNpcShopDefinitionSource.Sections(archive, "stackable/" + path);
        var action = One(sections, Fields.ActionTypeField);
        var actionIndex = sections.ToList().FindIndex(section => section.Name == Fields.ActionTypeField.Name);
        if (action is null || actionIndex < 0 || actionIndex + 1 >= sections.Count
            || sections[actionIndex + 1].Name != Fields.CloseActionTypeField.Name
            || action.Values.Count == 0
            || !TryName(archive, action.Values[0], out var actionName))
            return null;
        return actionName switch
        {
            "protect equipment" when kind == EquipmentUpgradeKind.Reinforcement && action.Values.Count == 1 =>
                new(EquipmentReinforcementProtectionKind.ResetToZero, 0),
            "new protect equipment" when kind == EquipmentUpgradeKind.Reinforcement && action.Values.Count == 2
                && action.Values[1].Kind == PvfTokenKind.Int32
                && action.Values[1].Value > 0 =>
                new(EquipmentReinforcementProtectionKind.RetainLevel, action.Values[1].Value),
            "amplify protect equipment" when kind == EquipmentUpgradeKind.Amplification && action.Values.Count == 1 =>
                new(EquipmentReinforcementProtectionKind.ResetToZero, 0),
            "new amplify protect equipment" when kind == EquipmentUpgradeKind.Amplification && action.Values.Count == 2
                && action.Values[1].Kind == PvfTokenKind.Int32 && action.Values[1].Value > 0 =>
                new(EquipmentReinforcementProtectionKind.RetainLevel, action.Values[1].Value),
            _ => null,
        };
    }

    private static EquipmentReinforcementTable? ReadTable(PvfArchive archive, string path, string tableName)
    {
        var sections = PvfNpcShopDefinitionSource.Sections(archive, path);
        PvfNpcShopDefinitionSource.Section? normal = null;
        for (var index = 0; index < sections.Count; index++)
        {
            if (sections[index].Name != Fields.TableField.Name) continue;
            if (index + 2 >= sections.Count || sections[index + 1].Name != Fields.TableTypeField.Name
                || sections[index + 2].Name != Fields.CloseTableField.Name)
                return null;
            var tableType = sections[index + 1];
            if (tableType.Values.Count == 0 || !TryName(archive, tableType.Values[0], out var name))
                return null;
            if (name == tableName)
            {
                if (normal is not null) return null;
                normal = tableType;
            }
            index += 2;
        }
        if (normal is null || normal.Values.Count <= 1 || (normal.Values.Count - 1) % 19 != 0)
            return null;

        var rows = new List<EquipmentReinforcementRow>();
        for (var start = 1; start < normal.Values.Count; start += 19)
        {
            var columns = new double[19];
            for (var column = 0; column < 19; column++)
                if (!TryNumber(normal.Values[start + column], out columns[column]) || !double.IsFinite(columns[column]))
                    return null;
            var target = rows.Count + 1;
            if (!TryInt(normal.Values[start + 7], out var failure) || failure is < 0 or > 100000
                || !TryInt(normal.Values[start + 8], out var penalty) || penalty is < 1 or > 3
                || !TryInt(normal.Values[start + 9], out var penaltyValue) || penaltyValue < 0
                || !TryInt(normal.Values[start + 10], out var material) || material <= 0
                || !TryInt(normal.Values[start + 11], out var count) || count <= 0)
                return null;
            rows.Add(new(target, Array.AsReadOnly(columns), failure, penalty,
                penaltyValue, material, count));
        }

        var notice = OneInt(archive, sections, Fields.NoticeField);
        var baseGold = IntList(One(sections, Fields.CostField), minimumCount: 2, allowZero: true);
        var rarityWeights = NumberList(One(sections, Fields.CostWeightsByRarityField), 7, allowZero: false);
        var typeWeights = NumberList(One(sections, Fields.TypeField), 12, allowZero: true);
        // A21 amplification omits this optional factor entirely; each level has neutral weight.
        var costSection = One(sections, Fields.CostWeightByUpgradeLevelField);
        var costByLevel = tableName == "amplify" && !sections.Any(section => section.Name == Fields.CostWeightByUpgradeLevelField.Name)
            ? new Dictionary<int, double>() : IntDoubleMap(costSection);
        var destroyLevels = RarityIntMap(archive, One(sections, Fields.DestroyLevelByRarityField));
        var maxLevels = RarityIntMap(archive, One(sections, Fields.MaxUpgradeLevelByRarityField));
        var bonus = ReadDestroyBonus(archive, sections);
        if (notice is not >= 0 || baseGold is null || rarityWeights is null
            || typeWeights is null || costByLevel is null || destroyLevels is null
            || maxLevels is null || bonus is null || baseGold.All(value => value == 0)
            || destroyLevels.Count == 0 || maxLevels.Count == 0
            || destroyLevels.Any(pair => maxLevels.TryGetValue(pair.Key, out var max) && pair.Value > max))
            return null;
        return new(rows.AsReadOnly(), Array.AsReadOnly(baseGold), Array.AsReadOnly(rarityWeights),
            Array.AsReadOnly(typeWeights), costByLevel, destroyLevels, maxLevels, bonus, notice.Value);
    }

    private static EquipmentReinforcementDestroyBonusDefinition? ReadDestroyBonus(
        PvfArchive archive, IReadOnlyList<PvfNpcShopDefinitionSource.Section> sections)
    {
        var start = sections.Select((section, index) => (section, index))
            .Where(pair => pair.section.Name == Fields.DisjointField.Name).ToArray();
        var end = sections.Select((section, index) => (section, index))
            .Where(pair => pair.section.Name == Fields.CloseDisjointField.Name).ToArray();
        if (start.Length != 1 || end.Length != 1 || end[0].index <= start[0].index)
            return null;
        var children = sections.Skip(start[0].index + 1)
            .Take(end[0].index - start[0].index - 1).ToArray();
        var itemId = OneInt(archive, children, Fields.DisjointBonusItemField);
        var level = OneInt(archive, children, Fields.EquipLevelConstField);
        var upgradeBase = OneInt(archive, children, Fields.UpgradeConstForBonusItemCountField);
        var weights = RarityDoubleMap(archive, One(children, Fields.UpgradeFailedBonusWeightByRarityField));
        var correction = RarityIntMap(archive, One(children, Fields.CorrectionGradeByRarityField),
            allowZero: true);
        return itemId is > 0 && level is > 0 && upgradeBase is >= 0
            && weights is { Count: > 0 } && correction is { Count: > 0 }
            ? new(itemId.Value, level.Value, upgradeBase.Value, weights, correction) : null;
    }

    private static Dictionary<int, string> ReadPaths(PvfArchive archive, string path)
    {
        if (archive.FindFile(path) < 0) return [];
        var paths = new Dictionary<int, string>();
        var ambiguous = new HashSet<int>();
        List<(int Id, string RelativePath)> entries;
        try { entries = PvfListFile.Read(archive, path); }
        catch (InvalidOperationException) { return []; }
        foreach (var (id, relativePath) in entries)
        {
            if (id <= 0 || ambiguous.Contains(id)) continue;
            if (string.IsNullOrWhiteSpace(relativePath) || !paths.TryAdd(id, relativePath))
            {
                paths.Remove(id);
                ambiguous.Add(id);
            }
        }
        return paths;
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
        return section is { Values.Count: 1 } && TryInt(section.Values[0], out _)
            ? name.Decode(archive, section.Values, "equipment reinforcement") : null;
    }

    private static int[]? IntList(
        PvfNpcShopDefinitionSource.Section? section, int minimumCount, bool allowZero)
    {
        if (section is null || section.Values.Count < minimumCount) return null;
        var values = new int[section.Values.Count];
        for (var index = 0; index < values.Length; index++)
            if (!TryInt(section.Values[index], out values[index])
                || values[index] < (allowZero ? 0 : 1)) return null;
        return values;
    }

    private static double[]? NumberList(
        PvfNpcShopDefinitionSource.Section? section, int minimumCount, bool allowZero)
    {
        if (section is null || section.Values.Count < minimumCount) return null;
        var values = new double[section.Values.Count];
        for (var index = 0; index < values.Length; index++)
            if (!TryNumber(section.Values[index], out values[index])
                || !double.IsFinite(values[index]) || values[index] < (allowZero ? 0 : double.Epsilon))
                return null;
        return values;
    }

    private static IReadOnlyDictionary<int, double>? IntDoubleMap(
        PvfNpcShopDefinitionSource.Section? section)
    {
        if (section is null || section.Values.Count == 0 || section.Values.Count % 2 != 0)
            return null;
        var values = new Dictionary<int, double>();
        for (var index = 0; index < section.Values.Count; index += 2)
        {
            if (!TryInt(section.Values[index], out var key) || key < 0
                || !TryNumber(section.Values[index + 1], out var weight)
                || !double.IsFinite(weight) || weight <= 0 || !values.TryAdd(key, weight))
                return null;
        }
        return values;
    }

    private static IReadOnlyDictionary<int, int>? RarityIntMap(
        PvfArchive archive, PvfNpcShopDefinitionSource.Section? section, bool allowZero = false)
    {
        if (section is null || section.Values.Count == 0 || section.Values.Count % 2 != 0)
            return null;
        var values = new Dictionary<int, int>();
        for (var index = 0; index < section.Values.Count; index += 2)
        {
            if (!TryName(archive, section.Values[index], out var name)
                || !Rarities.TryGetValue(name, out var rarity)
                || !TryInt(section.Values[index + 1], out var number)
                || number < (allowZero ? 0 : 1) || !values.TryAdd(rarity, number))
                return null;
        }
        return values;
    }

    private static IReadOnlyDictionary<int, double>? RarityDoubleMap(
        PvfArchive archive, PvfNpcShopDefinitionSource.Section? section)
    {
        if (section is null || section.Values.Count == 0 || section.Values.Count % 2 != 0)
            return null;
        var values = new Dictionary<int, double>();
        for (var index = 0; index < section.Values.Count; index += 2)
        {
            if (!TryName(archive, section.Values[index], out var name)
                || !Rarities.TryGetValue(name, out var rarity)
                || !TryNumber(section.Values[index + 1], out var number)
                || !double.IsFinite(number) || number < 0 || !values.TryAdd(rarity, number))
                return null;
        }
        return values;
    }

    private static bool TryInt(PvfToken token, out int value)
    {
        value = token.Value;
        return token.Kind == PvfTokenKind.Int32;
    }

    private static bool TryNumber(PvfToken token, out double value)
    {
        value = token.Kind switch
        {
            PvfTokenKind.Int32 => token.Value,
            PvfTokenKind.Single => token.AsSingle(),
            _ => double.NaN,
        };
        return double.IsFinite(value);
    }

    private static bool TryName(PvfArchive archive, PvfToken token, out string name)
    {
        name = token.IsString
            ? archive.ResolveString(token.Value).Trim().Trim('`').Trim().Trim('[', ']').ToLowerInvariant()
            : string.Empty;
        return name.Length > 0;
    }
}
