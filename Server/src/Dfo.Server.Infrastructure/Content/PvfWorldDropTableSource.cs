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
        var metadata = new PvfDropItemReader(archive);
        WorldDropEntry Resolve(int id)
        {
            var item = metadata.Get(id) ?? throw new InvalidDataException($"{Path}: missing item {id}.");
            return new(id, 0, item.Rarity, item.Kind, item.Durability);
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
