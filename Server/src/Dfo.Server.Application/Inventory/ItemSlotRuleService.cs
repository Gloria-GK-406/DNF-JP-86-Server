using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Inventory;

/// <summary>
/// The item's own band, plus the quick slots that sit in front of every band.
/// </summary>
/// <remarks>
/// The quick-slot range is 3 to 8 -- six slots, ahead of the equipment band that starts at 9.
/// Measured from the reference server, which names the same range in three independent places
/// (insert, move and slot-bound resolution), and confirmed live on 2026-08-18: the tutorial's
/// potion arrived in slot 65, the first slot of the consumable band, and the client immediately
/// asked to move it to slot 3.
///
/// The exclusions are the kinds whose home is another list entirely -- creatures and their gear,
/// avatars and their emblems. Everything else may sit on the bar, which is why the range is
/// expressed as an allowance with holes rather than as a list of permitted kinds.
/// </remarks>
public sealed class ItemSlotRuleService : IItemSlotRuleService
{
    private const byte KindCreature = 5;
    private const byte KindCreatureEquipment = 6;
    private const byte KindCreatureConsumable = 7;
    private const byte KindAvatar = 8;
    private const byte KindAvatarEmblem = 9;

    /// <summary>How far the avatar list runs, and what may sit anywhere in it.</summary>
    /// <remarks>
    /// Two hundred and ten slots, and only avatars. Read out of the reference A21 server:
    /// <c>ItemSlotBoundService.GetAvatarOpenRange</c> takes a list parameter, discards it, and
    /// returns <c>0..209</c> whatever it is handed; <c>TryGetItemKindBySlot</c> answers
    /// <c>KindAvatar</c> for every slot of that list rather than dividing it into bands the way
    /// the main list is divided. <b>Read from upstream source, not measured against a client.</b>
    /// <para>
    /// The list has no bands and no expansion. Upstream has no avatar-expansion concept at all,
    /// which is why the parameter it is handed goes unread -- see the open question about what
    /// that parameter means to the client, which this does not answer and does not depend on.
    /// </para>
    /// </remarks>
    public bool CanOccupySlot(BusinessSlot slot, ItemPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(placement);

        return slot.Space switch
        {
            BusinessInventorySpace.QuickSlot => AllowsQuickSlot(placement.ItemKind),
            BusinessInventorySpace.Avatar => placement.WorldClass == ItemWorldClass.Avatar,
            BusinessInventorySpace.CreatureInventory =>
                placement.WorldClass == ItemWorldClass.Creature && placement.Destination.Contains(slot),
            BusinessInventorySpace.CreatureEquipment =>
                placement.ItemKind == KindCreatureEquipment && placement.Destination.Contains(slot),
            BusinessInventorySpace.CreatureConsumable =>
                placement.ItemKind == KindCreatureConsumable && placement.Destination.Contains(slot),
            _ when slot.Space.IsCarriedItemSpace() => placement.Destination.Contains(slot),
            _ => false,
        };
    }

    /// <summary>
    /// Whether a kind may sit on the bar at all.
    /// </summary>
    /// <remarks>
    /// Read by two callers and written once. Whether an arriving item is <em>offered</em> the bar
    /// is <c>ItemArrivalRuleService</c>'s decision and whether it may <em>stay</em> there is this
    /// one's, and the two would be free to disagree if each carried its own list of exclusions --
    /// which is how an item could be placed somewhere a drag could never have put it.
    /// </remarks>
    internal static bool AllowsQuickSlot(byte itemKind) =>
        itemKind is not (KindCreature or KindCreatureEquipment or KindCreatureConsumable
            or KindAvatar or KindAvatarEmblem);
}
