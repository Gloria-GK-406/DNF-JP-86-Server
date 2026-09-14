using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.World;

/// <summary>
/// Observes a participant leaving one exact dungeon run after the domain run accepted the leave.
/// </summary>
/// <remarks>
/// The domain owns stable run and character identities only. Transport layers can implement this
/// seam to discard their own run-scoped delivery state without putting a connection generation
/// into the dungeon aggregate.
/// </remarks>
public interface IDungeonRunParticipantDepartureSink
{
    void ParticipantDeparted(DungeonRunId runId, CharacterId characterId);
}
