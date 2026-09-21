using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;
namespace Dfo.Server.Application.World;

internal sealed class DungeonRevivalDomainService(ISelectedSessionDomainService sessions,
    ICharacterContainerDomainService containers, IDungeonRunDomainService runs) : IDungeonRevivalDomainService
{
    public async ValueTask<DungeonRevivalResult> ReviveAsync(SelectedSessionIdentity payer,
        CharacterId target, DungeonRunId runId, CancellationToken cancellationToken)
    {
        if (sessions.FindByCharacter(payer.CharacterId) != payer) return new(false);
        var bag = containers.FindContainer(payer.CharacterId, InventoryContainerType.Main);
        var coin = bag is null ? null : await bag.ReadSlotAsync(1, cancellationToken).ConfigureAwait(false);
        if (coin is null || coin.Core.ItemId != 1 || coin.Core.Value <= 0) return new(false);
        if (!runs.StageRevival(payer.CharacterId, target, runId)) return new(false);
        var remaining = coin.Core with { Value = coin.Core.Value - 1 };
        // Keep the virtual counter at zero; the transactional item store commits before life state.
        await bag!.PutAsync(1, remaining, cancellationToken).ConfigureAwait(false);
        return new(true, remaining);
    }
}
