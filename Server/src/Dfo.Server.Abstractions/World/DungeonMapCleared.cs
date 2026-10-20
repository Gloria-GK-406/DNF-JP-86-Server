using System.Collections.Immutable;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.World;

/// <summary>A confirmed clear of one map occurrence, independent of the consumer's current location.</summary>
public sealed record DungeonMapCleared(DungeonRunId RunId, int DungeonId, DungeonDifficulty Difficulty,
    MazeCell Cell, int MapId, long RoomVersion, DateTimeOffset ClearedAt,
    ImmutableArray<DungeonRunParticipant> Participants) : IDomainEvent
{
    public bool IsBossRoom { get; init; }
    public int? CycleQuestId { get; init; }
    public ImmutableArray<Dfo.Server.Abstractions.Quests.CycleQuestAcceptance> CycleAcceptances { get; init; } = [];
}
