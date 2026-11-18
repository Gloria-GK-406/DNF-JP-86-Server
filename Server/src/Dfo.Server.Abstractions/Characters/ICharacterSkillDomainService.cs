using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.Characters;

public interface ICharacterSkillDomainService
{
    /// <summary>Reads the given build by its zero-based ID, independently of the active build.</summary>
    /// <remarks>Points are absent for a group without a point ledger.</remarks>
    ValueTask<CharacterSkillPageSnapshot?> GetAsync(CharacterId characterId, SkillGroup group,
        SkillBuildId build, CancellationToken cancellationToken);
}
