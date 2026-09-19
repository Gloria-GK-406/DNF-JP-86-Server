using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.Inventory;

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
    /// <summary>Whether an item with this placement may sit in this business slot.</summary>
    /// <remarks>
    /// The main list is not the only one a move can name any more, and each list a move may enter
    /// has to answer for what it lets in. This is where those answers live, together, because the
    /// alternative is a branch inside whatever is carrying out the move -- and then the next list
    /// to be served puts a second branch somewhere else.
    /// <para>
    /// <b>False for a list this server does not serve.</b> A list with no rule written for it is
    /// a door nobody has decided about, and the safe reading of an undecided door is shut. That
    /// makes adding a list a deliberate act rather than something that happens by falling through.
    /// </para>
    /// </remarks>
    bool CanOccupySlot(BusinessSlot slot, ItemPlacement placement);
}
