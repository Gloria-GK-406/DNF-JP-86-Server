namespace Dfo.Server.Abstractions.Characters;

public interface ITutorialCompletionDomainService
{
    ValueTask<bool> CompleteOnceAsync(OwnedCharacterId character, CancellationToken cancellationToken);
}

public interface ICharacterTutorialRepository
{
    ValueTask<bool> TrySetCompletedAsync(OwnedCharacterId character, CancellationToken cancellationToken);
}
