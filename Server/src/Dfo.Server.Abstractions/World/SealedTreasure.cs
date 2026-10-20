using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.Events;

namespace Dfo.Server.Abstractions.World;

public interface ISealedTreasureDomainService
{
    bool Supports(int dungeonId);
    ValueTask<DungeonEntryAttempt> EnterAsync(DungeonEntryContext context, CancellationToken ct);
}

public interface ISealedTreasureRuleService
{
    bool Supports(int dungeonId);
    int EventId { get; }
    EventConfiguration InitialActivity { get; }
    ValueTask<DungeonDefinition> DefinitionAsync(CancellationToken ct);
    bool AllowsTraversal(int dungeonId, IEnumerable<int> killedMonsters, MazeCell from, MazeCell target);
    bool AllowsObjectDestruction(int dungeonId, IEnumerable<int> killedMonsters, int objectCode);
    bool RequiresDestroyedObject(int dungeonId, int objectCode);
}
