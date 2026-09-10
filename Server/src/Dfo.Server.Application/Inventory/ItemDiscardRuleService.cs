using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Inventory;

internal sealed class ItemDiscardRuleService : IItemDiscardRuleService
{
    public ItemDiscardRejection ValidateRequest(
        InventoryContainerType container,
        byte entryCount,
        ushort operationType,
        int slotIndex,
        int itemId,
        int count) =>
        container == InventoryContainerType.Main
        && entryCount == 1
        && operationType == 1
        && slotIndex is >= 3 and < 354
        && itemId > 0
        && count > 0
            ? ItemDiscardRejection.None
            : ItemDiscardRejection.InvalidRequest;

    public ItemDiscardRejection ValidateItem(ContainerSlotItem item, int itemId, int count)
    {
        if (item.ItemId != itemId)
            return ItemDiscardRejection.ItemUnavailable;
        if (count > item.Count || !item.IsPile && count != 1)
            return ItemDiscardRejection.InvalidRequest;
        if (item.Core.EquipmentLockId != 0)
            return ItemDiscardRejection.Locked;
        return item.WorldClass is ItemWorldClass.Equipment or ItemWorldClass.Interchangeable
            ? ItemDiscardRejection.None
            : ItemDiscardRejection.CannotDiscard;
    }
}
