using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Abstractions.Inventory;

public interface ISkillMaterialConsumptionRuleService
{
    bool IsValidRequest(IReadOnlyList<SkillMaterialCost> costs);
    bool IsConsumable(ContainerSlotItem item, int expectedItemId);
}
