using Dfo.Server.Data.Game;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Characters;

/// <summary>Reads current character facts and live position, without login preparation or placement.</summary>
public interface ICharacterCurrentStateQueryDomainService
{
    ValueTask<SelectCharacterResult> ReadAsync(
        SelectedSessionIdentity session, CancellationToken cancellationToken);
}
