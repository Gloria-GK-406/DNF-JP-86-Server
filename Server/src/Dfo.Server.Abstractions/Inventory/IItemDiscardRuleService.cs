using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Abstractions.Inventory;

public interface IItemDiscardRuleService
{
    ItemDiscardRejection ValidateRequest(
        InventoryContainerType container,
        byte entryCount,
        ushort operationType,
        int slotIndex,
        int itemId,
        int count);

    ItemDiscardRejection ValidateItem(ContainerSlotItem item, int itemId, int count);
}
