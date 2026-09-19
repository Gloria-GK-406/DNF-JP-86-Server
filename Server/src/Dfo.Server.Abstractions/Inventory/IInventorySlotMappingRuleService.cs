using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Abstractions.Inventory;

/// <summary>The sole translation between A21 wire slots and business inventory slots.</summary>
public interface IInventorySlotMappingRuleService
{
    /// <exception cref="InventorySlotMappingException">The game coordinate is not a normal item slot.</exception>
    BusinessSlot ToBusinessSlot(GameSlot source);

    /// <exception cref="InventorySlotMappingException">The backing coordinate is not a normal item slot.</exception>
    BusinessSlot ToBusinessSlot(InventoryStorageSlot source);

    /// <exception cref="InventorySlotMappingException">The business coordinate cannot be represented by A21.</exception>
    GameSlot ToGameSlot(BusinessSlot source);
}
