using Dfo.Server.Abstractions.World;
namespace Dfo.Server.Abstractions.Characters;

public interface ICharacterExperienceInitializationDomainService
{
    ValueTask<CharacterExperienceScale> InitializeAsync(CancellationToken cancellationToken);
}
