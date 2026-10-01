using Dfo.Pvf.Schema;
using Dfo.Pvf;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfEquipmentMagicSealContentSource(PvfArchiveProvider provider)
    : IEquipmentMagicSealContentSource
{
    private static class Fields
    {
        internal static readonly PvfField<IReadOnlyList<int>> UniqueQuantityField = PvfField.Int32List("[unique]", optional: true);
        internal static readonly PvfField<IReadOnlyList<int>> CommonQuantityField = PvfField.Int32List("[common]", optional: true);

        internal static readonly PvfField<int> RandomOptionField = PvfField.Int32("[random option]", optional: true);
        internal static readonly PvfField<int> RarityField = PvfField.Int32("[rarity]", optional: true);
        internal static readonly PvfField<int> MinimumLevelField = PvfField.Int32("[minimum level]", optional: true);
        internal static readonly PvfField<IReadOnlyList<PvfToken>> EquipmentTypeField = PvfField.Tokens("[equipment type]", optional: true);
        internal static readonly PvfField<IReadOnlyList<PvfToken>> ChooseOptionGroupField = PvfField.Tokens("[choose option group]", optional: true);
        internal static readonly PvfField<IReadOnlyList<PvfToken>> ModifiedOptionSelectionField = PvfField.Tokens("[modified option selection]", optional: true);
        internal static readonly PvfField<int> UnsealScrollLevelLimitField = PvfField.Int32("[unseal scroll level limit]", optional: true);
        internal static readonly PvfField<IReadOnlyList<int>> BreakSealCostField = PvfField.Int32List("[break seal cost]", optional: true);
        internal static readonly PvfField<IReadOnlyList<int>> OptionModificationField = PvfField.Int32List("[option modification]", optional: true);
        internal static readonly PvfField<IReadOnlyList<int>> OptionGroupField = PvfField.Int32List("[option group]", optional: true);
        internal static readonly PvfField<int> LevelField = PvfField.Int32("[level]", optional: true);
        internal static readonly PvfField<bool> DungeonScopeField = PvfField.Marker("[dungeon]", optional: true);
        internal static readonly PvfField<bool> PvpScopeField = PvfField.Marker("[pvp]", optional: true);
        internal static readonly PvfField<int> OptionField = PvfField.Int32("[option]", optional: true);
        internal static readonly PvfField<bool> CloseLevelField = PvfField.Marker("[/level]", optional: true);
        internal static readonly PvfField<IReadOnlyList<double>> StuckField = PvfField.Numbers("[stuck]", optional: true);
        internal static readonly PvfField<IReadOnlyList<double>>[] ValueRangeFields =
        [
            PvfField.Numbers("[physical attack]"), PvfField.Numbers("[magical attack]"),
            PvfField.Numbers("[physical defense]"), PvfField.Numbers("[magical defense]"),
            PvfField.Numbers("[equipment physical attack]"), PvfField.Numbers("[equipment magical attack]"),
            PvfField.Numbers("[physical critical hit]"), PvfField.Numbers("[magical critical hit]"),
            PvfField.Numbers("[attack speed]"), PvfField.Numbers("[cast speed]"), PvfField.Numbers("[move speed]"),
            PvfField.Numbers("[fire attack]"), PvfField.Numbers("[water attack]"),
            PvfField.Numbers("[dark attack]"), PvfField.Numbers("[light attack]"), PvfField.Numbers("[all elemental attack]"),
            PvfField.Numbers("[fire resistance]"), PvfField.Numbers("[water resistance]"),
            PvfField.Numbers("[dark resistance]"), PvfField.Numbers("[light resistance]"), PvfField.Numbers("[all elemental resistance]"),
            PvfField.Numbers("[HP MAX]"), PvfField.Numbers("[MP MAX]"), PvfField.Numbers("[stuck resistance]"),
            PvfField.Numbers("[all activestatus resistance]"), PvfField.Numbers("[hit]"), PvfField.Numbers("[separate attack]"),
            StuckField,
        ];
    }

    private readonly Lock gate = new();
    private Dictionary<int, string>? equipmentPaths;
    private Dictionary<int, string>? stackablePaths;
    private Dictionary<int, string>? optionPaths;
    private readonly Dictionary<int, EquipmentMagicSealDefinition?> equipmentCache = [];
    private readonly Dictionary<int, MagicSealScrollDefinition?> scrollCache = [];

    public ValueTask<EquipmentMagicSealDefinition?> GetEquipmentAsync(
        int itemId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (!equipmentCache.TryGetValue(itemId, out var result))
                equipmentCache[itemId] = result = ReadEquipment(itemId);
            return ValueTask.FromResult(result);
        }
    }

    public ValueTask<MagicSealScrollDefinition?> GetScrollAsync(
        int itemId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (!scrollCache.TryGetValue(itemId, out var result))
                scrollCache[itemId] = result = ReadScroll(itemId);
            return ValueTask.FromResult(result);
        }
    }

    private EquipmentMagicSealDefinition? ReadEquipment(int itemId)
    {
        var archive = provider.Archive;
        equipmentPaths ??= Paths(archive, "equipment/equipment.lst");
        if (!equipmentPaths.TryGetValue(itemId, out var relative)) return null;
        var sections = RootSections(PvfNpcShopDefinitionSource.Sections(archive, "equipment/" + relative));
        if (OneInt(archive, sections, Fields.RandomOptionField, "equipment/" + relative) != 1) return null;
        var rarity = OneInt(archive, sections, Fields.RarityField, "equipment/" + relative);
        var level = OneInt(archive, sections, Fields.MinimumLevelField, "equipment/" + relative);
        var equipmentType = One(sections, Fields.EquipmentTypeField, "equipment/" + relative);
        if (rarity is null || level is null || equipmentType is null) return null;
        if (rarity < 0) throw Invalid("equipment/" + relative, Fields.RarityField, "negative rarity");
        if (level < 1) throw Invalid("equipment/" + relative, Fields.MinimumLevelField, "nonpositive equipment level");
        if (equipmentType.Values.Count is < 1 or > 2 || !equipmentType.Values[0].IsString
            || equipmentType.Values.Count == 2 && (equipmentType.Values[1].Kind != PvfTokenKind.Int32 || equipmentType.Values[1].Value < 0))
            throw Invalid("equipment/" + relative, Fields.EquipmentTypeField, "expected a type name and optional nonnegative Int32");
        var type = Name(archive, equipmentType.Values[0]);
        var keys = EquipmentKeys(relative, type);
        var initialIds = ResolveGroupIds(archive, Fields.ChooseOptionGroupField, keys, rarity.Value);
        if (initialIds.Count == 0) return null;
        var changeIds = ResolveGroupIds(archive, Fields.ModifiedOptionSelectionField, keys, rarity.Value);
        if (changeIds.Count == 0) changeIds = initialIds;
        var initial = BuildGroups(archive, initialIds, level.Value);
        var changes = BuildGroups(archive, changeIds, level.Value);
        var quantities = ReadQuantities(archive, rarity.Value);
        var costs = PvfNpcShopDefinitionSource.Sections(archive,
            "etc/randomoption/randomizedoptionoverall2.etc");
        var unsealCost = ReadUnsealCost(archive, costs, rarity.Value, level.Value);
        var changeCost = ReadChangeCost(archive, costs, rarity.Value, level.Value);
        if (initial.Count != initialIds.Count || changes.Count != changeIds.Count
            || quantities.Count == 0 || unsealCost is null || changeCost is null)
            return null;
        return new(itemId, rarity.Value, level.Value, unsealCost.Value, changeCost.Value,
            quantities, initial, changes);
    }

    private MagicSealScrollDefinition? ReadScroll(int itemId)
    {
        var archive = provider.Archive;
        stackablePaths ??= Paths(archive, "stackable/stackable.lst");
        if (!stackablePaths.TryGetValue(itemId, out var relative)) return null;
        var sections = RootSections(PvfNpcShopDefinitionSource.Sections(archive, "stackable/" + relative));
        // A21 script text explicitly limits the first alchemist scroll to Lv30. Other
        // level-limit codes need their own client/70 mapping before they can spend assets.
        return OneInt(archive, sections, Fields.UnsealScrollLevelLimitField, "stackable/" + relative) == 0
            ? new(itemId, 30) : null;
    }

    private List<IReadOnlyList<MagicSealCandidate>> BuildGroups(
        PvfArchive archive, IReadOnlyList<int> ids, int itemLevel)
    {
        optionPaths ??= Paths(archive, "etc/randomoption/randomoption.lst");
        const string groupingPath = "etc/randomoption/optiongrouping.etc";
        var grouping = PvfDocument.Open(archive, groupingPath).ReadAll(Fields.OptionGroupField);
        var byId = new Dictionary<int, IReadOnlyList<int>>();
        foreach (var values in grouping)
        {
            if (values.Count < 3 || values.Count % 2 != 1 || values[0] <= 0)
                throw Invalid(groupingPath, Fields.OptionGroupField, "expected group id and complete option/weight pairs");
            for (var i = 1; i < values.Count; i += 2)
                if (values[i] <= 0 || values[i + 1] < 0)
                    throw Invalid(groupingPath, Fields.OptionGroupField, "invalid option id or weight");
            if (!byId.TryAdd(values[0], values)) throw Invalid(groupingPath, Fields.OptionGroupField, "duplicate group id");
        }
        var result = new List<IReadOnlyList<MagicSealCandidate>>();
        foreach (var id in ids)
        {
            if (!byId.TryGetValue(id, out var values)) return [];
            var candidates = new List<MagicSealCandidate>();
            for (var i = 1; i < values.Count; i += 2)
            {
                var optionId = values[i];
                var weight = values[i + 1];
                if (optionId > byte.MaxValue || weight == 0 || !optionPaths.TryGetValue(optionId, out var path)) continue;
                var value = ReadOption(archive, path, itemLevel);
                if (value is { } option) candidates.Add(new(new((byte)optionId, option.Item1, option.Item2), weight));
            }
            if (candidates.Count == 0) return [];
            result.Add(candidates);
        }
        return result;
    }

    private static (byte, byte)? ReadOption(PvfArchive archive, string path, int itemLevel)
    {
        var source = "etc/randomoption/" + path;
        var sections = PvfDocument.Open(archive, source).Sections;
        var bestLevel = -1;
        (byte, byte)? best = null;
        var level = -1;
        var awaitingRange = false;
        var seenLevels = new HashSet<int>();
        var seenRanges = new HashSet<PvfField>();
        foreach (var section in sections)
        {
            if (Fields.DungeonScopeField.Matches(section.Name) || Fields.PvpScopeField.Matches(section.Name))
            {
                var scopeField = Fields.DungeonScopeField.Matches(section.Name) ? Fields.DungeonScopeField : Fields.PvpScopeField;
                scopeField.Decode(archive, section.Values, source);
                if (awaitingRange) throw Invalid(source, Fields.LevelField, "level has no option declaration");
                seenLevels.Clear();
                level = -1;
                continue;
            }
            if (Fields.OptionField.Matches(section.Name))
            {
                Fields.OptionField.Decode(archive, section.Values, source);
                if (awaitingRange) throw Invalid(source, Fields.LevelField, "level has no option declaration");
                seenLevels.Clear();
                continue;
            }
            if (Fields.LevelField.Matches(section.Name))
            {
                if (awaitingRange) throw Invalid(source, Fields.LevelField, "level has no option declaration");
                level = Fields.LevelField.Decode(archive, section.Values, source);
                if (level < 0) throw Invalid(source, Fields.LevelField, "negative level");
                if (!seenLevels.Add(level)) throw Invalid(source, Fields.LevelField, "duplicate level in the same option scope");
                seenRanges.Clear();
                awaitingRange = true;
                continue;
            }
            if (Fields.CloseLevelField.Matches(section.Name))
            {
                Fields.CloseLevelField.Decode(archive, section.Values, source);
                if (awaitingRange) throw Invalid(source, Fields.LevelField, "level has no option declaration");
                level = -1;
                continue;
            }
            var field = Fields.ValueRangeFields.FirstOrDefault(field => field.Matches(section.Name));
            if (field is not null)
            {
                if (level >= 0 && !seenRanges.Add(field)) throw Invalid(source, field, "duplicate range in the same level scope");
                var values = field.Decode(archive, section.Values, source);
                if (values.Count != 2) throw Invalid(source, field, "expected exactly two finite range values");
                // Finite values outside byte range remain valid PVF numbers. The existing
                // byte-state projection clamps them; parsing must never turn a bad token into
                // a plausible number. Stuck also legitimately declares negative rates.
                if (field != Fields.StuckField && values.Any(value => value < 0))
                    throw Invalid(source, field, "negative range for an unsigned stat");
                if (awaitingRange && level <= itemLevel && level >= bestLevel
                    && section.Values.All(token => token.Kind == PvfTokenKind.Int32))
                {
                    bestLevel = level;
                    best = ((byte)Math.Clamp(values[0], 0, byte.MaxValue), (byte)Math.Clamp(values[1], 0, byte.MaxValue));
                }
            }
            // Complex effect blocks and valid Single ranges are outside the old byte-state
            // projection, but recognised numeric fields above are validated in full.
            awaitingRange = false;
        }
        if (awaitingRange) throw Invalid(source, Fields.LevelField, "level has no option declaration");
        return best;
    }

    private static List<int> ResolveGroupIds(PvfArchive archive, PvfField<IReadOnlyList<PvfToken>> sectionName,
        IReadOnlyList<string> keys, int rarity)
    {
        const string source = "etc/randomoption/optiongroupselection.etc";
        var document = PvfDocument.Open(archive, source);
        if (!document.TryRead(sectionName, out var values)) return [];
        const int width = 11;
        if (values.Count == 0 || values.Count % width != 0) throw Invalid(source, sectionName, "truncated eleven-token selection row");
        for (var at = 0; at < values.Count; at += width)
            for (var column = 0; column < width; column++)
                if (column == 2 ? values[at + column].Kind != PvfTokenKind.QuotedString : values[at + column].Kind != PvfTokenKind.Int32)
                    throw Invalid(source, sectionName, "expected Int32 columns and one quoted equipment key");
        for (var at = 0; at < values.Count; at += width)
            if (string.IsNullOrWhiteSpace(archive.ResolveString(values[at + 2].Value))
                || values.Skip(at + 5).Take(3).Any(token => token.Value is < 1000 or > 1999))
                throw Invalid(source, sectionName, "empty equipment key or invalid option group id");
        foreach (var key in keys)
            for (var at = 0; at < values.Count; at += width)
                if (values[at].Value == rarity && Name(archive, values[at + 2]) == key)
                    return values.Skip(at + 5).Take(3).Select(token => token.Value).ToList();
        return [];
    }

    private static IReadOnlyList<string> EquipmentKeys(string path, string type)
    {
        var normalized = path.Replace('\\', '/').ToLowerInvariant();
        var keys = new List<string>();
        if (type == "weapon")
        {
            var parts = normalized.Split('/');
            var weapon = Array.IndexOf(parts, "weapon");
            if (weapon >= 0 && weapon + 1 < parts.Length)
                keys.Add(parts[weapon + 1] switch
                {
                    "boxglove" => "bglove", "hsword" => "lswd",
                    "beamsword" => "beamswd", "twinsword" => "twinswd",
                    "chakram" => "chakraweapon", var value => value,
                });
        }
        foreach (var (folder, prefix) in new[] { ("cloth", "cl"), ("leather", "lt"),
            ("larmor", "la"), ("harmor", "ha"), ("plate", "mt") })
            if (normalized.Contains('/' + folder + '/') && type is "coat" or "pants"
                or "shoulder" or "waist" or "shoes") keys.Add(prefix + " " + type);
        if (type.Length > 0) keys.Add(type);
        return keys;
    }

    private static List<MagicSealQuantity> ReadQuantities(PvfArchive archive, int rarity)
    {
        const string source = "etc/randomoption/optionquantity.etc";
        var field = rarity >= 3 ? Fields.UniqueQuantityField : Fields.CommonQuantityField;
        var document = PvfDocument.Open(archive, source);
        var result = new List<MagicSealQuantity>();
        if (!document.TryRead(field, out var values)) return result;
        if (values.Count == 0 || values.Count % 2 != 0) throw Invalid(source, field, "truncated quantity pair");
        var seenCounts = new HashSet<int>();
        for (var i = 0; i < values.Count; i += 2)
        {
            if (!seenCounts.Add(values[i])) throw Invalid(source, field, "duplicate quantity");
            if (values[i] is < 1 or > 3 || values[i + 1] < 0) throw Invalid(source, field, "invalid quantity or negative weight");
            if (values[i + 1] > 0) result.Add(new(values[i], values[i + 1]));
        }
        return result;
    }

    private static int? ReadUnsealCost(PvfArchive archive,
        IReadOnlyList<PvfNpcShopDefinitionSource.Section> sections, int rarity, int level)
    {
        const string source = "etc/randomoption/randomizedoptionoverall2.etc";
        var section = One(sections, Fields.BreakSealCostField, source);
        if (section is null) return null;
        var values = Fields.BreakSealCostField.Decode(archive, section.Values, source);
        if (values.Count == 0 || values.Count % 4 != 0) throw Invalid(source, Fields.BreakSealCostField, "truncated four-integer cost row");
        var bestLevel = -1;
        int? cost = null;
        var rows = new HashSet<(int Rarity, int Level, int Option)>();
        for (var i = 0; i < values.Count; i += 4)
        {
            if (!rows.Add((values[i], values[i + 1], values[i + 2]))) throw Invalid(source, Fields.BreakSealCostField, "duplicate cost row");
            if (values[i] < 0 || values[i + 1] < 0 || values[i + 2] < 1 || values[i + 3] < 0)
                throw Invalid(source, Fields.BreakSealCostField, "negative cost or invalid row operands");
            if (values[i] == rarity && values[i + 1] <= level && values[i + 1] > bestLevel && values[i + 2] == 1)
            { bestLevel = values[i + 1]; cost = values[i + 3]; }
        }
        return cost;
    }

    private static int? ReadChangeCost(PvfArchive archive,
        IReadOnlyList<PvfNpcShopDefinitionSource.Section> sections, int rarity, int level)
    {
        const string source = "etc/randomoption/randomizedoptionoverall2.etc";
        var section = One(sections, Fields.OptionModificationField, source);
        if (section is null) return null;
        var values = Fields.OptionModificationField.Decode(archive, section.Values, source);
        if (values.Count == 0 || values.Count % 3 != 0) throw Invalid(source, Fields.OptionModificationField, "truncated three-integer cost row");
        var bestLevel = -1;
        int? cost = null;
        var rows = new HashSet<int>();
        for (var i = 0; i < values.Count; i += 3)
        {
            if (!rows.Add(values[i])) throw Invalid(source, Fields.OptionModificationField, "duplicate level cost row");
            if (values[i] < 0 || values[i + 1] < 0 || values[i + 2] < 0) throw Invalid(source, Fields.OptionModificationField, "negative level or cost");
            if (values[i] <= level && values[i] > bestLevel)
            { bestLevel = values[i]; cost = values[i + (rarity >= 3 ? 2 : 1)]; }
        }
        return cost;
    }

    private static Dictionary<int, string> Paths(PvfArchive archive, string path)
    {
        if (archive.FindFile(path) < 0) return [];
        var result = new Dictionary<int, string>();
        var ambiguous = new HashSet<int>();
        foreach (var (id, relative) in PvfListFile.Read(archive, path))
            if (id > 0 && !ambiguous.Contains(id) && !string.IsNullOrWhiteSpace(relative)
                && !result.TryAdd(id, relative))
            { result.Remove(id); ambiguous.Add(id); }
        return result;
    }

    private static IReadOnlyList<PvfNpcShopDefinitionSource.Section> RootSections(
        IReadOnlyList<PvfNpcShopDefinitionSource.Section> sections)
    {
        var closing = sections.Where(section => section.Name.StartsWith("[/", StringComparison.Ordinal))
            .Select(section => section.Name).ToHashSet(StringComparer.Ordinal);
        var stack = new Stack<string>();
        var root = new List<PvfNpcShopDefinitionSource.Section>();
        foreach (var section in sections)
        {
            if (stack.TryPeek(out var expected) && section.Name == expected) { stack.Pop(); continue; }
            if (stack.Count == 0) root.Add(section);
            var close = "[/" + section.Name[1..];
            if (closing.Contains(close)) stack.Push(close);
        }
        return root;
    }

    private static PvfNpcShopDefinitionSource.Section? One(
        IReadOnlyList<PvfNpcShopDefinitionSource.Section> sections, PvfField name, string source)
    {
        var matches = sections.Where(section => name.Matches(section.Name)).ToArray();
        if (matches.Length > 1) throw Invalid(source, name, "duplicate field");
        return matches.FirstOrDefault();
    }

    private static int? OneInt(PvfArchive archive,
        IReadOnlyList<PvfNpcShopDefinitionSource.Section> sections, PvfField<int> name, string source)
    {
        var section = One(sections, name, source);
        return section is null ? null : name.Decode(archive, section.Values, source);
    }

    private static InvalidDataException Invalid(string path, PvfField field, string reason) =>
        new($"PVF '{path}', field '{field}': {reason}.");

    private static string Name(PvfArchive archive, PvfToken token) => token.IsString
        ? archive.ResolveString(token.Value).Trim().Trim('`', '[', ']').ToLowerInvariant()
        : string.Empty;
}
