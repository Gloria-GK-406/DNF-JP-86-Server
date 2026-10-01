using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Creatures;

public sealed record CreatureRenameReceipt(
    CreatureState Creature,
    BusinessSlot SourceSlot,
    InventoryItemCoreState? RemainingSourceCore);

public interface ICreatureNamingDomainService
{
    /// <summary>Atomically consume a creature rename card and change the equipped creature's name.</summary>
    ValueTask<CreatureRenameReceipt?> RenameAsync(SelectedSessionIdentity session,
        BusinessSlot sourceSlot, byte[] nameBytes, CancellationToken cancellationToken);
}
