using Dfo.Pvf.Schema;
using System.Collections.Immutable;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfWorldDropTableSource(PvfArchiveProvider provider) : IWorldDropTableSource
{
    private static class Fields
    {
        internal static readonly PvfField<IReadOnlyList<int>> WorldDropField = PvfField.Int32List("[world drop]", optional: true);
        internal static readonly PvfField<bool> CloseWorldDropField = PvfField.Marker("[/world drop]", optional: true);
    }

    internal const string Path = "etc/worlddrop.etc";

    public IReadOnlyList<WorldDropLevel> Load()
    {
        var archive = provider.Archive;
        var sections = (PvfDocument.TryOpen(archive, Path, PvfPreamble.Ignore)?.Sections ?? []);
        if (sections.Count != 2 || sections[0].Name != Fields.WorldDropField.Name
            || sections[1].Name != Fields.CloseWorldDropField.Name || sections[1].Values.Count != 0
            || sections[0].Values.Any(token => token.Kind != PvfTokenKind.Int32))
            throw new InvalidDataException($"{Path}: missing or malformed world drop section.");
        var metadata = new PvfDropItemReader(archive);
        WorldDropEntry Resolve(int id)
        {
            var item = metadata.Get(id) ?? throw new InvalidDataException($"{Path}: missing item {id}.");
            return new(id, 0, item.Rarity, item.Kind, item.Durability);
        }
        return Parse(Fields.WorldDropField.Decode(archive, sections[0].Values, Path), Resolve);
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
