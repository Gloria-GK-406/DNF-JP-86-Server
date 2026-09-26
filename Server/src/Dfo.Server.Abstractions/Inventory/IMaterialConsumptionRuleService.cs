using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Abstractions.Inventory;

public interface IMaterialConsumptionRuleService
{
    bool IsValidRequest(IReadOnlyList<MaterialCost> costs);
    bool IsConsumable(ContainerSlotItem item, int expectedItemId);
}
