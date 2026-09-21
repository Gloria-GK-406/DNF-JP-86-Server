using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;
namespace Dfo.Server.Abstractions.World;

public sealed record DungeonRevivalResult(bool Succeeded, InventoryItemCoreState? RemainingCoin = null);
public interface IDungeonRevivalDomainService
{
    ValueTask<DungeonRevivalResult> ReviveAsync(SelectedSessionIdentity payer,
        CharacterId target, DungeonRunId runId, CancellationToken cancellationToken);
}
