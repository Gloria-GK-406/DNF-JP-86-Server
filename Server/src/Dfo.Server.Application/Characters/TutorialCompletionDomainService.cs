using Dfo.Server.Abstractions.Characters;

namespace Dfo.Server.Application.Characters;

internal sealed class TutorialCompletionDomainService(ICharacterTutorialRepository characters)
    : ITutorialCompletionDomainService
{
    public ValueTask<bool> CompleteOnceAsync(OwnedCharacterId character, CancellationToken cancellationToken) =>
        characters.TrySetCompletedAsync(character, cancellationToken);
}
