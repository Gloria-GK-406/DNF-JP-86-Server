using Dfo.Server.Data.Characters;
namespace Dfo.Server.Abstractions.Characters;

public interface ICharacterSkillStateDomainService
{
    ValueTask ResetAsync(CharacterId characterId, SkillBuildId build, CancellationToken cancellationToken);
    ValueTask ResetAllAsync(CharacterId characterId, CancellationToken cancellationToken);
    ValueTask<CharacterSkillBuilds> BuildsAsync(CharacterId characterId, CancellationToken cancellationToken);
    ValueTask UnlockAsync(CharacterId characterId, SkillBuildId build, CancellationToken cancellationToken);
    ValueTask ActivateAsync(CharacterId characterId, SkillBuildId build, CancellationToken cancellationToken);

    ValueTask ReconcileLevelAsync(CharacterId characterId, CancellationToken cancellationToken);

    ValueTask ReconcileAsync(CharacterId characterId, CancellationToken cancellationToken);

    ValueTask<CharacterSkillFacts?> ReadAsync(CharacterId characterId, CancellationToken cancellationToken);
}
