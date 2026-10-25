using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.Networking;

/// <summary>Withdraws a departed character from the run's exact-session projection audience.</summary>
public interface IDungeonRunProjectionDomainService
{
    void Register(DungeonRunId runId, IReadOnlyList<Dfo.Server.Data.World.SelectedSessionIdentity> participants);
    void ParticipantDeparted(DungeonRunId runId, CharacterId characterId);
}
