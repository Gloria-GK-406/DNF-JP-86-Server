using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.World;

public sealed record DungeonParticipantLeft(CharacterId CharacterId, DungeonRunId RunId, int DungeonId) : IDomainEvent;

public interface IDungeonLifecycleDomainService
{
    ValueTask RecordDepartureAsync(DungeonParticipantLeft fact, CancellationToken cancellationToken = default);
}
