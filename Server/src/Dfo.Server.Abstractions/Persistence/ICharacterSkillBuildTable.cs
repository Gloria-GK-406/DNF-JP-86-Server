using Dfo.Server.Data.Characters;
namespace Dfo.Server.Abstractions.Persistence;

public interface ICharacterSkillBuildTable
{
    ValueTask<CharacterSkillBuilds> ReadAsync(CharacterId characterId, CancellationToken ct);
    ValueTask EnsureAsync(CharacterId characterId, CancellationToken ct);
    ValueTask AddAsync(CharacterId characterId, SkillBuildId buildId, CancellationToken ct);
    ValueTask SetActiveAsync(CharacterId characterId, SkillBuildId buildId, CancellationToken ct);
}
