using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Abstractions.Inventory;

/// <summary>
/// Answers where an item belongs: which band of the bag it goes in, which slot it is worn in, and
/// whether it is a piece of equipment at all.
/// </summary>
/// <remarks>
/// One subject read three ways, and the archive declares all three. The band and the stack limit
/// come from the item's own definition; the worn slot comes from the same definition's equipment
/// type; and whether a thing is equipment is one reading of the kind the band table already
/// carries.
/// <para>
/// They are behind one service because the third of them was written down twice -- once where the
/// floor is described and once where a pick-up is banked -- and two copies of a kind number are
/// two things free to disagree. A caller that needs to tell a piece of gear from a pile of
/// materials asks here rather than keeping a number of its own.
/// </para>
/// </remarks>
public interface IItemPlacementRuleService
{
    /// <summary>
    /// Where this item belongs and how much of it one slot holds, or null when the archive
    /// declares no item that can be held under that identifier.
    /// </summary>
    ValueTask<ItemPlacement?> PlacementForAsync(int itemId, CancellationToken cancellationToken);

    /// <summary>
    /// The equipment slot this item is worn in, or null when the archive declares nothing wearable
    /// under that identifier.
    /// </summary>
    ValueTask<byte?> WornSlotForAsync(int itemId, CancellationToken cancellationToken);

    /// <summary>
    /// Whether this item is a piece of equipment.
    /// </summary>
    /// <remarks>
    /// False for an item the archive does not declare, which is the same reading both callers of
    /// the kind number already took: an unknown piece is described and banked as an ordinary
    /// stackable rather than refused.
    /// </remarks>
    ValueTask<bool> IsEquipmentAsync(int itemId, CancellationToken cancellationToken);
}
