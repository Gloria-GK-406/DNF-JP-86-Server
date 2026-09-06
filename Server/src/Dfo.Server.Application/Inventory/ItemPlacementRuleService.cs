using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;

namespace Dfo.Server.Application.Inventory;

/// <summary>
/// The archive's own answer to where an item goes, read in one place for everyone who needs it.
/// </summary>
/// <remarks>
/// Nothing here decides anything the archive does not already declare. What it owns is the reading
/// of the equipment kind, which used to be a private constant in two unrelated files -- the floor
/// description and the pick-up -- that had to agree for a picked-up shoulder to be recognisably
/// the shoulder that was lying there.
/// <para>
/// <see cref="ClassifyAsync"/> is the same ownership taken one step further: what sort of thing a
/// template is has exactly one answer here, and every write of an item asks it rather than reading
/// the kind byte off the row it is about to write.
/// </para>
/// </remarks>
internal sealed class ItemPlacementRuleService(
    IItemPlacementDefinitionSource placements,
    IEquipmentSlotDefinitionSource equipmentSlots) : IItemPlacementRuleService
{
    public ValueTask<ItemPlacement?> PlacementForAsync(
        int itemId, CancellationToken cancellationToken) =>
        placements.GetAsync(itemId, cancellationToken);

    public ValueTask<byte?> WornSlotForAsync(int itemId, CancellationToken cancellationToken) =>
        equipmentSlots.GetAsync(itemId, cancellationToken);

    /// <summary>
    /// The single place a thing is classified.
    /// </summary>
    /// <remarks>
    /// Every write path reaches this method before it writes: the containers, through
    /// <c>StoreBackedItemContainer.PutAsync</c>; character creation, before it writes the pieces a
    /// character is created wearing; and the entry lease save, before it writes a row back.
    /// <para>
    /// There is nothing here but the archive's answer. A stored kind byte is not an input and
    /// cannot become one without changing the signature, which is the whole guard: a write may be
    /// handed a core saying anything, and a classification that could see the byte would be free
    /// to prefer it.
    /// </para>
    /// <para>
    /// <b>Amended; what the sentence above said before is quoted next.</b> It said the guard
    /// mattered because "the byte and the archive still disagree about an avatar -- stored as kind
    /// 8, given kind 1 by the equipment index". They no longer disagree: the equipment index gives
    /// an avatar template the same 8 storage records it under. The guard is unchanged and still
    /// worth having, for the reason now written above it. It is corrected rather than deleted
    /// because the disagreement is why the signature was drawn this way.
    /// </para>
    /// </remarks>
    public async ValueTask<ItemWorldClass> ClassifyAsync(
        int itemId, CancellationToken cancellationToken)
    {
        var placement = await placements.GetAsync(itemId, cancellationToken).ConfigureAwait(false);

        // A template the archive does not declare is a pile, which is how both readings of the
        // kind number already treated one.
        return placement?.WorldClass ?? ItemWorldClass.Interchangeable;
    }

    public async ValueTask<bool> IsEquipmentAsync(int itemId, CancellationToken cancellationToken) =>
        await this.ClassifyAsync(itemId, cancellationToken).ConfigureAwait(false)
            is not ItemWorldClass.Interchangeable;
}
