using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;

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
    public async ValueTask<ItemPlacement?> PlacementForAsync(int itemId, CancellationToken cancellationToken)
    {
        var placement = await placements.GetAsync(itemId, cancellationToken).ConfigureAwait(false);
        return SharedPlacement(itemId, placement is null ? null : placement with { ItemId = itemId });
    }

    public async ValueTask<ItemPlacement?> PlacementForAsync(int itemId, BusinessInventorySpace destination,
        CancellationToken cancellationToken)
    {
        var placement = await placements.GetAsync(itemId, cancellationToken).ConfigureAwait(false);
        if (placement is not null) placement = placement with { ItemId = itemId };
        return destination == BusinessInventorySpace.SharedMaterial ? SharedPlacement(itemId, placement) : placement;
    }

    private static ItemPlacement? SharedPlacement(int itemId, ItemPlacement? placement)
    {
        if (placement is null || !AccountMaterialCatalog.TryGetSlotIndex(itemId, out var slot)) return placement;
        if (placement.ItemKind != AccountMaterialCatalog.Definitions[slot].ItemKind || placement.WorldClass != ItemWorldClass.Interchangeable)
            throw new InvalidDataException($"Shared material {itemId} must be an ordinary material pile.");
        return placement with { Destination = new(BusinessInventorySpace.SharedMaterial, slot, slot), StackLimit = int.MaxValue };
    }

    public ValueTask<BusinessSlot?> WornSlotForAsync(int itemId, CancellationToken cancellationToken) =>
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
    /// The signature was drawn this way because the byte and the archive once disagreed about an
    /// avatar and about a contract creature. They no longer do, and the guard is unchanged: it is
    /// worth having for the reason written above it rather than for that divergence.
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
