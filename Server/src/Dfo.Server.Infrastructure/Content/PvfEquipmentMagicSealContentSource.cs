using Dfo.Pvf;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfEquipmentMagicSealContentSource(PvfArchiveProvider provider)
    : IEquipmentMagicSealContentSource
{
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
        var sections = PvfNpcShopDefinitionSource.Sections(archive, "equipment/" + relative);
        if (OneInt(sections, "[random option]") != 1) return null;
        var rarity = OneInt(sections, "[rarity]");
        var level = OneInt(sections, "[minimum level]");
        var equipmentType = One(sections, "[equipment type]");
        if (rarity is null or < 0 || level is null or < 1
            || equipmentType is not { Values.Count: > 0 }) return null;
        var type = Name(archive, equipmentType.Values[0]);
        var keys = EquipmentKeys(relative, type);
        var initialIds = ResolveGroupIds(archive, "[choose option group]", keys, rarity.Value);
        if (initialIds.Count == 0) return null;
        var changeIds = ResolveGroupIds(archive, "[modified option selection]", keys, rarity.Value);
        if (changeIds.Count == 0) changeIds = initialIds;
        var initial = BuildGroups(archive, initialIds, level.Value);
        var changes = BuildGroups(archive, changeIds, level.Value);
        var quantities = ReadQuantities(archive, rarity.Value);
        var costs = PvfNpcShopDefinitionSource.Sections(archive,
            "etc/randomoption/randomizedoptionoverall2.etc");
        var unsealCost = ReadUnsealCost(costs, rarity.Value, level.Value);
        var changeCost = ReadChangeCost(costs, rarity.Value, level.Value);
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
        var sections = PvfNpcShopDefinitionSource.Sections(archive, "stackable/" + relative);
        // A21 script text explicitly limits the first alchemist scroll to Lv30. Other
        // level-limit codes need their own client/70 mapping before they can spend assets.
        return OneInt(sections, "[unseal scroll level limit]") == 0
            ? new(itemId, 30) : null;
    }

    private List<IReadOnlyList<MagicSealCandidate>> BuildGroups(
        PvfArchive archive, IReadOnlyList<int> ids, int itemLevel)
    {
        optionPaths ??= Paths(archive, "etc/randomoption/randomoption.lst");
        var grouping = PvfNpcShopDefinitionSource.Sections(archive,
            "etc/randomoption/optiongrouping.etc");
        var result = new List<IReadOnlyList<MagicSealCandidate>>();
        foreach (var id in ids)
        {
            var group = grouping.SingleOrDefault(section => section.Name == "[option group]"
                && section.Values.Count > 0 && Int(section.Values[0]) == id);
            if (group is null) return [];
            var candidates = new List<MagicSealCandidate>();
            for (var i = 1; i + 1 < group.Values.Count; i += 2)
            {
                var optionId = Int(group.Values[i]);
                var weight = Int(group.Values[i + 1]);
                if (optionId is not > 0 or > byte.MaxValue || weight is not > 0
                    || !optionPaths.TryGetValue(optionId.Value, out var path)) continue;
                var value = ReadOption(archive, path, itemLevel);
                if (value is { } option)
                    candidates.Add(new(new((byte)optionId.Value, option.Item1, option.Item2),
                        weight.Value));
            }
            if (candidates.Count == 0) return [];
            result.Add(candidates);
        }
        return result;
    }

    private static (byte, byte)? ReadOption(PvfArchive archive, string path, int itemLevel)
    {
        var sections = PvfNpcShopDefinitionSource.Sections(archive, "etc/randomoption/" + path);
        var bestLevel = -1;
        (byte, byte)? best = null;
        for (var i = 0; i + 1 < sections.Count; i++)
        {
            var level = sections[i].Name == "[level]" ? Int(sections[i].Values.FirstOrDefault()) : null;
            if (level is null || level > itemLevel || level < bestLevel) continue;
            var values = sections[i + 1].Values.Where(token => token.Kind == PvfTokenKind.Int32)
                .Select(token => token.Value).ToArray();
            if (values.Length < 2) continue;
            bestLevel = level.Value;
            best = ((byte)Math.Clamp(values[^2], 0, 255),
                (byte)Math.Clamp(values[^1], 0, 255));
        }
        return best;
    }

    private static List<int> ResolveGroupIds(PvfArchive archive, string sectionName,
        IReadOnlyList<string> keys, int rarity)
    {
        var section = One(PvfNpcShopDefinitionSource.Sections(archive,
            "etc/randomoption/optiongroupselection.etc"), sectionName);
        if (section is null) return [];
        foreach (var key in keys)
        {
            for (var i = 2; i < section.Values.Count; i++)
            {
                if (Name(archive, section.Values[i]) != key
                    || Int(section.Values[i - 2]) != rarity) continue;
                var ids = new List<int>();
                for (var j = i + 1; j < section.Values.Count && ids.Count < 3; j++)
                {
                    var value = Int(section.Values[j]);
                    if (value is null) break;
                    if (value is >= 1000 and <= 1999) ids.Add(value.Value);
                }
                if (ids.Count > 0) return ids;
            }
        }
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
        var section = One(PvfNpcShopDefinitionSource.Sections(archive,
            "etc/randomoption/optionquantity.etc"), rarity >= 3 ? "[unique]" : "[common]");
        var result = new List<MagicSealQuantity>();
        if (section is null) return result;
        for (var i = 0; i + 1 < section.Values.Count; i += 2)
        {
            var count = Int(section.Values[i]);
            var weight = Int(section.Values[i + 1]);
            if (count is >= 1 and <= 3 && weight is > 0)
                result.Add(new(count.Value, weight.Value));
        }
        return result;
    }

    private static int? ReadUnsealCost(
        IReadOnlyList<PvfNpcShopDefinitionSource.Section> sections, int rarity, int level)
    {
        var values = One(sections, "[break seal cost]")?.Values;
        if (values is null) return null;
        var bestLevel = -1;
        int? cost = null;
        for (var i = 0; i + 3 < values.Count; i += 4)
        {
            var rowRarity = Int(values[i]); var rowLevel = Int(values[i + 1]);
            var optionIndex = Int(values[i + 2]); var rowCost = Int(values[i + 3]);
            if (rowRarity == rarity && rowLevel <= level && rowLevel > bestLevel
                && optionIndex == 1 && rowCost is >= 0)
            { bestLevel = rowLevel.Value; cost = rowCost; }
        }
        return cost;
    }

    private static int? ReadChangeCost(
        IReadOnlyList<PvfNpcShopDefinitionSource.Section> sections, int rarity, int level)
    {
        var values = One(sections, "[option modification]")?.Values;
        if (values is null) return null;
        var bestLevel = -1;
        int? cost = null;
        for (var i = 0; i + 2 < values.Count; i += 3)
        {
            var rowLevel = Int(values[i]);
            var common = Int(values[i + 1]); var unique = Int(values[i + 2]);
            var rowCost = rarity >= 3 ? unique : common;
            if (rowLevel <= level && rowLevel > bestLevel && rowCost is >= 0)
            { bestLevel = rowLevel.Value; cost = rowCost; }
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

    private static PvfNpcShopDefinitionSource.Section? One(
        IReadOnlyList<PvfNpcShopDefinitionSource.Section> sections, string name)
    {
        var matches = sections.Where(section => section.Name == name).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private static int? OneInt(
        IReadOnlyList<PvfNpcShopDefinitionSource.Section> sections, string name)
    {
        var values = One(sections, name)?.Values;
        return values is { Count: 1 } ? Int(values[0]) : null;
    }

    private static int? Int(PvfToken token) =>
        token.Kind == PvfTokenKind.Int32 ? token.Value : null;

    private static string Name(PvfArchive archive, PvfToken token) => token.IsString
        ? archive.ResolveString(token.Value).Trim().Trim('`', '[', ']').ToLowerInvariant()
        : string.Empty;
}
