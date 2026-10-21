using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Sessions;

internal sealed class SelectedCharacterCurrentViewDomainService(
    ISelectedSessionDomainService sessions,
    ICharacterCurrentStateQueryDomainService currentState) : ISelectedCharacterCurrentViewDomainService
{
    public async ValueTask<SelectCharacterResult?> TryLoadAsync(
        SelectedSessionIdentity session, CancellationToken cancellationToken)
    {
        if (!sessions.IsInWorld(session))
            return null;

        var result = await currentState.ReadAsync(session, cancellationToken)
            .ConfigureAwait(false);

        // A replacement can publish while the independently-owned facts are being read.
        return sessions.IsInWorld(session)
            ? result
            : null;
    }
}
