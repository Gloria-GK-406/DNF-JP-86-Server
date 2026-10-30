using Dfo.Pvf.Schema;
using System.Collections.Frozen;
using Dfo.Pvf;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Infrastructure.Content;

/// <summary>One startup scan; masks are expanded into reusable grade/rarity buckets.</summary>
internal sealed class PvfExtraDropContentSource(PvfArchiveProvider provider) : IExtraDropContentSource
{
    private static class Fields
    {
        internal static readonly PvfField<int> SpecialMonsterDropField = PvfField.Int32("[special monster drop]", optional: true);
        internal static readonly PvfField<int> CreationRateField = PvfField.Int32("[creation rate]", optional: true);
        internal static readonly PvfField<int> GradeField = PvfField.Int32("[grade]", optional: true);
        internal static readonly PvfField<IReadOnlyList<string>> DifficultyForExtraDropField = PvfField.Strings("[difficulty for extra drop]", optional: true);
        internal static readonly PvfField<IReadOnlyList<string>> DungeonTypeForExtraDropField = PvfField.Strings("[dungeon type for extra drop]", optional: true);
        internal static readonly PvfField<string> EquipmentTypeField = PvfField.Custom<string>("[equipment type]", (archive, values, source) =>
        {
            if (values.Count is < 1 or > 2 || !values[0].IsString ||
                values.Count == 2 && (values[1].Kind != PvfTokenKind.Int32 || values[1].Value < 0))
                throw new InvalidDataException($"PVF '{source}', field '[equipment type]': expected type name and optional nonnegative Int32 subtype.");
            return archive.ResolveString(values[0].Value);
        }, optional: true);
        internal static readonly PvfField<int> AncientDungeonField = PvfField.Int32("[ancient dungeon]", optional: true);
        internal static readonly PvfField<IReadOnlyList<PvfToken>> DimensionDungeonField = PvfField.Tokens("[dimension dungeon]", optional: true);
    }

    private readonly Lazy<ExtraDropContent> cache = new(() => Read(provider.Archive));
    public ExtraDropContent Load() => cache.Value;

