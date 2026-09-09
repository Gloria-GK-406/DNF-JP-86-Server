using Dfo.Server.Data.Game;
namespace Dfo.Server.Abstractions.Characters;

public interface ICharacterStateProjectionDomainService
{
    ValueTask<SelectCharacterResult> RefreshAsync(SelectCharacterResult result, CancellationToken cancellationToken);
}
