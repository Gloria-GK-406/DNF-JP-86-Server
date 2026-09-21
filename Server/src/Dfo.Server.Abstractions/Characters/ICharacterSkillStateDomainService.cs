using Dfo.Server.Data.Characters;
namespace Dfo.Server.Abstractions.Characters;

public interface ICharacterSkillStateDomainService
{
    ValueTask ReconcileAsync(CharacterId characterId, CancellationToken cancellationToken);

    ValueTask<CharacterSkillFacts?> ReadAsync(CharacterId characterId, CancellationToken cancellationToken);
}
