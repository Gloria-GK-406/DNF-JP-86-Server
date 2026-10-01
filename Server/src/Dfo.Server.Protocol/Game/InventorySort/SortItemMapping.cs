using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Protocol.Game.InventorySort;

internal static class SortItemMapping
{
    public static bool TryMap(SortItemRequest message, out BusinessInventorySpace space, out InventorySortOrder order)
    {
        var mapped = (message.ListType, message.Category) switch
        {
            (0, 1) => BusinessInventorySpace.CarriedEquipment,
            (0, 2) => BusinessInventorySpace.Consumable,
            (0, 3) => BusinessInventorySpace.Material,
            (0, 4) => BusinessInventorySpace.Quest,
            (0, 9) => BusinessInventorySpace.AvatarEmblem,
            (0, 10) => BusinessInventorySpace.ExpertMaterial,
            (1, 8) => BusinessInventorySpace.Avatar,
            (7, 6) => BusinessInventorySpace.CreatureEquipment,
            (7, 7) => BusinessInventorySpace.CreatureConsumable,
            _ => (BusinessInventorySpace?)null,
        };
        space = mapped.GetValueOrDefault();
        order = InventorySortOrder.Default;
        if (mapped is null || message.Condition > 4) return false;
        var allowed = space switch
        {
            BusinessInventorySpace.CarriedEquipment => message.Condition is 0 or 1 or 2 or 3,
            BusinessInventorySpace.Consumable => message.Condition is 0 or 3 or 4,
            BusinessInventorySpace.Material or BusinessInventorySpace.ExpertMaterial => message.Condition is 0 or 3,
            BusinessInventorySpace.Avatar => message.Condition is 0 or 1 or 3,
            _ => message.Condition == 0,
        };
        // Like the A21 reference, an unsupported condition on a valid page uses its default order.
        order = allowed ? (InventorySortOrder)message.Condition : InventorySortOrder.Default;
        return true;
    }
}
