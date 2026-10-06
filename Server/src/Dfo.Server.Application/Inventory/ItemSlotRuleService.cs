using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Inventory;

/// <summary>Coarse feature admission followed by physical slot and template restrictions.</summary>
public sealed class ItemSlotRuleService : IItemSlotRuleService
{
    // Physical placement only; current purchased capacity is enforced by the business container.
    public bool CanOccupySlot(BusinessSlot slot, ItemPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(placement);
        if (!ItemContainerAdmission.For(slot.Space).Matches(placement.Traits))
            return false;

        return slot.Space switch
        {
            BusinessInventorySpace.SharedMaterial =>
                placement.WorldClass == ItemWorldClass.Interchangeable && placement.Destination.Contains(slot),
            BusinessInventorySpace.QuickSlot => true,
            BusinessInventorySpace.TowerQuickSlot => placement.Destination.Contains(slot),
            BusinessInventorySpace.Avatar => placement.WorldClass == ItemWorldClass.Avatar,
            BusinessInventorySpace.PersonalCargo or BusinessInventorySpace.AccountCargo => true,
            BusinessInventorySpace.CreatureInventory =>
                placement.WorldClass == ItemWorldClass.Creature && placement.Destination.Contains(slot),
            BusinessInventorySpace.CreatureEquipment or BusinessInventorySpace.CreatureConsumable =>
                placement.Destination.Contains(slot),
            _ when slot.Space.IsCarriedItemSpace() => placement.Destination.Contains(slot),
            _ => false,
        };
    }

    internal static bool AllowsQuickSlot(ItemPlacement placement) =>
        ItemContainerAdmission.For(BusinessInventorySpace.QuickSlot).Matches(placement.Traits);
}
