using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Inventory;

internal sealed class InventorySlotProjectionDomainService(
    IInventorySlotMappingRuleService mappings) : IInventorySlotProjectionDomainService
{
    public GameSlot Project(BusinessSlot slot) => mappings.ToGameSlot(slot);
}
