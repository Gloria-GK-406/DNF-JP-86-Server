using Dfo.Server.Abstractions.World;
namespace Dfo.Server.Abstractions.Persistence;

/// <summary>Install the immutable progression constraints before accepting game traffic.</summary>
public interface ICharacterExperienceStoragePolicy
{
    ValueTask InstallAsync(CharacterExperienceScale scale, CancellationToken ct);
}
