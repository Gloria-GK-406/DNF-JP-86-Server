using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Inventory;

public interface IItemDiscardDomainService
{
    ValueTask<ItemDiscardResult> DiscardAsync(
        SelectedSessionIdentity session,
        InventoryContainerType container,
        byte entryCount,
        ushort operationType,
        int slotIndex,
        int itemId,
        int count,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}
