using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Characters;

/// <summary>Reads one current same-listener character for the native town inspect flow.</summary>
public interface IOtherCharacterDetailQueryDomainService
{
    ValueTask<CharacterDetailQueryResult> QueryAsync(
        SelectedSessionIdentity requester,
        ushort targetWireUserId,
        CancellationToken cancellationToken);
}
