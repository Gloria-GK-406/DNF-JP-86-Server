using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Abstractions.Inventory;

/// <summary>
/// Answers whether an item may occupy a given slot of the main inventory.
/// </summary>
/// <remarks>
/// The main inventory is not one flat run. Each item kind owns a band, and an item dropped outside
/// its own band is one the client will not draw where the player looks for it. Those bands are a
/// fact about the item and already come from content, so this service is handed the item's
/// placement rather than deriving a second copy of the band table.
///
/// What content does not answer is the quick slots: slots 3 to 8 of the same list, which the client
/// paints as the number-key bar and which accept almost any kind. They have no owner in content,
/// so they are this service's own rule.
/// </remarks>
public interface IItemSlotRuleService
{
    /// <summary>Whether an item with this placement may sit in that slot of the main inventory.</summary>
    bool CanOccupyMainSlot(ItemPlacement placement, int slotIndex);
}
