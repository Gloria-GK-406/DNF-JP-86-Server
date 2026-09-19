using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Abstractions.Inventory;

/// <summary>Whether an item may sit in a slot of some list, or why not.</summary>
/// <remarks>
/// Two refusals rather than one, because a caller with sentences to show a player has a different
/// one for a piece nothing is known about than for a piece aimed at the wrong band.
/// </remarks>
public enum SlotAdmission
{
    Allowed,

    /// <summary>The archive declares no item that can be held under that identifier.</summary>
    ItemUnknown,

    /// <summary>The item is known and that slot is not one it may occupy.</summary>
    SlotNotAllowed,
}

/// <summary>
/// Moving a piece between the bag and what the character is wearing.
/// </summary>
/// <remarks>
/// A move inside one list is the container's own business. A move between two is neither
/// container's: it needs the archive to say which slot a piece is worn in, and the bag's own rule
/// to say whether what comes off may land where it was aimed. Both arrive here so that the flow
/// carrying out the move does not have to hold either rule.
/// </remarks>
public interface IItemMovementDomainService
{
    /// <summary>
    /// The equipment slot this item is worn in, or null when nothing wearable is declared under
    /// that identifier.
    /// </summary>
    ValueTask<BusinessSlot?> WornSlotForAsync(int itemId, CancellationToken cancellationToken);

    /// <summary>Whether this item may occupy that slot of that list.</summary>
    /// <remarks>
    /// Asked of a named list rather than of the main one. It was main-only while the main list was
    /// the only one a move could land in; the avatar list is the second, and one question that
    /// takes the list is one rule rather than two free to disagree about the same item.
    /// </remarks>
    ValueTask<SlotAdmission> MayOccupySlotAsync(
        BusinessSlot slot,
        int itemId,
        CancellationToken cancellationToken);
}
