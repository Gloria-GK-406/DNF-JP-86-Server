using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Abstractions.Inventory;

public interface IItemDiscardRuleService
{
    ItemDiscardRejection ValidateRequest(
        BusinessSlot slot,
        byte entryCount,
        ushort operationType,
        int itemId,
        int count);

    ItemDiscardRejection ValidateItem(ContainerSlotItem item, int itemId, int count);
}
