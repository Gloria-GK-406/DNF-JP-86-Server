using System.Collections.Immutable;
using Dfo.Server.Abstractions.World;
namespace Dfo.Server.Abstractions.Content;

public sealed record MonsterDeclaredDropEntry(RolledDrop Item, int Weight);
public sealed record MonsterDeclaredDropTable(int MonsterId, ImmutableArray<MonsterDeclaredDropEntry> Entries);
public interface IMonsterDeclaredDropTableSource
{
    IReadOnlyList<MonsterDeclaredDropTable> Load();
}
public interface IMonsterDeclaredDropRuleService
{
    int Initialize();
    IReadOnlyList<RolledDrop> Draw(MonsterDropRequest request);
}