    internal static ExtraDropContent Read(PvfArchive archive)
    {
        var buckets = new Dictionary<(DropItemKind Kind, int Difficulty, ExtraDropDungeonType DungeonType),
            Dictionary<(int Grade, int Rarity), List<DropPoolEntry>>>();
        var items = new PvfDropItemReader(archive);
        var count = 0;
        foreach (var (list, prefix) in new[] {
            ("equipment/equipment.lst", "equipment/"), ("stackable/stackable.lst", "stackable/") })
        foreach (var row in PvfListFile.Read(archive, list))
        {
            var path = prefix + row.RelativePath;
            if (archive.FindFile(path) < 0)
                throw new InvalidDataException($"Extra drop scan: missing item {row.Id}: {path}.");
            // Avoid allocating all sections for the overwhelmingly common untagged case.
            var reader = archive.OpenScript(path);
            var tagged = false;
            while (reader.Read(out var token))
                if (token.Kind == PvfTokenKind.Identifier
                    && archive.ResolveString(token.Value) == Fields.SpecialMonsterDropField.Name) { tagged = true; break; }
            if (!tagged) continue;
            var document = PvfDocument.Open(archive, path).AtRoot();
            if (!document.TryRead(Fields.SpecialMonsterDropField, out var specialDrop)) continue;
            var typeName = document.TryRead(Fields.EquipmentTypeField, out var declaredType) ? declaredType : null;
            var sections = document.Sections.Select(section =>
                new PvfSection(section.Name, section.Values.ToList())).ToArray();
            int Number(PvfField<int> name, int fallback)
            {
                var fields = sections.Where(s => s.Name == name.Name).ToArray();
                if (fields.Length == 0) return fallback;
                if (fields.Length != 1 || fields[0].Values.Count != 1
                    || fields[0].Values[0].Kind != PvfTokenKind.Int32)
                    throw new InvalidDataException($"{path}: malformed {name}.");
                return name.Decode(archive, fields[0].Values, path);
            }
            if (specialDrop != 1)
                throw new InvalidDataException($"{path}: [special monster drop] requires 1.");
            var weight = Number(Fields.CreationRateField, 0);
            var grade = Number(Fields.GradeField, -1);
            if (weight < 0) throw new InvalidDataException($"{path}: negative creation rate.");
            if (weight == 0) continue;
            if (grade < 0) throw new InvalidDataException($"{path}: missing item grade.");
            var difficulties = ReadMask(archive, sections, Fields.DifficultyForExtraDropField,
                ["[easy]", "[medium]", "[hard]", "[ultimate]", "[hero]"]);
            var types = ReadMask(archive, sections, Fields.DungeonTypeForExtraDropField,
                ["[normal]", "[ancient]", "[dimension]"]);
            if (difficulties.Length == 0 || types.Length == 0) continue;
            var item = items.Get(row.Id)
                ?? throw new InvalidDataException($"{path}: unresolved item {row.Id}.");
            if (typeName?.EndsWith("avatar]", StringComparison.Ordinal) == true)
                item = item with { Kind = DropItemKind.Avatar };
            foreach (var difficulty in difficulties)
            foreach (var type in types)
            {
                var key = (item.Kind, difficulty, (ExtraDropDungeonType)type);
                if (!buckets.TryGetValue(key, out var pool)) buckets[key] = pool = [];
                var gradeKey = (grade, (int)item.Rarity);
                if (!pool.TryGetValue(gradeKey, out var bucket)) pool[gradeKey] = bucket = [];
                bucket.Add(new(row.Id, weight, item.Durability));
                if (pool.Values.SelectMany(b => b).Sum(e => (long)e.Weight) > int.MaxValue)
                    throw new InvalidDataException($"{path}: Extra candidate weights overflow.");
            }
            count++;
        }
        var dungeons = new Dictionary<int, ExtraDropDungeonType>();
        foreach (var row in PvfListFile.Read(archive, "dungeon/dungeon.lst"))
        {
            var path = "dungeon/" + row.RelativePath;
            if (archive.FindFile(path) < 0)
                throw new InvalidDataException($"Extra drops: missing dungeon {row.Id}: {path}.");
            var sections = (PvfDocument.TryOpen(archive, path, PvfPreamble.Ignore)?.Sections ?? []);
            var ancientSections = sections.Where(section => section.Name == Fields.AncientDungeonField.Name).ToArray();
            if (ancientSections.Length > 1) throw new InvalidDataException($"PVF '{path}', field '{Fields.AncientDungeonField.Name}': duplicate field.");
            var ancient = ancientSections.Length == 1 && Fields.AncientDungeonField.Decode(archive, ancientSections[0].Values, path) == 1;
            var dimension = sections.Any(s => s.Name == Fields.DimensionDungeonField.Name);
            dungeons.Add(row.Id, dimension ? ExtraDropDungeonType.Dimension
                : ancient ? ExtraDropDungeonType.Ancient : ExtraDropDungeonType.Normal);
        }
        return new(buckets.ToFrozenDictionary(p => p.Key, p => new DropItemPool(
            p.Key.Kind is DropItemKind.Equipment or DropItemKind.Avatar ? DropItemFamily.Equipment : DropItemFamily.Stackable,
            p.Value.ToFrozenDictionary(b => b.Key, b => (IReadOnlyList<DropPoolEntry>)b.Value.ToArray()))),
            dungeons.ToFrozenDictionary(), count);
    }

    private static int[] ReadMask(PvfArchive archive,
        IReadOnlyList<PvfSection> sections, PvfField<IReadOnlyList<string>> name, string[] allowed)
    {
        var fields = sections.Where(s => s.Name == name.Name).ToArray();
        if (fields.Length == 0) return [];
        if (fields.Length != 1) throw new InvalidDataException($"Duplicate {name}.");
        return ParseMask(name.Decode(archive, fields[0].Values, "extra drop mask"), allowed);
    }

    internal static int[] ParseMask(IEnumerable<string> values, string[] allowed)
    {
        var result = new HashSet<int>();
        foreach (var raw in values)
        {
            var value = raw.Trim().ToLowerInvariant();
            if (value == "[all]") { result.UnionWith(Enumerable.Range(0, allowed.Length)); continue; }
            var index = Array.IndexOf(allowed, value);
            if (index < 0) throw new InvalidDataException($"Unknown Extra drop mask {raw}.");
            result.Add(index);
        }
        return result.Order().ToArray();
    }
}
