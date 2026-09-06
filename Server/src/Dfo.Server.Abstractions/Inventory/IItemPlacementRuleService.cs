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
    /// What sort of thing this template is: a pile, or an individual piece of one of three kinds.
    /// </summary>
    /// <remarks>
    /// <b>The one place the question is answered.</b> Every write of an item passes through here
    /// before it is made, so a thing is classified as it is written rather than once during some
    /// migration -- and the two writes that used to answer it for themselves cannot drift.
    /// <para>
    /// It takes a template identifier and nothing else. That is not brevity: it is what makes the
    /// answer impossible to take from the kind byte a stored row happens to carry, and a caller is
    /// free to hand any core it likes to a write.
    /// </para>
    /// <para>
    /// <b>Amended; what this paragraph said before is quoted next.</b> It said: "Those two still
    /// disagree -- an avatar is stored as kind 8 and the archive's equipment index gives it kind 1
    /// -- and a classifier handed the row would be free to believe the row. The contract creature
    /// used to be a second such pair and no longer is: the archive now gives it the kind storage
    /// records it under." The avatar pair is answered now too: the archive gives an avatar template
    /// the 8 storage records it under, so no template is left that the two sources disagree about.
    /// It is corrected rather than deleted because the reason for the signature is unchanged and
    /// the record of what it was guarding against is what explains the shape.
    /// </para>
    /// </remarks>
    ValueTask<ItemWorldClass> ClassifyAsync(int itemId, CancellationToken cancellationToken);

    /// <summary>
    /// Whether this item is a piece of equipment.
    /// </summary>
    /// <remarks>
    /// One reading of <see cref="ClassifyAsync"/> rather than a second answer beside it: the
    /// archive's equipment index is exactly what makes a template an individual piece, so
    /// "equipment" here means any of the three individual kinds, which is what the kind number
    /// this used to read meant as well.
    /// <para>
    /// False for an item the archive does not declare, which is the same reading both callers of
    /// the kind number already took: an unknown piece is described and banked as an ordinary
    /// stackable rather than refused.
    /// </para>
    /// </remarks>
    ValueTask<bool> IsEquipmentAsync(int itemId, CancellationToken cancellationToken);
}
