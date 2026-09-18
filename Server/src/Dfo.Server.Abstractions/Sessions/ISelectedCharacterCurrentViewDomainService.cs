using Dfo.Server.Data.Game;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Sessions;

/// <summary>Builds a one-shot current projection for a published, exact selected session.</summary>
public interface ISelectedCharacterCurrentViewDomainService
{
    ValueTask<SelectCharacterResult?> TryLoadAsync(
        SelectedSessionIdentity session, CancellationToken cancellationToken);
}
