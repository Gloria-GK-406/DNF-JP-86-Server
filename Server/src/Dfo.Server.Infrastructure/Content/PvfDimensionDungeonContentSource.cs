using System.Collections.Frozen;
using Dfo.Pvf;
using Dfo.Pvf.Schema;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfDimensionDungeonContentSource(PvfArchiveProvider provider) : IDimensionDungeonContentSource
{
    private readonly Lazy<DimensionDungeonContent> cache = new(() => Read(provider.Archive));
    public DimensionDungeonContent Load() => cache.Value;

    internal static DimensionDungeonContent Read(PvfArchive archive)
    {
        const string configPath = "etc/dimensiongate.etc";
        var config = PvfDocument.Open(archive, configPath);
        int[] Ints(PvfSection section, string path) => PvfField.Int32List(section.Name, optional: true)
            .Decode(archive, section.Values, path).ToArray();
        int Count(string tag)
        {
            var sections = config.Sections.Where(s => s.Name == tag).ToArray();
            if (sections.Length != 1 || Ints(sections[0], configPath) is not [var value] || value < 0)
                throw new InvalidDataException($"{configPath}: invalid {tag}.");
            return value;
        }
        var ids = new HashSet<int>();
        foreach (var row in PvfListFile.Read(archive, "dungeon/dungeon.lst"))
        {
            var script = archive.OpenScript("dungeon/" + row.RelativePath);
            while (script.Read(out var token))
                if (token.Kind == PvfTokenKind.Identifier && archive.ResolveString(token.Value) == "[dimension dungeon]")
                { ids.Add(row.Id); break; }
        }
        const string dropsPath = "etc/dimensiongatedroplist.etc";
        var items = new PvfDropItemReader(archive);
        var pools = new Dictionary<(int, int), DimensionChroniclePool>();
        (int, int)? key = null;
        IReadOnlyList<RolledDrop> normal = [], set = [];
        foreach (var section in PvfDocument.Open(archive, dropsPath).Sections)
        {
            if (section.Name == "[chronicle grow type]")
            {
                if (key is not null || Ints(section, dropsPath) is not [var job, var grow] || job < 0 || grow < 0)
                    throw new InvalidDataException($"{dropsPath}: invalid career group.");
                key = (job, grow & 15); normal = []; set = [];
            }
            else if (section.Name is "[normal chronicle list]" or "[set chronicle list]")
            {
                if (key is null) throw new InvalidDataException($"{dropsPath}: items outside a career group.");
                var list = Ints(section, dropsPath).Distinct().Select(id => items.Get(id) is { Kind: DropItemKind.Equipment } item
                    ? item : throw new InvalidDataException($"{dropsPath}: missing chronicle equipment {id}.")).ToArray();
                if (section.Name == "[normal chronicle list]") normal = list; else set = list;
            }
            else if (section.Name == "[/chronicle grow type]")
            {
                if (key is null || !pools.TryAdd(key.Value, new(normal, set)))
                    throw new InvalidDataException($"{dropsPath}: duplicate or unbalanced career group.");
                key = null;
            }
        }
        if (key is not null || ids.Count == 0 || pools.Count == 0)
            throw new InvalidDataException("Incomplete dimension dungeon content.");
        // UpstreamStatic: DimensionDropSystem.FragmentItemId; not an inferred native rate.
        var fragment = items.Get(3311) ?? throw new InvalidDataException("Missing dimension fragment 3311.");
        return new(ids.ToFrozenSet(), Count("[daily default enter count]"), Count("[daily default extra enter count]"),
            pools.ToFrozenDictionary(), fragment);
    }
}
