using System.Collections.Immutable;
using Dfo.Pvf;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfMonsterDeclaredDropTableSource(PvfArchiveProvider provider) : IMonsterDeclaredDropTableSource
{
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
                var sections = PvfNpcShopDefinitionSource.Sections(archive, path)
                    .Where(section => section.Name == "[item]" && section.Values.Count > 0).ToArray();
                if (sections.Length == 0) continue;
                if (sections.Length != 1 || sections[0].Values.Any(token => token.Kind != PvfTokenKind.Int32))
                    throw new InvalidDataException($"{path}: malformed [item].");
                var table = Parse(row.Id, sections[0].Values.Select(token => token.Value).ToArray(),
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
