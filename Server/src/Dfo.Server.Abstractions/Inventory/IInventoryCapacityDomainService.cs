using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Abstractions.Inventory;

public interface IInventoryCapacityDomainService
{
    ValueTask<int> OpenSlotCountAsync(ContainerId container, BusinessInventorySpace space,
        CancellationToken cancellationToken);
}

public interface IInventoryCapacityRuleService
{
    int OpenSlotCount(BusinessInventorySpace space, int parameter);
}
