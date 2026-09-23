using System.Collections.Frozen;
using Dfo.Pvf;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Infrastructure.Content;

/// <summary>One startup scan; masks are expanded into reusable grade/rarity buckets.</summary>
internal sealed class PvfExtraDropContentSource(PvfArchiveProvider provider) : IExtraDropContentSource
{
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
                    && archive.ResolveString(token.Value) == "[special monster drop]") { tagged = true; break; }
            if (!tagged) continue;
            var sections = PvfNpcShopDefinitionSource.Sections(archive, path);
            int Number(string name, int fallback)
            {
                var fields = sections.Where(s => s.Name == name).ToArray();
                if (fields.Length == 0) return fallback;
                if (fields.Length != 1 || fields[0].Values.Count != 1
                    || fields[0].Values[0].Kind != PvfTokenKind.Int32)
                    throw new InvalidDataException($"{path}: malformed {name}.");
                return fields[0].Values[0].Value;
            }
            if (Number("[special monster drop]", 0) != 1)
                throw new InvalidDataException($"{path}: [special monster drop] requires 1.");
            var weight = Number("[creation rate]", 0);
            var grade = Number("[grade]", -1);
            if (weight < 0) throw new InvalidDataException($"{path}: negative creation rate.");
            if (weight == 0) continue;
            if (grade < 0) throw new InvalidDataException($"{path}: missing item grade.");
            var difficulties = ReadMask(archive, sections, "[difficulty for extra drop]",
                ["[easy]", "[medium]", "[hard]", "[ultimate]", "[hero]"]);
            var types = ReadMask(archive, sections, "[dungeon type for extra drop]",
                ["[normal]", "[ancient]", "[dimension]"]);
            if (difficulties.Length == 0 || types.Length == 0) continue;
            var item = items.Get(row.Id)
                ?? throw new InvalidDataException($"{path}: unresolved item {row.Id}.");
            if (sections.Any(s => s.Name == "[equipment type]" && s.Values.Any(t =>
                t.IsString && archive.ResolveString(t.Value).EndsWith("avatar]", StringComparison.Ordinal))))
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
            var sections = PvfNpcShopDefinitionSource.Sections(archive, path);
            var ancient = sections.Any(s => s.Name == "[ancient dungeon]"
                && s.Values.Any(v => v.Kind == PvfTokenKind.Int32 && v.Value == 1));
            var dimension = sections.Any(s => s.Name == "[dimension dungeon]");
            dungeons.Add(row.Id, dimension ? ExtraDropDungeonType.Dimension
                : ancient ? ExtraDropDungeonType.Ancient : ExtraDropDungeonType.Normal);
        }
        return new(buckets.ToFrozenDictionary(p => p.Key, p => new DropItemPool(
            p.Key.Kind is DropItemKind.Equipment or DropItemKind.Avatar ? DropItemFamily.Equipment : DropItemFamily.Stackable,
            p.Value.ToFrozenDictionary(b => b.Key, b => (IReadOnlyList<DropPoolEntry>)b.Value.ToArray()))),
            dungeons.ToFrozenDictionary(), count);
    }

    private static int[] ReadMask(PvfArchive archive,
        IReadOnlyList<PvfNpcShopDefinitionSource.Section> sections, string name, string[] allowed)
    {
        var fields = sections.Where(s => s.Name == name).ToArray();
        if (fields.Length == 0) return [];
        if (fields.Length != 1) throw new InvalidDataException($"Duplicate {name}.");
        return ParseMask(fields[0].Values.Select(t => t.IsString ? archive.ResolveString(t.Value)
            : throw new InvalidDataException($"Non-string {name} mask.")), allowed);
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
