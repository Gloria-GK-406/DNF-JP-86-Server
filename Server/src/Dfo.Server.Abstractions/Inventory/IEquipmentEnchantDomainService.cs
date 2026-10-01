using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Inventory;

public interface IEquipmentEnchantDomainService
{
    ValueTask<EquipmentEnchantResult> EnchantAsync(
        SelectedSessionIdentity session, BusinessSlot beadSlot, BusinessSlot targetSlot,
        CancellationToken cancellationToken);
}
