using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Inventory;

public interface IEquipmentDurabilityDomainService
{
    ValueTask<EquipmentDurabilityResult> DecreaseAsync(SelectedSessionIdentity session, byte slotIndex,
        CancellationToken cancellationToken);
}
