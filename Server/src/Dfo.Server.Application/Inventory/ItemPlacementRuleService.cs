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
/// </remarks>
internal sealed class ItemPlacementRuleService(
    IItemPlacementDefinitionSource placements,
    IEquipmentSlotDefinitionSource equipmentSlots) : IItemPlacementRuleService
{
    /// <summary>What the archive's equipment index says every worn item is.</summary>
    private const byte EquipmentKind = 1;

    public ValueTask<ItemPlacement?> PlacementForAsync(
        int itemId, CancellationToken cancellationToken) =>
        placements.GetAsync(itemId, cancellationToken);

    public ValueTask<byte?> WornSlotForAsync(int itemId, CancellationToken cancellationToken) =>
        equipmentSlots.GetAsync(itemId, cancellationToken);

    public async ValueTask<bool> IsEquipmentAsync(int itemId, CancellationToken cancellationToken)
    {
        var placement = await placements.GetAsync(itemId, cancellationToken).ConfigureAwait(false);
        return placement?.ItemKind == EquipmentKind;
    }
}
