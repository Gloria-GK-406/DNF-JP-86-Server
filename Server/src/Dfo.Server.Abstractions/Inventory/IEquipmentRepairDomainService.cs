using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Inventory;

public interface IEquipmentRepairDomainService
{
    ValueTask<EquipmentRepairResult> RepairWithCouponAsync(SelectedSessionIdentity session,
        BusinessSlot target, BusinessSlot coupon, CancellationToken cancellationToken);
    ValueTask<EquipmentRepairResult> AutoRepairAsync(SelectedSessionIdentity session, CancellationToken cancellationToken);
    ValueTask<EquipmentRepairResult> RepairAsync(
        SelectedSessionIdentity session, BusinessSlot? slot, CancellationToken cancellationToken);
}
