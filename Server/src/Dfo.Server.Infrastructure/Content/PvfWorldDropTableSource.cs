using System.Collections.Immutable;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfWorldDropTableSource(PvfArchiveProvider provider) : IWorldDropTableSource
{
    internal const string Path = "etc/worlddrop.etc";

    public IReadOnlyList<WorldDropLevel> Load()
    {
        var archive = provider.Archive;
        var sections = PvfNpcShopDefinitionSource.Sections(archive, Path);
        if (sections.Count != 2 || sections[0].Name != "[world drop]"
            || sections[1].Name != "[/world drop]" || sections[1].Values.Count != 0
            || sections[0].Values.Any(token => token.Kind != PvfTokenKind.Int32))
            throw new InvalidDataException($"{Path}: missing or malformed world drop section.");
        var paths = new Dictionary<int, (string Path, DropItemKind Kind)>();
        foreach (var (list, folder, kind) in new[] {
            ("stackable/stackable.lst", "stackable/", DropItemKind.Stackable),
            ("equipment/equipment.lst", "equipment/", DropItemKind.Equipment) })
            foreach (var row in PvfListFile.Read(archive, list))
                paths.TryAdd(row.Id, (folder + row.RelativePath, kind));
        var items = new Dictionary<int, WorldDropEntry>();
        WorldDropEntry Resolve(int id)
        {
            if (items.TryGetValue(id, out var cached)) return cached;
            if (!paths.TryGetValue(id, out var path) || archive.FindFile(path.Path) < 0)
                throw new InvalidDataException($"{Path}: missing item {id}.");
            var item = PvfNpcShopDefinitionSource.Sections(archive, path.Path);
            int Field(string name, int fallback)
            {
                var values = item.Where(section => section.Name == name).ToArray();
                if (values.Length == 0) return fallback;
                if (values.Length != 1 || values[0].Values.Count != 1 || values[0].Values[0].Kind != PvfTokenKind.Int32)
                    throw new InvalidDataException($"{Path}: item {id}, malformed {name} in {path.Path}.");
                return values[0].Values[0].Value;
            }
            var rarity = Field("[rarity]", 0);
            var durability = Field("[durability]", 0);
            if (!Enum.IsDefined((DropRarity)rarity) || durability < 0 || durability > ushort.MaxValue)
                throw new InvalidDataException($"{Path}: invalid metadata for item {id}.");
            return items[id] = new(id, 0, (DropRarity)rarity, path.Kind, (ushort)durability);
        }
        return Parse(sections[0].Values.Select(token => token.Value).ToArray(), Resolve);
    }

    internal static IReadOnlyList<WorldDropLevel> Parse(IReadOnlyList<int> values, Func<int, WorldDropEntry> resolve)
    {
        var result = new List<WorldDropLevel>();
        var levels = new HashSet<int>();
        var index = 0;
        int Read()
        {
            if (index >= values.Count) throw new InvalidDataException($"{Path}: truncated row at integer {index}.");
            return values[index++];
        }
        while (index < values.Count)
        {
            var level = Read();
            var reserved = Read();
            if (level <= 0 || !levels.Add(level) || reserved != 0)
                throw new InvalidDataException($"{Path}: unsupported/duplicate level header {level}, {reserved}.");
            var entries = ImmutableArray.CreateBuilder<WorldDropEntry>();
            while (true)
            {
                var item = Read();
                if (item == -1) break;
                var weight = Read();
                if (item < 0 || weight < 0 || (item == 0 && weight != 0))
                    throw new InvalidDataException($"{Path}: invalid item/weight at level {level}, integer {index - 2}.");
                if (weight > 0) entries.Add(resolve(item) with { Weight = weight });
            }
            result.Add(new(level, entries.ToImmutable()));
        }
        if (result.Count == 0) throw new InvalidDataException($"{Path}: no levels.");
        return result;
    }
}
