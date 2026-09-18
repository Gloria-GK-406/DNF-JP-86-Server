using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.World;

namespace Dfo.Server.Protocol.Tests.Game;

internal sealed class RuntimeCurrentView(
    ISelectedSessionRuntimeDirectory runtime,
    ICharacterStateProjectionDomainService? projection = null,
    ICharacterInventoryEntryDomainService? inventory = null)
    : ISelectedCharacterCurrentViewDomainService
{
    public async ValueTask<SelectCharacterResult?> TryLoadAsync(
        SelectedSessionIdentity session, CancellationToken cancellationToken)
    {
        if (runtime is not ILegacySessionSnapshotFixture fixture
            || !fixture.TryGet(session, out var result, out _) || result is null)
            return null;
        if (inventory is not null)
            result = result with
            {
                CharacterInventory = await inventory.LoadAsync(
                    session.AccountId, session.CharacterId, cancellationToken).ConfigureAwait(false),
            };
        return projection is null ? result
            : await projection.RefreshAsync(result, cancellationToken).ConfigureAwait(false);
    }
}
