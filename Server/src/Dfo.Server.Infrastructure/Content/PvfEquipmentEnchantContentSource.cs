using Dfo.Pvf;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfEquipmentEnchantContentSource(PvfArchiveProvider provider)
    : IEquipmentEnchantContentSource
{
    private readonly Lock gate = new();
    private Dictionary<int, string>? stackables;
    private Dictionary<int, string>? equipment;
    private readonly Dictionary<(int Bead, int Target), EquipmentEnchantDefinition?> cache = [];

    public ValueTask<EquipmentEnchantDefinition?> ResolveAsync(
        int beadItemId, int equipmentItemId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            var key = (beadItemId, equipmentItemId);
            if (!cache.TryGetValue(key, out var result))
                cache[key] = result = Read(key);
            return ValueTask.FromResult(result);
        }
    }

    private EquipmentEnchantDefinition? Read((int Bead, int Target) key)
    {
        var archive = provider.Archive;
        stackables ??= Paths(archive, "stackable/stackable.lst");
        equipment ??= Paths(archive, "equipment/equipment.lst");
        if (!stackables.TryGetValue(key.Bead, out var beadPath)
            || !equipment.TryGetValue(key.Target, out var targetPath)) return null;

        var bead = PvfNpcShopDefinitionSource.Sections(archive, "stackable/" + beadPath);
        var type = One(bead, "[stackable type]");
        var cardId = OneInt(bead, "[monster card id]");
        if (type is null || type.Values.Count != 2
            || Name(archive, type.Values[0]) != "enchant waste"
            || type.Values[1].Kind != PvfTokenKind.Int32 || type.Values[1].Value != 0
            || cardId is not > 0 || !stackables.TryGetValue(cardId.Value, out var cardPath)) return null;

        foreach (var whitelist in new[] { "[target item id]", "[bead limited usable item]" })
        {
            var declarations = bead.Where(s => s.Name == whitelist).ToArray();
            if (declarations.Length > 1 || declarations.Length == 1
                && (declarations[0].Values.Count == 0
                    || declarations[0].Values.Any(v => v.Kind != PvfTokenKind.Int32)
                    || !declarations[0].Values.Any(v => v.Value == key.Target))) return null;
        }

        var card = PvfNpcShopDefinitionSource.Sections(archive, "stackable/" + cardPath);
        var category = One(card, "[item category]");
        var target = PvfNpcShopDefinitionSource.Sections(archive, "equipment/" + targetPath);
        var equipmentType = One(target, "[equipment type]");
        var stringData = One(card, "[string data]");
        if (category is not { Values.Count: 1 }
            || Name(archive, category.Values[0]) != "monster card"
            || equipmentType is null || equipmentType.Values.Count == 0
            || stringData is null || stringData.Values.Count < 2
            || !HasSupportedEnchantIndex(card)) return null;
        var targetType = Name(archive, equipmentType.Values[0]);
        return targetType.Length > 0 && stringData.Values.Skip(1)
            .Any(token => Name(archive, token) == targetType)
            ? new(key.Bead, cardId.Value) : null;
    }

    private static Dictionary<int, string> Paths(PvfArchive archive, string path)
    {
        if (archive.FindFile(path) < 0) return [];
        List<(int Id, string RelativePath)> entries;
        try { entries = PvfListFile.Read(archive, path); }
        catch (InvalidOperationException) { return []; }
        var result = new Dictionary<int, string>();
        var ambiguous = new HashSet<int>();
        foreach (var (id, relative) in entries)
            if (id > 0 && !ambiguous.Contains(id) && !string.IsNullOrWhiteSpace(relative)
                && !result.TryAdd(id, relative))
            {
                result.Remove(id);
                ambiguous.Add(id);
            }
        return result;
    }

    private static PvfNpcShopDefinitionSource.Section? One(
        IReadOnlyList<PvfNpcShopDefinitionSource.Section> sections, string name)
    {
        var matches = sections.Where(s => s.Name == name).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private static int? OneInt(
        IReadOnlyList<PvfNpcShopDefinitionSource.Section> sections, string name)
    {
        var section = One(sections, name);
        return section is { Values.Count: 1 } && section.Values[0].Kind == PvfTokenKind.Int32
            ? section.Values[0].Value : null;
    }

    internal static bool HasSupportedEnchantIndex(
        IReadOnlyList<PvfNpcShopDefinitionSource.Section> sections)
    {
        var matches = sections.Where(section => section.Name == "[enchant index]").ToArray();
        return matches.Length == 1 && matches[0].Values.Count == 1
            && matches[0].Values[0].Kind == PvfTokenKind.Int32
            && matches[0].Values[0].Value == 0;
    }

    private static string Name(PvfArchive archive, PvfToken token) => token.IsString
        ? archive.ResolveString(token.Value).Trim().Trim('`').Trim().Trim('[', ']').ToLowerInvariant()
        : string.Empty;
}
