using Dfo.Server.Testing;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Effects;

namespace Dfo.Server.Application.Tests.Inventory;

internal static class ConsumptionEffectPreflight
{
    // These fixtures only execute physical consumption and memory effects; no equipment/assets are seeded.
    public static IInventoryEffectPreflightDomainService For(IBusinessInventorySlotDomainService slots) =>
        new InventoryEffectPreflightDomainService(slots, null!, null!, null!, null!, [], containers: TestRequired.Absent<Dfo.Server.Abstractions.Inventory.ICharacterContainerDomainService>(), premiums: TestRequired.Absent<Dfo.Server.Abstractions.Accounts.IPremiumServiceDomainService>(), accountTable: TestRequired.Absent<Dfo.Server.Abstractions.Persistence.IAccountTable>(), usage: TestRequired.Absent<Dfo.Server.Abstractions.Accounts.IPremiumServiceUsageDomainService>(), expiration: null!);
}
