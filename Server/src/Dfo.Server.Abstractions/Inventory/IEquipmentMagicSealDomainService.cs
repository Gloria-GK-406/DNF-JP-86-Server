using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Inventory;

public interface IEquipmentMagicSealDomainService
{
    ValueTask<EquipmentMagicSealResult> UnsealAsync(
        SelectedSessionIdentity session, BusinessSlot targetSlot, BusinessSlot? scrollSlot,
        CancellationToken cancellationToken);

    ValueTask<EquipmentMagicSealResult> ChangeAsync(
        SelectedSessionIdentity session, BusinessSlot targetSlot, BusinessSlot? materialSlot,
        byte optionIndex, CancellationToken cancellationToken);
}
