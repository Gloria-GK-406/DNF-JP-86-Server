using Dfo.Pvf.Schema;
using System.Collections.Immutable;
using Dfo.Pvf;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfMonsterDeclaredDropTableSource(PvfArchiveProvider provider) : IMonsterDeclaredDropTableSource
{
    private static class Fields
    {
        internal static readonly PvfField<IReadOnlyList<int>> ItemField = PvfField.Int32List("[item]", optional: true);
    }

    public IReadOnlyList<MonsterDeclaredDropTable> Load()
    {
        var archive = provider.Archive;
        var items = new PvfDropItemReader(archive);
        var result = new List<MonsterDeclaredDropTable>();
        var errors = new List<string>();
        foreach (var row in PvfListFile.Read(archive, "monster/monster.lst"))
        {
            var path = "monster/" + row.RelativePath;
            try
            {
                var sections = (PvfDocument.TryOpen(archive, path, PvfPreamble.Ignore)?.Sections ?? [])
                    .Where(section => section.Name == Fields.ItemField.Name).ToArray();
                if (sections.Length == 0) continue;
                var declarations = sections.Select(section => Fields.ItemField.Decode(archive, section.Values, path)).ToArray();
                var activeDeclarations = declarations.Where(values => values.Count > 0).ToArray();
                if (activeDeclarations.Length > 1)
                    throw new InvalidDataException($"{path}: duplicate nonempty {Fields.ItemField.Name}.");
                if (activeDeclarations.Length == 0) continue;
                var table = Parse(row.Id, activeDeclarations[0],
                    id => items.Get(id) ?? throw new InvalidDataException($"{path}: missing item {id}."));
                if (!table.Entries.IsEmpty) result.Add(table);
            }
            catch (InvalidDataException error) { errors.Add(error.Message); }
        }
        if (errors.Count > 0) throw new InvalidDataException(string.Join("\n", errors));
        return result;
    }

    internal static MonsterDeclaredDropTable Parse(int monsterId, IReadOnlyList<int> values, Func<int, RolledDrop> resolve)
    {
        if (values.Count % 2 != 0) throw new InvalidDataException($"Monster {monsterId}: incomplete [item] pair.");
        var entries = ImmutableArray.CreateBuilder<MonsterDeclaredDropEntry>();
        long total = 0;
        for (var i = 0; i < values.Count; i += 2)
        {
            var id = values[i];
            var weight = values[i + 1];
            if (id <= 0 || weight < 0) throw new InvalidDataException($"Monster {monsterId}: invalid [item] pair.");
            total += weight;
            if (total > int.MaxValue) throw new InvalidDataException($"Monster {monsterId}: [item] total exceeds supported integer range.");
            if (weight > 0) entries.Add(new(resolve(id), weight));
        }
        return new(monsterId, entries.ToImmutable());
    }
}
