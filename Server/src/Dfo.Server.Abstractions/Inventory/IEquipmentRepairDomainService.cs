using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Inventory;

public interface IEquipmentRepairDomainService
{
    ValueTask<EquipmentRepairResult> RepairAsync(SelectedSessionIdentity session, InventoryContainerType container,
        int slotIndex, CancellationToken cancellationToken);
}
