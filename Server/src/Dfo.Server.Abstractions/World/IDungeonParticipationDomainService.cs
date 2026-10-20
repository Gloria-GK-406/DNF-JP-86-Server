using Dfo.Server.Data.Characters;
namespace Dfo.Server.Abstractions.World;

public enum DungeonParticipationMode { Ordinary, IllusionTower, BossTower }
public sealed record DungeonParticipationOwner(DungeonParticipationMode Mode, Guid RunId);
/// <summary>Process-local, transaction-visible exclusive participation across dungeon modes.</summary>
public interface IDungeonParticipationDomainService
{
    DungeonParticipationOwner? Find(CharacterId character);
    bool TryClaim(IReadOnlyList<CharacterId> characters, DungeonParticipationOwner owner);
    void Release(CharacterId character, DungeonParticipationOwner expected);
    // Ordinary dungeon departure is an existing immediate lifecycle boundary, including entry commit.
    void ReleaseCommitted(CharacterId character, DungeonParticipationOwner expected);
}
