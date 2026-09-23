using Dfo.Pvf;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Infrastructure.Content;

/// <summary>Item metadata for explicit drop tables, independent of creation-rate eligibility.</summary>
internal sealed class PvfDropItemReader
{
    private readonly PvfArchive archive;
    private readonly Dictionary<int, (string Path, DropItemKind Kind)> paths = [];
    private readonly Dictionary<int, RolledDrop?> items = [];

    public PvfDropItemReader(PvfArchive archive)
    {
        this.archive = archive;
        foreach (var (list, folder, kind) in new[] {
            ("stackable/stackable.lst", "stackable/", DropItemKind.Stackable),
            ("equipment/equipment.lst", "equipment/", DropItemKind.Equipment) })
            foreach (var row in PvfListFile.Read(archive, list))
                paths.TryAdd(row.Id, (folder + row.RelativePath, kind));
    }

    public RolledDrop? Get(int id)
    {
        if (items.TryGetValue(id, out var cached)) return cached;
        if (!paths.TryGetValue(id, out var path) || archive.FindFile(path.Path) < 0)
            return items[id] = null;
        var allSections = PvfNpcShopDefinitionSource.Sections(archive, path.Path);
        var closingTags = allSections.Where(section => section.Name.StartsWith("[/", StringComparison.Ordinal))
            .Select(section => section.Name).ToHashSet(StringComparer.Ordinal);
        var sections = new List<PvfNpcShopDefinitionSource.Section>();
        var blocks = new Stack<string>();
        foreach (var section in allSections)
        {
            if (blocks.TryPeek(out var expected) && section.Name == expected)
            {
                blocks.Pop();
                continue;
            }
            if (blocks.Count == 0) sections.Add(section);
            var closing = "[/" + section.Name[1..];
            if (closingTags.Contains(closing)) blocks.Push(closing);
        }
        // Nested equipment/reward conditions can have their own [rarity] and [durability].
        int Field(string name, int fallback)
        {
            var values = sections.Where(section => section.Name == name).ToArray();
            if (values.Length == 0) return fallback;
            if (values.Length != 1 || values[0].Values.Count != 1 || values[0].Values[0].Kind != PvfTokenKind.Int32)
                throw new InvalidDataException($"Item {id}: malformed {name} in {path.Path}.");
            return values[0].Values[0].Value;
        }
        var rarity = Field("[rarity]", 0);
        var durability = Field("[durability]", 0);
        if (!Enum.IsDefined((DropRarity)rarity) || durability < 0 || durability > ushort.MaxValue)
            throw new InvalidDataException($"Invalid drop metadata for item {id} in {path.Path}.");
        return items[id] = new RolledDrop(id, 1, (DropRarity)rarity, path.Kind, (ushort)durability);
    }
}
