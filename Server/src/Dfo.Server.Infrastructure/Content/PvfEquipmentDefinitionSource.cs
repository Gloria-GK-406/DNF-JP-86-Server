using System.Collections.Frozen;
using Dfo.Pvf;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Infrastructure.Content;

/// <summary>Strictly reads equipment facts; operation rules interpret types and restrictions.</summary>
internal sealed class PvfEquipmentDefinitionSource(PvfArchiveProvider provider) : IEquipmentDefinitionSource
{
    private const string IndexPath = "equipment/equipment.lst";
    private readonly Lock gate = new();
    private Dictionary<int, string>? equipmentPaths;
    private readonly Dictionary<int, EquipmentDefinition> equipmentCache = [];

    public ValueTask<int> InitializeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            equipmentPaths ??= ReadPaths(provider.Archive);
            return ValueTask.FromResult(equipmentPaths.Count);
        }
    }

    public ValueTask<EquipmentDefinition?> GetEquipmentAsync(int itemId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            equipmentPaths ??= ReadPaths(provider.Archive);
            if (!equipmentPaths.TryGetValue(itemId, out var path))
                return ValueTask.FromResult<EquipmentDefinition?>(null);
            if (!equipmentCache.TryGetValue(itemId, out var definition))
            {
                // Failed reads throw before assignment, so they cannot become negative cache entries.
                definition = ReadEquipment(provider.Archive, itemId, path);
                equipmentCache.Add(itemId, definition);
            }
            return ValueTask.FromResult<EquipmentDefinition?>(definition);
        }
    }

    private static Dictionary<int, string> ReadPaths(PvfArchive archive)
    {
        var file = RequireScript(archive, null, IndexPath, "index");
        var reader = archive.OpenScript(file);
        var paths = new Dictionary<int, string>();
        while (reader.Read(out var id))
        {
            if (id.Kind != PvfTokenKind.Int32 || id.Value <= 0)
                throw Invalid(null, IndexPath, "index item id", "expected a positive integer");
            if (!reader.Read(out var token) || token.Kind != PvfTokenKind.QuotedString)
                throw Invalid(id.Value, IndexPath, "index path", "expected one quoted relative path");
            var relative = archive.ResolveString(token.Value).Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(relative) || relative.StartsWith('/')
                || relative.Contains(':') || relative.Split('/').Any(part => part is ".." or "." or ""))
                throw Invalid(id.Value, IndexPath, "index path", "invalid relative path");
            if (!paths.TryAdd(id.Value, "equipment/" + relative))
                throw Invalid(id.Value, IndexPath, "index item id", "duplicate equipment id");
        }
        if (paths.Count == 0)
            throw Invalid(null, IndexPath, "index", "equipment index is empty");
        return paths;
    }

    private static EquipmentDefinition ReadEquipment(PvfArchive archive, int itemId, string path)
    {
        var file = RequireScript(archive, itemId, path, "template");
        var sections = ReadSections(archive, itemId, path, file);
        var minimumLevel = RequiredInt(sections, itemId, path, "[minimum level]", 0, int.MaxValue);
        var grade = RequiredInt(sections, itemId, path, "[grade]", 0, int.MaxValue);
        var rarity = RequiredInt(sections, itemId, path, "[rarity]", 0, 6);
        var durability = OneSection(sections, itemId, path, "[durability]", required: false);
        ushort? maximum = durability is null ? null
            : (ushort)ReadInt(durability, itemId, path, "[durability]", 0, ushort.MaxValue);
        var type = OneSection(sections, itemId, path, "[equipment type]", required: true)!;
        if (type.Count is < 1 or > 2 || !type[0].IsString
            || type.Count == 2 && (type[1].Kind != PvfTokenKind.Int32 || type[1].Value < 0))
            throw Invalid(itemId, path, "[equipment type]", "expected a type name and optional nonnegative integer");
        var typeName = Normalize(archive.ResolveString(type[0].Value));
        if (typeName.Length == 0)
            throw Invalid(itemId, path, "[equipment type]", "type name is empty");

        var impossible = OneSection(sections, itemId, path, "[impossible contents]", required: false);
        var declarations = new HashSet<string>(StringComparer.Ordinal);
        if (impossible is not null)
            foreach (var token in impossible)
            {
                if (!token.IsString)
                    throw Invalid(itemId, path, "[impossible contents]", "expected string declarations");
                var name = Normalize(archive.ResolveString(token.Value));
                if (name.Length == 0 || !declarations.Add(name))
                    throw Invalid(itemId, path, "[impossible contents]", "empty or duplicate declaration");
            }
        return new(minimumLevel, grade, rarity, typeName, maximum)
        {
            ImpossibleContents = declarations.ToFrozenSet(StringComparer.Ordinal),
        };
    }

    private static int RequireScript(PvfArchive archive, int? itemId, string path, string field)
    {
        var file = archive.FindFile(path);
        if (file < 0)
            throw Invalid(itemId, path, field, "required script is missing");
        if (!archive.GetEntry(file).IsScript)
            throw Invalid(itemId, path, field, "expected a script file");
        if (archive.GetFileBytes(file).Length % PvfToken.Size != 0)
            throw Invalid(itemId, path, field, "script has a truncated token record");
        return file;
    }

    private static Dictionary<string, List<List<PvfToken>>> ReadSections(
        PvfArchive archive, int itemId, string path, int file)
    {
        var sections = new Dictionary<string, List<List<PvfToken>>>(StringComparer.Ordinal);
        var reader = archive.OpenScript(file);
        List<PvfToken>? values = null;
        while (reader.Read(out var token))
        {
            if (token.Kind == PvfTokenKind.Identifier)
            {
                var name = archive.ResolveString(token.Value);
                if (!sections.TryGetValue(name, out var occurrences))
                    sections.Add(name, occurrences = []);
                values = [];
                occurrences.Add(values);
            }
            else if (values is null)
                throw Invalid(itemId, path, "template", "value appears before a section tag");
            else
                values.Add(token);
        }
        return sections;
    }

    private static List<PvfToken>? OneSection(Dictionary<string, List<List<PvfToken>>> sections,
        int itemId, string path, string field, bool required)
    {
        if (!sections.TryGetValue(field, out var matches))
        {
            if (required)
                throw Invalid(itemId, path, field, "required field is missing");
            return null;
        }
        if (matches.Count != 1)
            throw Invalid(itemId, path, field, "duplicate field");
        return matches[0];
    }

    private static int RequiredInt(Dictionary<string, List<List<PvfToken>>> sections,
        int itemId, string path, string field, int minimum, int maximum) =>
        ReadInt(OneSection(sections, itemId, path, field, required: true)!, itemId, path,
            field, minimum, maximum);

    private static int ReadInt(List<PvfToken> values, int itemId, string path, string field,
        int minimum, int maximum)
    {
        if (values.Count != 1 || values[0].Kind != PvfTokenKind.Int32)
            throw Invalid(itemId, path, field, "expected exactly one integer");
        var value = values[0].Value;
        if (value < minimum || value > maximum)
            throw Invalid(itemId, path, field, $"value {value} is outside [{minimum}, {maximum}]");
        return value;
    }

    private static string Normalize(string value) =>
        value.Trim().Trim('`').Trim().Trim('[', ']').ToLowerInvariant();

    private static InvalidDataException Invalid(int? itemId, string path, string field, string reason) =>
        new($"Equipment {itemId?.ToString() ?? "index"}, PVF '{path}', field '{field}': {reason}.");
}
